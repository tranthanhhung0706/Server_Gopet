using System.Text.Json.Serialization;

namespace REGServer.Config;

/// <summary>
/// Cấu hình chạy server, nạp từ config/server.json (thay cho App.config kiểu cũ).
/// </summary>
public sealed class ServerSettings
{
    [JsonPropertyName("tcpPort")]
    public int TcpPort { get; set; } = 19180;

    [JsonPropertyName("httpPort")]
    public int HttpPort { get; set; } = 8082;

    [JsonPropertyName("domainName")]
    public string DomainName { get; set; } = "";

    [JsonPropertyName("isOnlyAdminLogin")]
    public bool IsOnlyAdminLogin { get; set; }
}
