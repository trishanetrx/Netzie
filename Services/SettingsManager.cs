using System;
using System.IO;
using System.Text.Json;

namespace NetSentry.Services
{
    public class AppSettings
    {
        public bool AlertsMuted { get; set; } = false;
        public double ThresholdMbps { get; set; } = 1.0;
        public string SortMode { get; set; } = "UsageDesc";
    }

    public static class SettingsManager
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSentry");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        private static readonly object _lock = new();

        public static AppSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        string json = File.ReadAllText(SettingsFile);
                        var settings = JsonSerializer.Deserialize<AppSettings>(json);
                        if (settings != null) return settings;
                    }
                }
                catch { }
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(SettingsFolder);
                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsFile, json);
                }
                catch { }
            }
        }
    }
}
