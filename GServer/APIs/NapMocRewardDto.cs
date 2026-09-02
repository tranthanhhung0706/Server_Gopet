namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `nap_moc_reward` dùng cho API admin. GiftData để dạng string (JSON thô,
    /// giống GiftCodeDto.GiftData) — admin sửa trực tiếp JSON, không auto-bind int[][] như
    /// Data/NapMocReward.cs (class nội bộ GameController.ClaimNapMocReward() dùng để trao thưởng).
    /// </summary>
    public sealed class NapMocRewardDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        // Mốc tổng nạp (user.tongnap, DB web) cần đạt để nhận thưởng này.
        public long Threshold { get; set; }
        public string GiftData { get; set; } = "";
        // Danh sách user_id đã nhận ĐÚNG mốc này — GServer tự quản lý khi player nhận thưởng qua
        // NPC (xem GameController.ClaimNapMocReward()), API admin chỉ đọc, không cho sửa trực tiếp.
        public string UsersOfUseThis { get; set; } = "[]";
    }
}
