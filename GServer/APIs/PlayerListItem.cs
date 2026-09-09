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
        // Điểm Hoa Ngọc cả đời từ sự kiện săn boss (xem Data/Event/Year2026/Boss2026.cs) — không
        // bị trừ khi tiêu Hoa Ngọc mua đồ, dùng để xếp hạng.
        public int NumBossFlowerCoin2026 { get; set; }
        // Số dư Hoa Ngọc ĐANG CÓ (MONEY_TYPE_FLOWER_COIN) — giảm khi mua đồ ở SHOP_BOSS_2026.
        public int FlowerCoin { get; set; }
    }
}
