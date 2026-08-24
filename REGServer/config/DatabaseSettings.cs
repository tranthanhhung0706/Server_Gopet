using System.Text.Json.Serialization;

namespace REGServer.Config;

public sealed class DatabaseConnectionSettings
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 3306;

    [JsonPropertyName("database")]
    public string Database { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "root";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    public string ToConnectionString() =>
        $"Server={Host};Port={Port};Database={Database};Uid={Username};Pwd={Password};CharSet=utf8mb4;";
}

/// <summary>
/// Cấu hình kết nối MySQL, nạp từ config/database.json. Có 2 DB giống GServer: "game" (server_db) và "web" (web_db, chứa bảng user/account).
/// </summary>
public sealed class DatabaseSettings
{
    [JsonPropertyName("game")]
    public DatabaseConnectionSettings Game { get; set; } = new();

    [JsonPropertyName("web")]
    public DatabaseConnectionSettings Web { get; set; } = new();
}
