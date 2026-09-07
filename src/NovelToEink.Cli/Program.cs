using System;
using System.IO;
using System.Linq;
using NovelToEink.Xtc;
using NovelToEink.XtcConverter;

namespace NovelToEink.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 1;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "help":
                    PrintHelp();
                    return 0;
                case "--list-fonts":
                    PrintFonts();
                    return 0;
            }

            string? inputPath = null;
            string? outputDirectory = null;
            string? fontFile = null;
            var device = XteinkDevice.X4Pro;
            var vertical = true;
            var fontSize = 36;
            var padding = (Top: 3, Bottom: 0, Left: 0, Right: 0);

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        if (i + 1 >= args.Length) { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        outputDirectory = args[++i];
                        break;

                    case "-f":
                    case "--font":
                        if (i + 1 >= args.Length) { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        fontFile = args[++i];
                        break;

                    case "-d":
                    case "--device":
                        if (i + 1 >= args.Length) { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        if (!XteinkDeviceInfo.TryParse(args[++i], out device))
                        {
                            Console.WriteLine($"Unknown device: {args[i]} (expected X3 or X4Pro)");
                            return 1;
                        }
                        break;

                    case "--horizontal":
                        vertical = false;
                        break;

                    case "--fontsize":
                        if (i + 1 >= args.Length) { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        if (!int.TryParse(args[++i], out fontSize) || fontSize < 8 || fontSize > 200)
                        {
                            Console.WriteLine($"Invalid font size: {args[i]} (expected 8-200)");
                            return 1;
                        }
                        break;

                    case "--padding":
                        if (i + 1 >= args.Length) { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        if (!TryParsePadding(args[++i], out padding))
                        {
                            Console.WriteLine($"Invalid padding: {args[i]} (expected 4 non-negative integers, e.g. 20,20,10,10)");
                            return 1;
                        }
                        break;

                    default:
                        if (!args[i].StartsWith("-"))
                        {
                            inputPath = args[i];
                        }
                        else
                        {
                            Console.WriteLine($"Unknown option: {args[i]}");
                            PrintHelp();
                            return 1;
                        }
                        break;
                }
            }

            if (inputPath == null)
            {
                Console.WriteLine("Missing input argument");
                PrintHelp();
                return 1;
            }

            bool isDirectory = Directory.Exists(inputPath);
            bool isFile = File.Exists(inputPath);
            if (!isFile && !isDirectory)
            {
                Console.WriteLine($"Input not found: {inputPath}");
                return 1;
            }

            var options = new XtcRenderOptions
            {
                Device = device,
                FontFile = fontFile,
                FontSize = fontSize,
                Padding = padding,
                Vertical = vertical,
            };

            var inputDir = isDirectory ? inputPath : Path.GetDirectoryName(inputPath)!;
            var outDir = outputDirectory ?? Path.Combine(inputDir, "converted");

            if (isDirectory)
            {
                var (s, f) = EpubToXtcConverter.ConvertDirectory(inputDir, outDir, options);
                Console.WriteLine($"\nDone. Success={s} Failed={f}");
                return f == 0 ? 0 : 1;
            }

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            var xtcFile = Path.Combine(outDir, fileName + ".xtc");
            Console.WriteLine($"Converting: {inputPath}");
            var ok = XtcConverterLibrary.ConvertEpubToXtc(inputPath, xtcFile, options);
            if (ok) Console.WriteLine($"✓ XTC: {xtcFile}");
            else Console.WriteLine("✗ Conversion failed");
            return ok ? 0 : 1;
        }

        /// <summary>"top,bottom,left,right" を余白として解釈する。</summary>
        static bool TryParsePadding(string value, out (int Top, int Bottom, int Left, int Right) padding)
        {
            padding = (0, 0, 0, 0);
            var parts = value.Split(',');
            if (parts.Length != 4) return false;

            var v = new int[4];
            for (var i = 0; i < 4; i++)
                if (!int.TryParse(parts[i].Trim(), out v[i]) || v[i] < 0) return false;

            padding = (v[0], v[1], v[2], v[3]);
            return true;
        }

        static void PrintFonts()
        {
            var fonts = FontFinder.Enumerate();
            if (fonts.Count == 0)
            {
                Console.WriteLine("利用できるフォントが見つかりませんでした。");
                return;
            }

            Console.WriteLine("利用できるフォント:");
            foreach (var font in fonts)
                Console.WriteLine($"  [{font.Source}] {font.DisplayName}\n      {font.FilePath}");
        }

        static void PrintHelp()
        {
            Console.WriteLine("NovelToEink EPUB to XTC Converter");
            Console.WriteLine("-----------------------------");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("  NovelToEink.Cli <path> [-o <outputDir>] [-d X3|X4Pro] [-f <fontFile>] [--horizontal] [--fontsize <px>] [--padding <t,b,l,r>]");
            Console.WriteLine("");
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <path>      EPUB file or a directory containing EPUB files");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <dir>  Output directory for generated XTC files");
            Console.WriteLine("  -d, --device <id>   Target device: X3 (528x792) or X4Pro (480x800, default)");
            Console.WriteLine("  -f, --font <file>   Font file to use for rendering (default: auto-detected)");
            Console.WriteLine("      --horizontal    Lay out horizontally instead of Japanese vertical writing");
            Console.WriteLine("      --fontsize <px>     Body font size in pixels, 8-200 (default: 36)");
            Console.WriteLine("      --padding <t,b,l,r>  Extra margin in pixels, added to the built-in gutter (default: 3,0,0,0)");
            Console.WriteLine("      --list-fonts    List the fonts available for rendering");
            Console.WriteLine("  -h, --help          Show this help");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  NovelToEink.Cli book.epub -o out -d X3");
            Console.WriteLine("  NovelToEink.Cli mybooks -o out -d X4Pro -f C:/Windows/Fonts/msmincho.ttc --fontsize 48 --padding 20,20,10,10");
        }
    }
}
