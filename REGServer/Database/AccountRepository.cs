using Dapper;
using REGServer.Database.Models;

namespace REGServer.Database;

/// <summary>Truy vấn bảng `user` (web_db) — tài khoản đăng nhập.</summary>
public sealed class AccountRepository
{
    private readonly DbManager _db;

    public AccountRepository(DbManager db)
    {
        _db = db;
    }

    public async Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await _db.CreateWebConnectionAsync(ct).ConfigureAwait(false);
        return await conn.QueryFirstOrDefaultAsync<AccountRecord>(
            "SELECT * FROM `user` WHERE `username` = @Username LIMIT 1",
            new { Username = username }).ConfigureAwait(false);
    }

    /// <summary>Đếm tổng số tài khoản trong bảng `user` (web_db, ví dụ gopettae_gopet_web).</summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.CreateWebConnectionAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM `user`").ConfigureAwait(false);
    }
}
