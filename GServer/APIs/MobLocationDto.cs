namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `gopet_mob_location` (DB game gopettae_tae2) — toạ độ cố định các điểm
    /// quái mọc trên từng bản đồ (xem GopetPlace.createNewMob()). Bảng KHÔNG có cột khoá chính —
    /// chỉ có (mapID, x, y) — nên API này dùng chính 3 cột đó làm khoá tự nhiên để sửa/xoá, không
    /// bịa thêm cột id (tránh phải ALTER TABLE bảng đang chạy thật).
    /// </summary>
    public sealed class MobLocationDto
    {
        public int MapID { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
