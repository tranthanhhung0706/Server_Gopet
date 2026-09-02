using Gopet.Data.Collections;

namespace Gopet.Data
{
    /// <summary>
    /// Mẫu phần thưởng "mốc nạp" (bảng `nap_moc_reward`, DB game gopettae_tae2) — mốc tổng nạp
    /// (user.tongnap, web DB gopettae_gopet_web) người chơi cần đạt để nhận thưởng. GiftData dùng
    /// chung format mảng phần thưởng [type, id, số lượng, ...] với gift_code/NOEL_DAILYS — bind
    /// tự động qua JsonAdapter&lt;int[][]&gt; đã đăng ký global (GopetManager.cs). UsersOfUseThis
    /// cùng cơ chế với GiftCodeData.usersOfUseThis — danh sách user_id đã nhận ĐÚNG mốc này.
    /// </summary>
    public class NapMocReward
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long Threshold { get; set; }
        public int[][] GiftData { get; set; }
        public JArrayList<int> UsersOfUseThis { get; set; } = new();
    }
}
