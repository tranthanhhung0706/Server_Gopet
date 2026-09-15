namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `gopet_mob` (DB game gopettae_tae2) — cấu hình chỉ số quái theo TỪNG CẤP ĐỘ
    /// (khác `gopet_map_moblvl` là cấu hình map nào spawn cấp nào). Xem Mob.initMob() tra đúng dòng
    /// theo cấp random được để lấy hp/chỉ số; Pet.getAtk()/getStr()/getAgi()/getInt() đọc thẳng
    /// Str/Agi/Int; PetBattle dùng Exp/Coin làm phần thưởng khi quái chết. Khoá chính là Lvl (không
    /// auto-increment, admin tự nhập cấp).
    /// </summary>
    public sealed class GopetMobDto
    {
        public int Lvl { get; set; }
        public int Str { get; set; } = 1;
        public int Int { get; set; } = 1;
        public int Agi { get; set; } = 1;
        public int Exp { get; set; }
        public int Coin { get; set; }
        public int Hp { get; set; }
    }
}
