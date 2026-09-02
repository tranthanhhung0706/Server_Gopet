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
    }
}
