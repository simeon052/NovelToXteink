import cv2
import os
import sys
import glob
import numpy as np
import shutil
import patoolib

def process_and_save_zip(base_name, image_paths, output_root):
    """
    base_name: 元のフォルダ名やアーカイブ名（拡張子なし）
    image_paths: その作品に含まれる全画像のリスト
    output_root: ZIPを保存する親フォルダ
    """
    if not image_paths: return

    # 作業用の一時保存フォルダ
    temp_crop_dir = os.path.join(output_root, f"_temp_{base_name}")
    if os.path.exists(temp_crop_dir): shutil.rmtree(temp_crop_dir)
    os.makedirs(temp_crop_dir)

    success_count = 0
    # 画像パス順に処理（ファイル名順）
    for img_path in sorted(image_paths):
        n = np.fromfile(img_path, np.uint8)
        img = cv2.imdecode(n, cv2.IMREAD_COLOR)
        if img is None: continue

        # 画像処理（コマ検出）
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 245, 255, cv2.THRESH_BINARY_INV)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        panels = []
        for cnt in contours:
            x, y, w, h = cv2.boundingRect(cnt)
            if w > 100 and h > 100: panels.append((x, y, w, h))

        # 日本語読み順ソート（右上から左下）
        panels.sort(key=lambda p: (p[1] // 300, -p[0]))

        img_orig_name, img_ext = os.path.splitext(os.path.basename(img_path))

        for i, (x, y, w, h) in enumerate(panels):
            panel_img = img[y:y+h, x:x+w]
            # 命名: (元の画像名)_(二桁番号).(拡張子)
            save_name = f"{img_orig_name}_{i+1:02d}{img_ext}"
            save_path = os.path.join(temp_crop_dir, save_name)
            
            res, img_encode = cv2.imencode(img_ext, panel_img)
            if res:
                with open(save_path, mode='w+b') as f:
                    img_encode.tofile(f)
                success_count += 1

    # ZIP作成
    if success_count > 0:
        zip_output_path = os.path.join(output_root, f"{base_name}_c")
        shutil.make_archive(zip_output_path, 'zip', root_dir=temp_crop_dir)
        print(f"作成完了: {base_name}_c.zip ({success_count}コマ)")
    
    # 作業フォルダ削除
    shutil.rmtree(temp_crop_dir)

def main(target_folder):
    target_folder = os.path.abspath(target_folder)
    
    # 1. 圧縮ファイル（rar, zip, cbz, cbr）を個別に処理
    for ext in ("*.rar", "*.zip", "*.cbz", "*.cbr"):
        for arch in glob.glob(os.path.join(target_folder, ext)):
            base_name = os.path.splitext(os.path.basename(arch))[0]
            print(f"アーカイブ処理中: {base_name}")
            
            extract_work = os.path.join(target_folder, f"_extract_{base_name}")
            if os.path.exists(extract_work): shutil.rmtree(extract_work)
            os.makedirs(extract_work)
            
            try:
                patoolib.extract_archive(arch, outdir=extract_work, verbosity=-1)
                imgs = []
                for iext in ("*.jpg", "*.jpeg", "*.png", "*.webp"):
                    imgs.extend(glob.glob(os.path.join(extract_work, "**", iext), recursive=True))
                process_and_save_zip(base_name, imgs, target_folder)
            finally:
                shutil.rmtree(extract_work)

    # 2. 子フォルダを個別に処理
    for entry in os.scandir(target_folder):
        if entry.is_dir() and not entry.name.startswith("_"):
            print(f"フォルダ処理中: {entry.name}")
            imgs = []
            for iext in ("*.jpg", "*.jpeg", "*.png", "*.webp"):
                imgs.extend(glob.glob(os.path.join(entry.path, "**", iext), recursive=True))
            process_and_save_zip(entry.name, imgs, target_folder)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        main(sys.argv[1])
    else:
        print("使い方: python Komawari.py フォルダのパス")
