using System.IO;
using System.Text.Json;

namespace LogFileCollector
{
    /// <summary>
    /// Loads configuration from appsettings.json.
    /// </summary>
    public static class ConfigLoader
    {
        public static Appsettings Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Config file not found", path);

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Appsettings>(json, options)
                   ?? new Appsettings();
        }
    }
}
