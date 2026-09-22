using System;

namespace Gopet.APIs
{
    /// <summary>Hình chiếu tóm tắt bảng `player` cho danh sách — chỉ field vô hại (số/tên/mốc thời gian).</summary>
    public sealed class PlayerListItem
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public int Gender { get; set; }
        public long Gold { get; set; }
        public long Coin { get; set; }
        public long Lua { get; set; }
        public int Star { get; set; }
        public int ClanId { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime LoginDate { get; set; }
        public DateTime LastTimeOnline { get; set; }
        // IP lần đăng nhập gần nhất (player.LastLoginIp, ghi lúc login). NULL nếu chưa đăng nhập từ khi có cột này.
        public string? LastLoginIp { get; set; }
        // Điểm Hoa Ngọc cả đời từ sự kiện săn boss (xem Data/Event/Year2026/Boss2026.cs) — không
        // bị trừ khi tiêu Hoa Ngọc mua đồ, dùng để xếp hạng.
        public int NumBossFlowerCoin2026 { get; set; }
        // Số dư Hoa Ngọc ĐANG CÓ (MONEY_TYPE_FLOWER_COIN) — giảm khi mua đồ ở SHOP_BOSS_2026.
        public int FlowerCoin { get; set; }
        // Tổng vàng (gold) đã tiêu — chỉ cộng dồn khi event "spend_gold_rank" đang bật qua
        // EventConfigManager, xem Player.CanAddSpendGold. Dùng cho bảng "Top Đại gia xuống núi".
        public long SpendGold { get; set; }
        // Tổng số lần dùng Hộp quà trung thu THƯỜNG/VIP (sự kiện TrungThu2026) — xem
        // Data/Event/Year2026/TrungThu2026.cs, dùng cho 2 bảng xếp hạng riêng.
        public int NumUseMoonCakeBoxNormal2026 { get; set; }
        public int NumUseMoonCakeBoxVip2026 { get; set; }
    }
}
