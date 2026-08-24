namespace REGServer.Database.Models;

/// <summary>
/// Map tối giản tới bảng `player` trong server_db (xem MariaDB_SQL/server_db.sql).
/// Bản gốc (Data/User/PlayerData.cs) có hàng trăm field (items/pets/task/clan...) — ở base này chỉ
/// lấy vài cột cốt lõi để chứng minh kết nối DB chạy được; port thêm field khi cần logic thật.
/// </summary>
public sealed class PlayerRecord
{
    public int ID { get; set; }
    public int user_id { get; set; }
    public bool isAdmin { get; set; }
    public string name { get; set; } = "";
    public int gender { get; set; }
    public long gold { get; set; }
    public long coin { get; set; }
    public long lua { get; set; }
    public int clanId { get; set; } = -1;
}
