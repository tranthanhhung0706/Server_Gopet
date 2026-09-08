using Dapper;
using REGServer.Database.Models;

namespace REGServer.Database;

/// <summary>Truy vấn bảng `player` (server_db) — nhân vật trong game.</summary>
public sealed class PlayerRepository
{
    private readonly DbManager _db;

    public PlayerRepository(DbManager db)
    {
        _db = db;
    }

    public async Task<PlayerRecord?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        await using var conn = await _db.CreateGameConnectionAsync(ct).ConfigureAwait(false);
        return await conn.QueryFirstOrDefaultAsync<PlayerRecord>(
            "SELECT * FROM `player` WHERE `name` = @Name LIMIT 1",
            new { Name = name }).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.CreateGameConnectionAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM `player`").ConfigureAwait(false);
    }
}
