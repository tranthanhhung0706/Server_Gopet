using System.Text.Json;

namespace REGServer.Config;

/// <summary>
/// Nạp cấu hình JSON đơn giản, thay thế System.Configuration/App.config của bản cũ.
/// </summary>
public static class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ServerSettings LoadServerSettings(string path = "config/server.json")
    {
        return Load<ServerSettings>(path) ?? new ServerSettings();
    }

    public static DatabaseSettings LoadDatabaseSettings(string path = "config/database.json")
    {
        return Load<DatabaseSettings>(path) ?? new DatabaseSettings();
    }

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Không tìm thấy file cấu hình: {path}");
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
