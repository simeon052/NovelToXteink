using System;
using System.IO;
using System.Linq;
using NovelToEink.Xtc;
using NovelToEink.XtcConverter;
using NovelToEink.Core;

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

            // Export/Importコマンドの処理
            if (args.Length >= 2)
            {
                if (args[0].Equals("--export-settings", System.StringComparison.OrdinalIgnoreCase))
                {
                    var outputPath = args[1];
                    var settings = new ExportSettings();
                    if (SettingsManager.ExportSettings(settings, outputPath))
                    {
                        Console.WriteLine($"設定を {outputPath} にエクスポートしました");
                        return 0;
                    }
                    else
                    {
                        Console.Error.WriteLine($"設定のエクスポートに失敗しました: {outputPath}");
                        return 1;
                    }
                }
                else if (args[0].Equals("--import-settings", System.StringComparison.OrdinalIgnoreCase))
                {
                    var inputPath = args[1];
                    var settings = SettingsManager.ImportSettings(inputPath);
                    if (settings != null)
                    {
                        Console.WriteLine($"設定を {inputPath} からインポートしました");
                        // インポートされた設定を保存
                        if (SettingsManager.SaveSettings(settings))
                        {
                            Console.WriteLine("設定を保存しました");
                            return 0;
                        }
                        else
                        {
                            Console.Error.WriteLine("設定の保存に失敗しました");
                            return 1;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"設定のインポートに失敗しました: {inputPath}");
                        return 1;
                    }
                }
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
            var threshold = 200;
            var padding = (Top: 3, Bottom: 0, Left: 0, Right: 0);

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                            inputPath = args[++i];
                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                            outputDirectory = args[++i];
                        break;
                    case "-f":
                    case "--font":
                        if (i + 1 < args.Length)
                            fontFile = args[++i];
                        break;
                    case "-d":
                    case "--device":
                        if (i + 1 < args.Length)
                        {
                            var deviceStr = args[++i];
                            if (deviceStr.Equals("x4pro", StringComparison.OrdinalIgnoreCase))
                                device = XteinkDevice.X4Pro;
                            else if (deviceStr.Equals("x4", StringComparison.OrdinalIgnoreCase))
                                device = XteinkDevice.X4;
                            else if (deviceStr.Equals("x3", StringComparison.OrdinalIgnoreCase))
                                device = XteinkDevice.X3;
                        }
                        break;
                    case "-h":
                    case "--horizontal":
                        vertical = false;
                        break;
                    case "-s":
                    case "--font-size":
                        if (i + 1 < args.Length)
                        {
                            if (int.TryParse(args[++i], out var fs))
                                fontSize = fs;
                        }
                        break;
                    case "-t":
                    case "--threshold":
                        if (i + 1 < args.Length)
                        {
                            if (int.TryParse(args[++i], out var th))
                                threshold = th;
                        }
                        break;
                    case "--padding":
                        if (i + 1 < args.Length)
                        {
                            var paddingStr = args[++i];
                            var parts = paddingStr.Split(',');
                            if (parts.Length == 4)
                            {
                                if (int.TryParse(parts[0], out var top) &&
                                    int.TryParse(parts[1], out var bottom) &&
                                    int.TryParse(parts[2], out var left) &&
                                    int.TryParse(parts[3], out var right))
                                {
                                    padding = (top, bottom, left, right);
                                }
                            }
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(inputPath) || string.IsNullOrEmpty(outputDirectory))
            {
                PrintHelp();
                return 1;
            }

            try
            {
                var converter = new XtcConverterLibrary();
                converter.Convert(inputPath, outputDirectory, device, vertical, fontSize, threshold, padding, fontFile);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("NovelToEink CLI");
            Console.WriteLine("Converts EPUB files to XTC format for E-ink devices");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  NovelToEink.Cli.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -i, --input <file>       Input EPUB file");
            Console.WriteLine("  -o, --output <dir>         Output directory");
            Console.WriteLine("  -f, --font <file>            Font file (optional)");
            Console.WriteLine("  -d, --device <device>    Device type (x4pro, x4, x3)");
            Console.WriteLine("  -h, --horizontal           Horizontal layout (default: vertical)");
            Console.WriteLine("  -s, --font-size <size>      Font size (default: 36)");
            Console.WriteLine("  -t, --threshold <value>      Threshold for image processing (default: 200)");
            Console.WriteLine("  --padding <top,bottom,left,right> Padding values");
            Console.WriteLine("  --list-fonts             List available fonts");
            Console.WriteLine("  --export-settings <file>   Export current settings to a file");
            Console.WriteLine("  --import-settings <file>      Import settings from a file");
            Console.WriteLine("  -h, --help                 Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  NovelToEink.Cli.exe -i input.epub -o output_dir");
            Console.WriteLine("  NovelToEink.Cli.exe -i input.epub -o output_dir -d x4pro -s 24");
            Console.WriteLine("  NovelToEink.Cli.exe --export-settings settings.json");
            Console.WriteLine("  NovelToEink.Cli.exe --import-settings settings.json");
        }

        static void PrintFonts()
        {
            Console.WriteLine("Available fonts:");
            foreach (var font in XtcConverterLibrary.AvailableFonts)
            {
                Console.WriteLine($"  {font}");
            }
        }
    }
}