using System;
using System.IO;
using System.Text.Json;

namespace OPCWebServer
{
    public class ConfigService
    {
        private readonly string _defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        // Загрузка по умолчанию (из папки программы)
        public AppConfig Load() => LoadFromFile(_defaultPath);

        // НОВЫЙ МЕТОД: Загрузка из любого указанного файла
        public AppConfig LoadFromFile(string customPath)
        {
            if (!File.Exists(customPath)) return new AppConfig();
            try
            {
                string json = File.ReadAllText(customPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }

        public void Save(AppConfig config)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_defaultPath, JsonSerializer.Serialize(config, options));
        }
    }
}
