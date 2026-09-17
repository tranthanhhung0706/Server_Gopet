namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `trung_thu_milestone` dùng cho API admin. GiftData để dạng string (JSON thô,
    /// giống NapMocRewardDto.GiftData) — admin sửa trực tiếp qua GiftDataBuilder, không auto-bind
    /// int[][] như Data/Event/Year2026/TrungThuMilestone.cs (GameController.ClaimTrungThuMilestone dùng).
    /// </summary>
    public sealed class TrungThuMilestoneDto
    {
        public int Id { get; set; }
        // 0 = mốc hộp quà THƯỜNG, 1 = mốc hộp quà VIP.
        public sbyte BoxType { get; set; }
        public string Name { get; set; } = "";
        // Mốc số lần dùng hộp (player.NumUseMoonCakeBoxNormal2026/Vip2026) cần đạt để nhận thưởng này.
        public int Threshold { get; set; }
        public string GiftData { get; set; } = "";
        // Danh sách user_id đã nhận ĐÚNG mốc này — GServer tự quản lý, API admin chỉ đọc.
        public string UsersOfUseThis { get; set; } = "[]";
    }
}
