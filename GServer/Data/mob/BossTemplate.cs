namespace Gopet.Data.Mob
{
    public class BossTemplate
    {
        public int bossId { get; private set; }
        public string name { get; private set; }
        public int[][] gift { get; private set; }
        public sbyte typeBoss { get; private set; }
        public int petTemplateId { get; private set; }
        public int lvl { get; private set; }
        public int str { get; private set; }
        public int agi { get; private set; }
        public int _int { get; private set; }
        public int exp { get; private set; }
        public int hp { get; private set; }
        public int atk { get; private set; }
        public int[] HourSummon { get; private set; }
        public int[] BossMapSummon { get; private set; }
        /// <summary>
        /// Danh sách skillId (bảng `skill`, vd 101=song kích, 105=sấm sét, 107=hạ độc, 111=hút máu)
        /// boss này có thể ngẫu nhiên tung ra thay vì chỉ đánh thường — null/rỗng = đánh thường như
        /// mọi boss khác. Xem PetBattle.cs (mobAttack) đọc field này qua Boss.skill.
        /// </summary>
        public int[] SkillIds { get; private set; }
        /// <summary>
        /// Số lượng boss hồi ra mỗi lần trúng giờ/hết chu kỳ — chỉ áp dụng typeBoss=2 (Boss Trung
        /// Thu, xem BossTrungThu2026.cs). Boss loại khác (0/1/3/4/6) không đọc field này.
        /// </summary>
        public int SummonCount { get; private set; }
        /// <summary>
        /// Nếu > 0: bỏ qua HourSummon, hồi lặp lại đều đặn mỗi SummonIntervalMinutes phút (vd 30 =
        /// mỗi 30 phút) suốt cả ngày thay vì chỉ đúng vài giờ cố định — chỉ áp dụng typeBoss=2.
        /// = 0 (mặc định): dùng HourSummon (hồi đúng những giờ cấu hình trong ngày) như bình thường.
        /// </summary>
        public int SummonIntervalMinutes { get; private set; }
        #region CONST
        /// <summary>
        /// Loại boss là boss sự kiện sinh nhật hoặc chỉ đánh giảm đc 1 máu
        /// </summary>
        public const int TYPE_BIRTHDAY_EVENT = 6;
        #endregion
        public string getName(Player player)
        {
            return player.Language.BossNameLanguage[this.bossId];
        }
    }
}