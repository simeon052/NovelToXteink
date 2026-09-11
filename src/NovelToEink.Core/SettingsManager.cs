using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NovelToEink.Core
{
    /// <summary>
    /// 設定を管理するクラス
    /// </summary>
    public class ExportSettings
    {
        public string ExportPath { get; set; } = "";
        public string ImportPath { get; set; } = "";
        public string ExportFormat { get; set; } = "json";
        public bool Compress { get; set; } = false;
        public string LastExportedPath { get; set; } = "";
        public string LastImportedPath { get; set; } = "";
        
        public ExportSettings()
        {
        }
        
        public ExportSettings(string exportPath, string importPath, string exportFormat, bool compress)
        {
            ExportPath = exportPath;
            ImportPath = importPath;
            ExportFormat = exportFormat;
            Compress = compress;
        }
        
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                {"exportPath", ExportPath},
                {"importPath", ImportPath},
                {"exportFormat", ExportFormat},
                {"compress", Compress},
                {"lastExportedPath", LastExportedPath},
                {"lastImportedPath", LastImportedPath}
            };
        }
        
        public static ExportSettings FromDictionary(Dictionary<string, object> data)
        {
            var settings = new ExportSettings();
            if (data.TryGetValue("exportPath", out var exportPathObj))
                settings.ExportPath = exportPathObj?.ToString() ?? "";
            if (data.TryGetValue("importPath", out var importPathObj))
                settings.ImportPath = importPathObj?.ToString() ?? "";
            if (data.TryGetValue("exportFormat", out var exportFormatObj))
                settings.ExportFormat = exportFormatObj?.ToString() ?? "json";
            if (data.TryGetValue("compress", out var compressObj) && compressObj is bool compress)
                settings.Compress = compress;
            if (data.TryGetValue("lastExportedPath", out var lastExportedPathObj))
                settings.LastExportedPath = lastExportedPathObj?.ToString() ?? "";
            if (data.TryGetValue("lastImportedPath", out var lastImportedPathObj))
                settings.LastImportedPath = lastImportedPathObj?.ToString() ?? "";
            
            return settings;
        }
    }
    
    /// <summary>
    /// 設定の保存と読み込みを担当するクラス
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NovelToEink",
            "settings.json");
        
        /// <summary>
        /// 設定をファイルに保存
        /// </summary>
        /// <param name="settings">保存する設定</param>
        /// <returns>保存に成功したかどうか</returns>
        public static bool SaveSettings(ExportSettings settings)
        {
            try
            {
                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // JSONとして保存
                var json = JsonSerializer.Serialize(settings.ToDictionary(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(SettingsFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                // ログ出力が必要な場合は追加
                return false;
            }
        }
        
        /// <summary>
        /// ファイルから設定を読み込み
        /// </summary>
        /// <returns>読み込まれた設定、読み込みに失敗した場合はnull</returns>
        public static ExportSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return null;
                }
                
                var json = File.ReadAllText(SettingsFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (data == null)
                {
                    return null;
                }
                
                return ExportSettings.FromDictionary(data);
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// 指定されたパスのファイルに設定をエクスポート
        /// </summary>
        /// <param name="settings">エクスポートする設定</param>
        /// <param name="filePath">エクスポート先のファイルパス</param>
        /// <returns>エクスポートに成功したかどうか</returns>
        public static bool ExportSettings(ExportSettings settings, string filePath)
        {
            try
            {
                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // JSONとしてエクスポート
                var json = JsonSerializer.Serialize(settings.ToDictionary(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        /// <summary>
        /// 指定されたパスのファイルから設定をインポート
        /// </summary>
        /// <param name="filePath">インポート元のファイルパス</param>
        /// <returns>インポートされた設定、インポートに失敗した場合はnull</returns>
        public static ExportSettings ImportSettings(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }
                
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (data == null)
                {
                    return null;
                }
                
                return ExportSettings.FromDictionary(data);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}