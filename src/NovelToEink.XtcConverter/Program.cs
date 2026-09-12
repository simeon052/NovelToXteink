using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NovelToEink.XtcConverter;

namespace NovelToEink.XtcConverter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: XtcConverter <epubFile> [xtcFile]");
                Console.WriteLine("Usage: XtcConverter folder <folderPath>");
                return;
            }

            string input = args[0];
            if (input.Equals("folder", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: XtcConverter folder <folderPath>");
                    return;
                }
                string folderPath = args[1];
                await ConvertFolderAsync(folderPath);
            }
            else
            {
                string epubFile = input;
                string xtcFile = args.Length > 1 ? args[1] : null;
                await ConvertFileAsync(epubFile, xtcFile);
            }
        }

        static async Task ConvertFileAsync(string epubFile, string xtcFile)
        {
            try
            {
                if (!File.Exists(epubFile))
                {
                    Console.WriteLine($"EPUB file not found: {epubFile}");
                    return;
                }

                if (xtcFile == null)
                {
                    xtcFile = Path.ChangeExtension(epubFile, ".xtc");
                }

                Console.WriteLine($"Converting {epubFile} to {xtcFile}");
                
                var converter = new XtcConverter();
                await converter.ConvertAsync(epubFile, xtcFile);
                
                Console.WriteLine("Conversion completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during conversion: {ex.Message}");
            }
        }

        static async Task ConvertFolderAsync(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Console.WriteLine($"Folder not found: {folderPath}");
                    return;
                }

                var epubFiles = Directory.GetFiles(folderPath, "*.epub");
                Console.WriteLine($"Found {epubFiles.Length} EPUB files in {folderPath}");
                
                foreach (string epubFile in epubFiles)
                {
                    string xtcFile = Path.ChangeExtension(epubFile, ".xtc");
                    Console.WriteLine($"Converting {epubFile} to {xtcFile}");
                    
                    var converter = new XtcConverter();
                    await converter.ConvertAsync(epubFile, xtcFile);
                    
                    Console.WriteLine("Conversion completed successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during folder conversion: {ex.Message}");
            }
        }
    }
}