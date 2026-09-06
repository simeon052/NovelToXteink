import os, sys, hashlib, io, tempfile, subprocess, shutil, webbrowser, struct
import tkinter as tk
from tkinter import filedialog
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps # ImageOpsを追加
import ebooklib
from ebooklib import epub
from bs4 import BeautifulSoup
import patoolib
from tqdm import tqdm
from flask import Flask, render_template_string, request, jsonify
import base64

# ==========================================
# --- 基本設定 & 定数 ---
# ==========================================
DEF_WIDTH, DEF_HEIGHT = 480, 800
IMG_EXTS = ('.jpg', '.jpeg', '.png', '.webp')
TATE_REPLACE = {
    # --- 既存の記号 ---
    "…": "︙", "‥": "︰", "ー": "丨", "─": "丨", "―": "丨", "-": "丨", "－": "丨", 
    "～": "≀", "〜": "≀", "〰": "≀",
    "「": "﹁", "」": "﹂", "『": "﹃", "』": "﹄",
    "（": "︵", "）": "︶", "(": "︵", ")": "︶",
    "【": "︻", "】": "︼", "〔": "︹", "〕": "︺",
    "［": "﹇", "］": "﹈", "[": "﹇", "]": "﹈",
    "｛": "︷", "｝": "︸", "{": "︷", "}": "︸",
    "＜": "︿", "＞": "﹀", "<": "︿", ">": "﹀",
    "《": "︽", "》": "︾",
    "、": "︑", "。": "︒",

    # --- 数学記号・演算子を追加 ---
    "＝": "‖", "=": "‖",    # イコールは縦向きの二重線に
    "＋": "＋",              # プラスはそのまま（フォントにより自動で馴染みます）
    "±": "∓",               # プラスマイナス
    "×": "×",              # かける（そのまま）
    "÷": "÷",              # わる（そのまま）
    "≠": "⧘",               # 等しくない
    "≒": "≓",               # 近似
    "≡": "⦀",               # 合同（三重線）
    "∞": "∞",              # 無限（フォントにより回転が必要な場合あり）
    "：": "‥",               # コロン（比率）は縦に並ぶ二点リーダー代用が読みやすい
    "；": "；",              # セミコロン
    
    # --- 新しい記号 ---
    "～": "丨",              # 波引波を縦書き用に変換（より確実）
}

KUTOTEN_OFFSET_X, KUTOTEN_OFFSET_Y = 18, -8

def get_font_list():
    font_dir = Path(__file__).parent / "Font"
    files = list(font_dir.glob("*.ttf")) + list(font_dir.glob("*.ttc")) + list(font_dir.glob("*.otf"))
    fonts = [f.name for f in files]
    for f in ["C:/Windows/Fonts/msmincho.ttc", "C:/Windows/Fonts/msgothic.ttc"]:
        if os.path.exists(f): fonts.append(f)
    return fonts if fonts else ["(フォントなし)"]

def draw_char_tate(draw, char, pos_tuple, font, f_size):
    curr_x, curr_y = pos_tuple
    
    # --- 2文字（！？や！！）を横並びにする精密調整 ---
    if len(char) == 2:
        # 1文字の枠（f_size）を2分割して、それぞれのセンターに配置
        sub_f_size = int(f_size * 0.75)  # 少し小さめにして半角感を出す
        sub_font = ImageFont.truetype(font.path, sub_f_size)
        
        # 1文字あたりの占有幅
        half_w = f_size // 2
        # 文字自体の幅を考慮した微調整オフセット
        char_offset = (half_w - sub_f_size) // 2
        
        # 1文字目（左側）
        draw.text((curr_x + char_offset + 10, curr_y), char[0], font=sub_font, fill=0)
        # 2文字目（右側）
        draw.text((curr_x + half_w + char_offset + 10, curr_y), char[1], font=sub_font, fill=0)
        return

    # --- 通常の1文字処理（以下、変更なし） ---
    char = TATE_REPLACE.get(char, char)
    if char in "、。":
        draw.text((curr_x + KUTOTEN_OFFSET_X, curr_y + KUTOTEN_OFFSET_Y), char, font=font, fill=0)
    else:
        draw.text((curr_x, curr_y), char, font=font, fill=0)

