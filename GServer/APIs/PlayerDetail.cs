using System;

namespace Gopet.APIs
{
    /// <summary>
    /// Chi tiết 1 player — nhiều field hơn PlayerListItem. Các cột lưu object C# lồng nhau
    /// (Pet, Item, ShopArena, GopetCaptcha...) trả về NGUYÊN VĂN chuỗi JSON trong DB (hậu tố
    /// "Json"), KHÔNG deserialize thành object C# — vì các class đó nhiều tầng lồng nhau
    /// (Pet có PetTatto/PetEffect list, Item có option/enchant...), model lại đầy đủ tốn công
    /// không tương xứng với 1 API xem/quản trị. Chỉ để ADMIN XEM, không có endpoint sửa các field
    /// JSON này (rủi ro làm hỏng cấu trúc dữ liệu game nếu sửa tay sai định dạng).
    /// </summary>
    public sealed class PlayerDetail
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public int Gender { get; set; }
        public long Gold { get; set; }
        public long SpendGold { get; set; }
        public long Coin { get; set; }
        public long Lua { get; set; }
        public int Star { get; set; }
        public int ClanId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsOnSky { get; set; }
        public bool IsFirstFree { get; set; }
        public string? AvatarPath { get; set; }
        public int AccumulatedPoint { get; set; }
        public int ArenaPoint { get; set; }
        public int EventPoint { get; set; }
        public int KioskFund { get; set; }
        public int PkPoint { get; set; }
        public int CurrentAchievementId { get; set; }
        public DateTime LoginDate { get; set; }
        public DateTime LastTimeOnline { get; set; }

        // Nguyên văn JSON trong DB — chỉ đọc.
        public string? ItemsJson { get; set; }
        public string? PetsJson { get; set; }
        public string? PetSelectedJson { get; set; }
        public string? SkinJson { get; set; }
        public string? WingJson { get; set; }
        public string? AchievementsJson { get; set; }
    }
}
