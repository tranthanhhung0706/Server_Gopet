using MySqlConnector;
using REGServer.Config;

namespace REGServer.Database;

/// <summary>
/// Tương đương Manager/MYSQLManager.cs cũ, nhưng lấy connection string từ config/database.json
/// (System.Text.Json) thay vì App.config/ConfigurationManager kiểu .NET Framework cũ.
/// Vẫn dùng MySqlConnector + Dapper y như GServer nên schema/DB có thể tái sử dụng nguyên (server_db, web_db).
/// </summary>
public sealed class DbManager
{
    private readonly DatabaseSettings _settings;

    public DbManager(DatabaseSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Kết nối tới DB game chính (bảng player, item, pet... - tương đương MYSQLManager.create()).</summary>
    public async Task<MySqlConnection> CreateGameConnectionAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_settings.Game.ToConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>Kết nối tới DB web (bảng user/account - tương đương MYSQLManager.createWebMySqlConnection()).</summary>
    public async Task<MySqlConnection> CreateWebConnectionAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_settings.Web.ToConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>Thử mở rồi đóng kết nối ngay, dùng cho endpoint /health/db. Không throw — trả lỗi dạng chuỗi.</summary>
    public async Task<(bool ok, string message)> TestConnectionAsync(
        Func<CancellationToken, Task<MySqlConnection>> connect, CancellationToken ct)
    {
        try
        {
            await using MySqlConnection conn = await connect(ct).ConfigureAwait(false);
            return (true, $"OK (server version {conn.ServerVersion})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
