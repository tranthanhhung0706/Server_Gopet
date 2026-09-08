namespace REGServer.Database.Models;

/// <summary>
/// Map tối giản tới bảng `user` trong web_db (xem MariaDB_SQL/web_db.sql). Chỉ lấy các cột cần cho
/// đăng nhập; khi cần thêm cột nào cứ bổ sung property cùng tên (Dapper tự map theo tên cột).
/// </summary>
public sealed class AccountRecord
{
    public int user_id { get; set; }
    public string username { get; set; } = "";
    public string password { get; set; } = "";
    public int role { get; set; }
    public bool isBaned { get; set; }
    public string banReason { get; set; } = "";
    public int coin { get; set; }
    public string email { get; set; } = "";
}
