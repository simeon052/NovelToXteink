using System;
using System.IO;
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
                default:
                    break;
            }

            // Parse options
            string inputPath = null;
            string outputDirectory = null;
            string fontFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length) outputDirectory = args[++i];
                        else { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        break;
                    case "-f":
                    case "--font":
                        if (i + 1 < args.Length) fontFile = args[++i];
                        else { Console.WriteLine("Missing value for " + args[i]); return 1; }
                        break;
                    default:
                        if (!args[i].StartsWith("-"))
                            inputPath = args[i];
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

            // Validate inputs
            bool isDirectory = Directory.Exists(inputPath);
            bool isFile = File.Exists(inputPath);
            if (!isFile && !isDirectory)
            {
                Console.WriteLine($"Input not found: {inputPath}");
                return 1;
            }

            // XTC conversion mode
            var inputDir = isDirectory ? inputPath : Path.GetDirectoryName(inputPath)!;
            var outDir = outputDirectory ?? Path.Combine(inputDir, "converted");

            if (!File.Exists(inputPath))
            {
                var (s, f) = EpubToXtcConverter.ConvertDirectory(inputDir, outDir, fontFile);
                Console.WriteLine($"\nDone. Success={s} Failed={f}");
                return f == 0 ? 0 : 1;
            }
            else
            {
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var xtcFile = Path.Combine(outDir, fileName + ".xtc");
                Console.WriteLine($"Converting: {inputPath}");
                var ok = XtcConverterLibrary.ConvertEpubToXtc(inputPath, xtcFile, new XtcOptions
                {
                    Resolution = (480, 800),
                    FontFile = fontFile,
                    EnableVerticalWriting = true
                });
                if (ok) Console.WriteLine($"✓ XTC: {xtcFile}");
                else Console.WriteLine("✗ Conversion failed");
                return ok ? 0 : 1;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("NovelToEink EPUB to XTC Converter");
            Console.WriteLine("---------------------------------");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("  NovelToEink.Cli <path> [-o <outputDir>] [-f <fontFile>]");
            Console.WriteLine("");
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <path>      EPUB file or a directory containing EPUB files");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <dir>  Output directory for generated XTC files");
            Console.WriteLine("  -f, --font <file>   Font file to use for XTC conversion");
            Console.WriteLine("  -h, --help          Show this help");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  NovelToEink.Cli book.epub -o out");
            Console.WriteLine("  NovelToEink.Cli mybooks -o out");
        }
    }
}
