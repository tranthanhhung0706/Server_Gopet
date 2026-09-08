namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `boss` (DB game gopettae_tae2) dùng cho API admin — Gift/HourSummon/
    /// BossMapSummon để dạng string (JSON thô, giống GiftCodeDto.GiftData) để admin sửa trực tiếp,
    /// không auto-bind int[][]/int[] như Data/mob/BossTemplate.cs (class nội bộ GopetManager dùng
    /// lúc chạy game — có type handler JsonAdapter tự parse).
    /// TypeBoss: 0 = boss ngày thường, 1 = boss vượt ải, 3 = boss sự kiện thú cưng du côn,
    /// 4 = boss ngày theo giờ (HourSummon), 6 = TYPE_BIRTHDAY_EVENT (chỉ trừ 1 HP/đòn bất kể sát
    /// thương thật — xem Boss.addHp()).
    /// </summary>
    public sealed class BossDto
    {
        public int BossId { get; set; }
        public string Name { get; set; } = "";
        public int PetTemplateId { get; set; }
        public int Str { get; set; }
        public int _int { get; set; }
        public int Agi { get; set; }
        public int Lvl { get; set; }
        public sbyte TypeBoss { get; set; }
        // JSON mảng phần thưởng thô, cùng format gift_code.gift_data — vd [[9,3,1,182,...]].
        public string Gift { get; set; } = "[]";
        public int Exp { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        // JSON mảng số nguyên đơn giản — vd [8,20] (giờ triệu hồi trong ngày).
        public string HourSummon { get; set; } = "[]";
        // JSON mảng id bản đồ được phép triệu hồi boss — vd [1,2,3].
        public string BossMapSummon { get; set; } = "[]";
    }
}