def apply_xtc_filter(img, dither, threshold, w, h):
    img = img.convert("L")
    img.thumbnail((w, h), Image.Resampling.LANCZOS)
    background = Image.new("L", (w, h), 255)
    offset = ((w - img.width) // 2, (h - img.height) // 2)
    background.paste(img, offset)
    if dither:
        res = background.convert("1", dither=Image.FLOYDSTEINBERG)
    else:
        res = background.point(lambda x: 255 if x > threshold else 0, mode="1")
    return res

def generate_preview_base64(args):
    try:
        w, h = 480, 800
        dither = args.get('dither') == 'true'
        threshold = int(args.get('threshold', 128))
        mode = args.get('mode', 'text')

        if mode == 'image':
            # --- 画像プレビューモード ---
            file_b64 = args.get('file_b64')
            if file_b64:
                # ユーザーがアップロードした画像をデコードして読み込む
                header, encoded = file_b64.split(",", 1)
                src_img = Image.open(io.BytesIO(base64.b64decode(encoded)))
            else:
                # 画像がない場合はダミーを作成
                src_img = Image.new('L', (w, h), 255)
                draw = ImageDraw.Draw(src_img)
                for i in range(16):
                    draw.rectangle([0, i * (h//16), w, (i+1) * (h//16)], fill=int(255 * (i/15)))
            
            # フィルタ（ディザリング・しきい値）を適用
            res_img = apply_xtc_filter(src_img, dither, threshold, w, h)
        
        else:
            # --- 文章プレビューモード (PDF Page 5 のロジック) ---
            res_img = Image.new('L', (w, h), 255)
            draw = ImageDraw.Draw(res_img)
            f_size = int(args.get('font_size', 36))
            r_size = int(args.get('ruby_size', 14))
            l_space = int(args.get('line_spacing', 56))
            m_t, m_b, m_r = int(args.get('margin_t', 12)), int(args.get('margin_b', 12)), int(args.get('margin_r', 12))
            
            font_file = args.get('font_file', "")
            font_p = Path(font_file) if font_file.startswith("C:") else (Path(__file__).parent / "Font" / font_file)
            if not font_p.exists(): return ""
            font = ImageFont.truetype(str(font_p), f_size)
            ruby_font = ImageFont.truetype(str(font_p), r_size)

            iroha = [
                ("色", "いろ"), ("は", ""), ("匂", "にほ"), ("へど", ""), ("散", "ち"), ("りぬるを", ""), ("、", ""),
                ("我", "わが"), ("世", "よ"), ("誰", "たれ"), ("ぞ", ""), ("常", "つね"), ("ならむ", ""), ("。", ""),
                ("有", "う"), ("為", "ゐ"), ("の", ""), ("奥", "おく"), ("山", "やま"), ("今", "け"), ("日", "ふ"), 
                ("越", "こ"), ("えて", ""), ("、", ""),
                ("浅", "あさ"), ("き", ""), ("夢", "ゆめ"), ("見", "み"), ("じ", ""), ("酔", "ゑ"), ("ひもせず", ""), ("。", "")
            ]
            
            curr_x, curr_y = w - f_size - (r_size + 4) - m_r, m_t
            running = True
            # 【ループ】画面がいっぱいになるまで繰り返す
            while running:
                for kanji, ruby in iroha:
                    for char in kanji:
                        # 通常の改行判定（行末までいったら次へ）
                        if curr_y > h - m_b - f_size:
                            curr_y, curr_x = m_t, curr_x - l_space
                            if curr_x < 12: running = False; break
                        
                        # 描画
                        draw_char_tate(draw, char, (curr_x, curr_y), font, f_size)
                        
                        # ルビ描画
                        if ruby:
                            for r_idx, r_char in enumerate(ruby):
                                ry = curr_y + r_idx * (r_size + 2)
                                if ry < h - m_b:
                                    draw.text((curr_x + f_size + 4, ry), r_char, font=ruby_font, fill=0)
                        
                        curr_y += f_size + 2

                        # 【修正】「。」の後に強制改行（「、」では改行しない）
                        if char == "。":
                            curr_y = m_t
                            curr_x -= l_space
                            if curr_x < 12: running = False; break
                    
                    if not running: break

        # --- 共通の返却処理 ---
        buf = io.BytesIO()
        res_img.convert("RGB").save(buf, format='PNG')
        return base64.b64encode(buf.getvalue()).decode('utf-8')

    except Exception as e:
        print(f"Preview Error: {e}")
        return ""


def png_to_xtg_bytes(img, w, h, args_obj):
    bw_img = apply_xtc_filter(img, args_obj.dither, args_obj.threshold, w, h)
    # 引数にナイトモードがあり、かつ有効な場合のみ反転
    if getattr(args_obj, 'night_mode', False):
        bw_img = ImageOps.invert(bw_img.convert("L"))
    row_bytes = (w + 7) // 8
    data = bytearray(row_bytes * h); pixels = bw_img.load()
    for y in range(h):
        for x in range(w):
            if pixels[x, y] > 0: data[y * row_bytes + (x // 8)] |= 1 << (7 - (x % 8))
    md5 = hashlib.md5(data).digest()[:8]
    return struct.pack("<4sHHBBI8s", b"XTG\x00", w, h, 0, 0, len(data), md5) + data
# ※前半部分の TATE_REPLACE に "～": "丨" (または波引波 "〰") を追加するとより確実です。
# ここでは後半のロジックとUI・Flaskルートをすべて修正した状態で提示します。

def build_xtc(xtg_blobs, out_path, w, h):
    cnt = len(xtg_blobs)
    if cnt == 0: return
    idx_off, data_off = 48, 48 + cnt * 16
    idx_table, curr_off = bytearray(), data_off
    for b in xtg_blobs:
        idx_table += struct.pack("<Q I H H", curr_off, len(b), w, h); curr_off += len(b)
    header = struct.pack("<4sHHBBBBIQQQQ", b"XTC\x00", 1, cnt, 1, 0, 0, 0, 0, 0, idx_off, data_off, 0)
    with open(out_path, "wb") as f: f.write(header); f.write(idx_table); [f.write(b) for b in xtg_blobs]

def process_image_data(data, args):
    """画像データを読み込んでサイズ調整する"""
    try:
        with Image.open(io.BytesIO(data)) as s_img:
            # 白黒(L)に変換
            s_img = s_img.convert("L")
            # 指定サイズ(480x800等)に収まるようリサイズ
            s_img.thumbnail((args.width, args.height), Image.Resampling.LANCZOS)
            # 背景(白)を作成して中央に配置
            bg = Image.new('L', (args.width, args.height), 255)
            bg.paste(s_img, ((args.width - s_img.width)//2, (args.height - s_img.height)//2))
            # XTG形式に変換して返す
            return png_to_xtg_bytes(bg, args.width, args.height, args)
    except Exception as e:
        print(f"画像処理エラー: {e}")
        return None

def process_archive(archive_path, args):
    if args.manga:
        print(f"\n[Manga Mode] 準備開始: {archive_path.name}")
        komawari_py = Path(__file__).parent / "Komawari.py"
        work_base = archive_path.parent / "_manga_work"
        
        # 1. 作業フォルダのクリーンアップと作成
        if work_base.exists(): shutil.rmtree(work_base)
        work_base.mkdir(parents=True, exist_ok=True)
        
        xtg_blobs = [] # ここに変換データを溜める
        try:
            # 2. ZIPをコピーして Komawari.py を実行
            shutil.copy2(archive_path, work_base)
            subprocess.run([sys.executable, str(komawari_py), str(work_base)], check=True)
            
            # 3. 生成された _c.zip を探す
            panel_zip = work_base / f"{archive_path.stem}_c.zip"
            if not panel_zip.exists():
                print(f"【警告】{panel_zip.name} が生成されませんでした。")
                return

            # 4. 画像を解凍して186枚を確実にリストアップ
            extract_dir = work_base / "_extracted"
            extract_dir.mkdir(exist_ok=True)
            patoolib.extract_archive(str(panel_zip), outdir=str(extract_dir), verbosity=-1)
            
            img_files = sorted([p for p in extract_dir.rglob("*") if p.suffix.lower() in IMG_EXTS])
            print(f"デバッグ: {len(img_files)} 枚の画像を検出しました。変換を開始します...")

            # 5. 【最重要】1枚ずつ確実に変換するループ
            for img_p in tqdm(img_files, desc="変換中", unit="枚", leave=False):
                try:
                    with open(img_p, 'rb') as f:
                        # 画像をXTC用データに変換してリストに追加
                        blob = process_image_data(f.read(), args)
                        if blob:
                            xtg_blobs.append(blob)
                except Exception as e:
                    print(f"画像変換スキップ ({img_p.name}): {e}")
            
            # 6. すべての画像が溜まったら、一時フォルダが消える「前」に書き出す
            if len(xtg_blobs) > 0:
                out_xtc_path = archive_path.parent / f"{archive_path.stem}_c.xtc"
                build_xtc(xtg_blobs, out_xtc_path, args.width, args.height)
                print(f"✓ マンガモード完了: {out_xtc_path.name}")
                print(f"保存場所: {out_xtc_path.absolute()}")
            else:
                print("【エラー】変換された画像データが1件もありませんでした。")

        except Exception as e:
            print(f"【エラー】Manga処理中に致命的な問題が発生: {e}")
        finally:
            # 7. 最後にフォルダを掃除して終了
            if work_base.exists(): shutil.rmtree(work_base)
            print("作業フォルダの掃除が完了しました。")
            return # 通常モードを絶対に動かさないためにここで終了

    # --- 通常モード (args.manga が False の時だけ実行) ---
    print(f"\n[通常モード] {archive_path.name}")

    # ==========================================
    # --- 通常モード (Mangaモードがオフの時だけ実行) ---
    # ==========================================
    # マンガモード実行済みの場合はここで確実に終了させる（二重処理防止）
    if args.manga: return

    print(f"\n[通常モード開始] {archive_path.name}")
    
    # 【修正箇所】保存先のパスを定義
    normal_xtc_path = archive_path.with_suffix(".xtc")
    xtg_blobs = []

    # 一時フォルダを作成して解凍
    with tempfile.TemporaryDirectory() as tmpdir:
        try:
            patoolib.extract_archive(str(archive_path), outdir=tmpdir, verbosity=-1)
        except Exception as e:
            print(f"【エラー】解凍に失敗しました: {e}")
            return
        
        # 画像ファイルをスキャン（サブフォルダまで探す）
        img_files = sorted([p for p in Path(tmpdir).rglob("*") if p.suffix.lower() in IMG_EXTS])
        print(f"デバッグ: {len(img_files)} 枚の画像を検出しました。変換を開始します...")

        # 1枚ずつ変換してリストに溜める
        for img_p in tqdm(img_files, desc="通常変換中", unit="枚", leave=False):
            try:
                with open(img_p, 'rb') as f:
                    # 前に定義した process_image_data を使用
                    blob = process_image_data(f.read(), args)
                    if blob:
                        xtg_blobs.append(blob)
            except Exception as e:
                print(f"画像スキップ ({img_p.name}): {e}")
                continue

    # すべての画像が溜まったら XTC ファイルを書き出す
    if len(xtg_blobs) > 0:
        build_xtc(xtg_blobs, normal_xtc_path, args.width, args.height)
        print(f"✓ 通常変換完了: {normal_xtc_path.name}")
    else:
        print("【エラー】変換できる画像が見つかりませんでした。")



def process_epub(epub_path, font_path, args):
    book = epub.read_epub(str(epub_path))
    font, ruby_font = ImageFont.truetype(font_path, args.font_size), \
                      ImageFont.truetype(font_path, args.ruby_size)
    xtg_blobs = []

    def add_page(image, is_illustration=False):
        temp_args = type('Args', (object,), vars(args))()
        if is_illustration: temp_args.night_mode = False
        xtg_blobs.append(png_to_xtg_bytes(image, args.width, args.height, temp_args))

    image_map = {item.file_name: item.get_content() for item in \
                 book.get_items_of_type(ebooklib.ITEM_IMAGE)}
    docs = []
    for item_id in book.spine:
        it = book.get_item_with_id(item_id[0] if isinstance(item_id, tuple) else item_id)
        if it and it.get_type() == ebooklib.ITEM_DOCUMENT: docs.append(it)

    for item in tqdm(docs, desc="描画中", unit="章", leave=False):
        soup = BeautifulSoup(item.get_content(), 'html.parser')
        body = soup.find('body') or soup
        img = Image.new('L', (args.width, args.height), 255); draw = ImageDraw.Draw(img)
        curr_x, curr_y = args.width - args.font_size - (args.ruby_size + 4) - args.margin_r, \
                         args.margin_t

        def walk_xml(node):
            nonlocal img, draw, curr_x, curr_y
            
            # --- 画像・扉絵・外字の処理 ---
            is_img_tag = (node.name == 'img' or node.name == 'image')
            has_src = (node.get('src') or node.get('xlink:href'))
            if is_img_tag and has_src:
                src = node.get('src', node.get('xlink:href', '')).split('/')[-1]
                img_data = next((v for k, v in image_map.items() if src in k), None)
                if img_data:
                    try:
                        with Image.open(io.BytesIO(img_data)) as s_img:
                            aspect = s_img.width / s_img.height if s_img.height > 0 else 1
                            is_illustration = (s_img.height >= 400 or (aspect > 0.5 and s_img.height > args.font_size * 4))
                            if not is_illustration:
                                # 外字・アイコン（文字として処理）
                                if curr_y + args.font_size > args.height - args.margin_b:
                                    curr_y, curr_x = args.margin_t, curr_x - args.line_spacing
                                    if curr_x < args.margin_l:
                                        add_page(img); img = Image.new('L', (args.width, args.height), 255)
                                        draw, curr_x, curr_y = ImageDraw.Draw(img), \
                                        args.width-args.font_size-(args.ruby_size+4)-args.margin_r, args.margin_t
                                scale = args.font_size / s_img.height
                                char_img = s_img.resize((int(s_img.width*scale), args.font_size), Image.Resampling.LANCZOS).convert("L")
                                if getattr(args, 'night_mode', False): char_img = ImageOps.invert(char_img)
                                img.paste(char_img, (curr_x + (args.font_size - char_img.width)//2, curr_y + 4))
                                curr_y += args.font_size + 4
                            else:
                                # 挿絵・扉絵（ページ丸ごと）
                                if any(p < 255 for p in img.getdata()): add_page(img)
                                add_page(s_img, is_illustration=True)
                                img = Image.new('L', (args.width, args.height), 255); draw = ImageDraw.Draw(img)
                                curr_x, curr_y = args.width - args.font_size - (args.ruby_size + 4) - args.margin_r, args.margin_t
                    except Exception as e:
                        print(f"画像処理エラー ({src}): {e}")
                return

            # --- ルビの処理 ---
            if node.name == 'ruby':
                rb = "".join(t.get_text() if hasattr(t, 'get_text') else str(t) for t in node.contents if getattr(t, 'name', '') != 'rt')
                rt = "".join(t.get_text() if hasattr(t, 'get_text') else "" for t in node.find_all('rt'))
                ruby_base_y = curr_y
                for char in rb:
                    if curr_y > args.height - args.margin_b - args.font_size:
                        curr_y, curr_x = args.margin_t, curr_x - args.line_spacing
                        if curr_x < args.margin_l:
                            add_page(img); img = Image.new('L', (args.width, args.height), 255)
                            draw, curr_x, curr_y = ImageDraw.Draw(img), \
                            args.width-args.font_size-(args.ruby_size+4)-args.margin_r, args.margin_t
                        ruby_base_y = curr_y
                    draw_char_tate(draw, char, (curr_x, curr_y), font, args.font_size); curr_y += args.font_size + 2
                if rt:
                    rb_h, rt_h = len(rb)*(args.font_size+2), len(rt)*(args.ruby_size+2)
                    ry = ruby_base_y + (rb_h - rt_h)//2
                    for r_char in rt:
                        if args.margin_t <= ry < args.height-args.margin_b:
                            draw.text((curr_x+args.font_size+4, ry), r_char, font=ruby_font, fill=0)
                        ry += args.ruby_size+2
                return

            # --- テキストノードの処理（横並び記号対応） ---
            for child in node.contents:
                if isinstance(child, str):
                    text = child.replace('\n', '').strip()
                    i = 0
                    while i < len(text):
                        # 改ページ・改行判定
                        if curr_y > args.height - args.margin_b - args.font_size:
                            curr_y, curr_x = args.margin_t, curr_x - args.line_spacing
                            if curr_x < args.margin_l:
                                add_page(img); img = Image.new('L', (args.width, args.height), 255)
                                draw, curr_x, curr_y = ImageDraw.Draw(img), \
                                args.width-args.font_size-(args.ruby_size+4)-args.margin_r, args.margin_t
                        
                        # 感嘆符・疑問符の2文字横並び判定
                        if i + 1 < len(text) and text[i] in "！？!?" and text[i+1] in "！？!?":
                            draw_char_tate(draw, text[i:i+2], (curr_x, curr_y), font, args.font_size)
                            i += 2
                        else:
                            draw_char_tate(draw, text[i], (curr_x, curr_y), font, args.font_size)
                            i += 1
                        
                        curr_y += args.font_size + 2
                elif child.name:
                    walk_xml(child)

            # --- ブロック要素による改行 ---
            if node.name in ['p', 'div', 'h1', 'h2', 'h3', 'section', 'header']:
                if curr_y > args.margin_t:
                    curr_y, curr_x = args.margin_t, curr_x - args.line_spacing
                    if curr_x < args.margin_l:
                        add_page(img); img = Image.new('L', (args.width, args.height), 255)
                        draw, curr_x, curr_y = ImageDraw.Draw(img), \
                        args.width-args.font_size-(args.ruby_size+4)-args.margin_r, args.margin_t

        walk_xml(body)
        if any(p < 255 for p in img.getdata()): add_page(img)

    build_xtc(xtg_blobs, epub_path.with_suffix(".xtc"), args.width, args.height)


app = Flask(__name__)
HTML_UI = """
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="UTF-8"><title>縦書きXTC Web-Ui & Viewer</title>
<style>
body { font-family: 'Meiryo', sans-serif; background: #f0f4f8; margin: 0; padding: 20px; display: flex; justify-content: center; height: 100vh; overflow: hidden; }
.main-container { display: flex; gap: 20px; align-items: flex-start; max-height: 95vh; }
.viewer-container { width: 340px; background: #333; padding: 15px; border-radius: 12px; display: flex; flex-direction: column; color: white; box-shadow: 0 5px 15px rgba(0,0,0,0.3); }
.viewer-screen { background: #000; width: 100%; aspect-ratio: 480 / 800; border: 2px solid #555; margin-bottom: 10px; overflow: hidden; display: flex; align-items: center; justify-content: center; }
#viewerCanvas { max-width: 100%; max-height: 100%; image-rendering: pixelated; background: #000; }
.box { background: white; padding: 25px; border-radius: 12px; box-shadow: 0 5px 15px rgba(0,0,0,0.08); width: 480px; overflow-y: auto; max-height: 90vh; }
.preview-box { width: 300px; background: #222; padding: 15px; border-radius: 10px; text-align: center; color: white; }
.preview-screen { background: white; width: 100%; aspect-ratio: 480 / 800; margin: 10px 0; border: 2px solid #000; overflow: hidden; display: flex; align-items: center; justify-content: center; }
#preview_img { max-width: 100%; max-height: 100%; }
.row { display: flex; align-items: center; margin-bottom: 10px; font-size: 0.9em; border-bottom: 1px solid #f0f0f0; padding-bottom: 5px; }
.label { width: 140px; font-weight: bold; }
.input-area { flex: 1; display: flex; align-items: center; gap: 5px; }
input, select { padding: 6px; border: 1px solid #ddd; border-radius: 4px; width: 100%; }
input[type="number"] { width: 45px; }
.btn-select { padding: 4px 8px; font-size: 0.75em; cursor: pointer; background: #f8f9fa; border: 1px solid #ccc; border-radius: 4px; white-space: nowrap; }
.section-title { background: #3498db; color: white; padding: 5px 10px; border-radius: 4px; font-size: 0.85em; margin: 10px 0; font-weight: bold; }
.btn-run { background: #e74c3c; color: white; padding: 12px; border: none; border-radius: 8px; width: 100%; cursor: pointer; font-weight: bold; margin-top: 10px; box-shadow: 0 4px 0 #c0392b; }
.v-btn { background: #555; color: white; border: 1px solid #777; padding: 5px 10px; cursor: pointer; border-radius: 4px; font-weight: bold; }
.tab-btn.active { background: #3498db; color: white; }
</style>
</head>
<body>
<div class="main-container">
    <div class="viewer-container">
        <div style="font-size:0.85em; font-weight:bold; margin-bottom:8px; text-align:center;">XTC ビューア</div>
        <div class="viewer-screen"><canvas id="viewerCanvas"></canvas></div>
        <div class="viewer-controls">
            <input type="file" id="xtcInput" accept=".xtc" style="font-size:0.75em;">
            <div class="viewer-btns" style="display:flex; justify-content:space-between; margin-top:5px;">
                <button class="v-btn" id="v-prev">◀ 前</button>
                <div style="font-size:0.8em;"><span id="v-cur">0</span> / <span id="v-total">0</span></div>
                <button class="v-btn" id="v-next">次 ▶</button>
            </div>
            <input type="range" id="v-seekbar" min="0" max="0" value="0" style="width:100%;">
        </div>
    </div>
    <div class="box">
        <h1>縦書きXTC 変換設定</h1>
        <form id="runForm">
            <div class="row"><div class="label">変換対象パス</div><div class="input-area">
                <input type="text" name="target" id="target_path" value="." onchange="updatePreview()">
                <button type="button" class="btn-select" onclick="selectPath('folder')">フォルダ</button>
                <button type="button" class="btn-select" onclick="selectPath('file')">ファイル</button>
            </div></div>
            <div class="section-title">フォントと文章の設定</div>
            <div class="row"><div class="label">使用フォント</div><select name="font_file" id="font_file" onchange="updatePreview()">
                {% for f in fonts %}<option value="{{ f }}">{{ f }}</option>{% endfor %}
            </select></div>
            <div class="row"><div class="label">文字サイズ・行間</div>
                本文:<input type="number" name="font_size" id="font_size" value="36" onchange="updatePreview()">
                ルビ:<input type="number" name="ruby_size" id="ruby_size" value="14" onchange="updatePreview()">
                行間:<input type="number" name="line_spacing" id="line_spacing" value="56" onchange="updatePreview()">
            </div>
            <div class="row"><div class="label">ページ余白設定</div>
                上:<input type="number" name="margin_t" id="margin_t" value="12" onchange="updatePreview()">
                下:<input type="number" name="margin_b" id="margin_b" value="12" onchange="updatePreview()">
                右:<input type="number" name="margin_r" id="margin_r" value="12" onchange="updatePreview()">
                左:<input type="number" name="margin_l" id="margin_l" value="12" onchange="updatePreview()">
            </div>
            <div class="section-title">ディザリングと画像調整</div>
            <div class="row"><div class="label">ディザリング設定</div><div class="input-area">
                <input type="checkbox" name="dither" id="d_check" checked onchange="document.getElementById('th').disabled=this.checked; updatePreview();" style="width:auto;"> 有効
                <span style="margin-left:10px;">しきい値:</span><input type="number" name="threshold" id="th" value="128" disabled onchange="updatePreview()">
            </div></div>
            <div class="row"><div class="label">プレビュー用画像</div><input type="file" id="preview_file" accept="image/*" onchange="handleFileSelect(this)"></div>
            <div class="section-title">実行オプション</div>
            <div class="row" style="border:none;">
                <label style="margin-right:15px;"><input type="checkbox" name="night_mode" id="n_check" onchange="updatePreview()" style="width:auto;"> 白黒反転</label>
                <label style="margin-right:15px;"><input type="checkbox" name="manga" style="width:auto;"> 1ｺﾏ切り</label>
                <label><input type="checkbox" name="open_folder" checked style="width:auto;"> 完了後にﾌｫﾙﾀﾞを開く</label>
            </div>
            <button type="button" id="runBtn" class="btn-run" onclick="submitRun()">変換処理を開始する</button>
            <div id="statusMsg" style="margin-top:10px; font-weight:bold; text-align:center; min-height:1.2em;"></div>
        </form>
    </div>
    <div class="preview-box">
        <div class="tab-btns" style="display:flex; gap:4px; justify-content:center; margin-bottom:5px;">
            <button class="tab-btn active" onclick="setMode('text', this)">文章プレビュー</button>
            <button class="tab-btn" onclick="setMode('image', this)">画像プレビュー</button>
        </div>
        <div class="preview-screen"><img id="preview_img" src=""></div>
        <div style="font-size:0.7em; opacity:0.6;">XTC変換前Preview</div>
    </div>
</div>
<script>
let xtcBuf, xtcPages = [], xtcCurIdx = 0;
const vCanvas = document.getElementById('viewerCanvas'), vCtx = vCanvas.getContext('2d');
const vCur = document.getElementById('v-cur'), vTotal = document.getElementById('v-total'), vSeek = document.getElementById('v-seekbar');

document.getElementById('xtcInput').onchange = async (e) => {
    const file = e.target.files[0]; if(!file) return;
    xtcBuf = await file.arrayBuffer(); const v = new DataView(xtcBuf);
    if (v.getUint8(0) !== 0x58) return alert("Not XTC");
    const count = v.getUint16(6, true); xtcPages = [];
    for (let i=0; i<count; i++) {
        const o = 48 + (i*16);
        xtcPages.push({ off: Number(v.getBigUint64(o, true)), len: v.getUint32(o+8, true) });
    }
    vTotal.innerText = xtcPages.length; vSeek.max = xtcPages.length - 1; xtcCurIdx = 0; renderXTC();
};

function renderXTC() {
    if (!xtcPages || xtcPages.length === 0) return;
    const p = xtcPages[xtcCurIdx], v = new DataView(xtcBuf, p.off, p.len);
    const w = v.getUint16(4, true), h = v.getUint16(6, true), rowBytes = Math.ceil(w/8);
    vCanvas.width = w; vCanvas.height = h;
    const id = vCtx.createImageData(w, h);
    for (let y=0; y<h; y++) {
        const rOff = 22 + (y * rowBytes);
        for (let x=0; x<w; x++) {
            const bIdx = rOff + (x >> 3);
            const i = (y * w + x) * 4;
            let val = (bIdx < p.len && (v.getUint8(bIdx) >> (7 - (x % 8))) & 1) ? 255 : 0;
            id.data[i] = id.data[i+1] = id.data[i+2] = val; id.data[i+3] = 255;
        }
    }
    vCtx.putImageData(id, 0, 0); vCur.innerText = xtcCurIdx + 1; vSeek.value = xtcCurIdx;
}

document.getElementById('v-prev').onclick = () => { xtcCurIdx = Math.max(0, xtcCurIdx - 1); renderXTC(); };
document.getElementById('v-next').onclick = () => { xtcCurIdx = Math.min(xtcPages.length-1, xtcCurIdx + 1); renderXTC(); };
vSeek.oninput = () => { xtcCurIdx = parseInt(vSeek.value); renderXTC(); };

let currentMode = 'text', selectedFileB64 = null;
function setMode(m, el) { currentMode = m; document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active')); el.classList.add('active'); updatePreview(); }
function handleFileSelect(input) {
    if (input.files && input.files[0]) {
        const reader = new FileReader();
        reader.onload = (e) => {
            selectedFileB64 = e.target.result;
            // 【重要】画像を選んだら、強制的に「画像プレビュー」タブをアクティブにする
            const imgTabBtn = document.querySelectorAll('.tab-btn')[1]; // 2番目のボタンが画像用
            setMode('image', imgTabBtn); 
        };
        reader.readAsDataURL(input.files[0]);
    }
}
function updatePreview() {
    const data = {
        mode: currentMode, file_b64: selectedFileB64,
        font_file: document.getElementById('font_file').value, font_size: document.getElementById('font_size').value,
        ruby_size: document.getElementById('ruby_size').value, line_spacing: document.getElementById('line_spacing').value,
        margin_t: document.getElementById('margin_t').value, margin_b: document.getElementById('margin_b').value,
        margin_r: document.getElementById('margin_r').value, margin_l: document.getElementById('margin_l').value,
        dither: document.getElementById('d_check').checked ? 'true' : 'false', threshold: document.getElementById('th').value,
        night_mode: document.getElementById('n_check').checked ? 'true' : 'false'
    };
    fetch('/get_preview', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(data) })
    .then(r=>r.json()).then(d=>{ if(d.img) document.getElementById('preview_img').src = "data:image/png;base64," + d.img; });
}
function selectPath(t) { fetch('/select_path?type='+t).then(r=>r.json()).then(d=>{ if(d.path) { document.getElementById('target_path').value=d.path; updatePreview(); } }); }
async function submitRun() {
    const btn = document.getElementById('runBtn'); const msg = document.getElementById('statusMsg');
    const formData = new FormData(document.getElementById('runForm'));
    btn.disabled = true; msg.style.color = "#3498db"; msg.innerText = "処理中...";
    try {
        const response = await fetch('/run', { method: 'POST', body: formData });
        const result = await response.json(); msg.style.color = result.status === "success" ? "#27ae60" : "#e74c3c"; msg.innerText = result.message;
    } catch (e) { msg.innerText = "エラーが発生しました"; } finally { btn.disabled = false; }
}
window.onload = updatePreview;
</script>
</body>
</html>
"""

@app.route('/')
def index():
    return render_template_string(HTML_UI, fonts=get_font_list())

@app.route('/get_preview', methods=['POST'])
def get_preview():
    return jsonify(img=generate_preview_base64(request.json))

@app.route('/select_path')
def select_path():
    t = request.args.get('type')
    root = tk.Tk(); root.withdraw(); root.attributes('-topmost', True)
    path = filedialog.askopenfilename() if t == 'file' else filedialog.askdirectory()
    root.destroy(); return jsonify(path=path)

@app.route('/run', methods=['POST'])
def run():
    class Args: pass
    a = Args(); tp = Path(request.form.get('target'))
    a.width, a.height = 480, 800
    a.font_size, a.ruby_size = int(request.form.get('font_size')), int(request.form.get('ruby_size'))
    a.line_spacing = int(request.form.get('line_spacing'))
    a.margin_t, a.margin_b, a.margin_r, a.margin_l = int(request.form.get('margin_t')), int(request.form.get('margin_b')), int(request.form.get('margin_r')), int(request.form.get('margin_l'))
    a.dither, a.manga, a.night_mode = 'dither' in request.form, 'manga' in request.form, 'night_mode' in request.form
    a.threshold = int(request.form.get('threshold', 128))
    fp = Path(__file__).parent / "Font" / request.form.get('font_file') if not request.form.get('font_file').startswith("C:") else Path(request.form.get('font_file'))
    
    try:
        targets = [tp] if tp.is_file() else list(tp.glob("*"))
        for p in targets:
            # 処理済みファイルやXTC自体はスキップ
            if p.stem.endswith("_c") or p.suffix.lower() == ".xtc": continue
            
            if p.suffix.lower() == '.epub': 
                process_epub(p, str(fp), a)
            elif p.suffix.lower() in ('.zip', '.rar', '.cbz', '.cbr'): 
                process_archive(p, a)
                
        # 全ファイルのループが終わったらフォルダを開く
        if 'open_folder' in request.form: 
            os.startfile(tp.parent if tp.is_file() else tp)
            
        return jsonify(status="success", message="変換完了しました。")
    except Exception as e: 
        return jsonify(status="error", message=f"エラー発生: {e}")
if __name__ == "__main__":
    webbrowser.open("http://127.0.0.1:5000"); app.run(port=5000)
