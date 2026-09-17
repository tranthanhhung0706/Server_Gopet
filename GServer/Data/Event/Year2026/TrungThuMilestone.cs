using Gopet.Data.Collections;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Mẫu phần thưởng "mốc dùng hộp quà Trung Thu" (bảng `trung_thu_milestone`, DB game
    /// gopettae_tae2) — HOÀN TOÀN TÁCH RIÊNG khỏi `nap_moc_reward` (mốc tổng nạp). Mốc so với
    /// player.NumUseMoonCakeBoxNormal2026/NumUseMoonCakeBoxVip2026 (BoxType 0/1) thay vì user.tongnap
    /// nên không cần query DB web — xem GameController.ClaimTrungThuMilestone(). GiftData/
    /// UsersOfUseThis dùng chung cơ chế với NapMocReward.cs (1 mốc chỉ nhận được đúng 1 lần/player).
    /// </summary>
    public class TrungThuMilestone
    {
        public int Id { get; set; }
        /// <summary>0 = mốc hộp quà THƯỜNG, 1 = mốc hộp quà VIP.</summary>
        public sbyte BoxType { get; set; }
        public string Name { get; set; } = "";
        public int Threshold { get; set; }
        public int[][] GiftData { get; set; } = new int[0][];
        public JArrayList<int> UsersOfUseThis { get; set; } = new();
    }
}
