namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `achievement` (DB game gopettae_tae2) — mẫu danh hiệu (trang bị icon/khung
    /// + cộng chỉ số Atk/Def/Hp/Mp, xem Pet.cs áp dụng cho danh hiệu ĐANG MẶC qua
    /// PlayerData.CurrentAchievementId). Cột Int/Str/Agi hiện KHÔNG được code nào đọc — chỉ mang
    /// tính hiển thị/dự phòng, không có tác dụng cộng chỉ số thật (xem Pet.cs dòng áp buff).
    /// IdTemplate tự tăng (AUTO_INCREMENT), khác các bảng khác trong hệ thống (item/pet/shop...)
    /// tự chỉ định id.
    /// </summary>
    public sealed class AchievementDto
    {
        public int IdTemplate { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? IconPath { get; set; }
        public string? FramePath { get; set; }
        public bool IsVertically { get; set; }
        public int FrameNum { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        // Cột thật trong DB là `Int` — phải bọc backtick trong SQL vì INT là kiểu dữ liệu dành
        // riêng của MySQL/MariaDB, dùng trần làm tên cột sẽ lỗi cú pháp.
        public int Int { get; set; }
        public int Str { get; set; }
        public int Agi { get; set; }
        // Thời hạn tính bằng mili-giây (khoảng thời gian, KHÔNG phải timestamp) — 0 = vĩnh viễn.
        // Xem Achievement.cs constructor.
        public long Expire { get; set; }
        public int VX { get; set; }
        public int VY { get; set; }
    }
}
