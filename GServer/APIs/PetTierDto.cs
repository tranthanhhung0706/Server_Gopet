namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `pet_tier` (DB game gopettae_tae2) — công thức tiến hoá pet (khác Trùng
    /// sinh): tiêu 2 pet (chính + mồi) để ra 1 pet loài mới, dùng bởi GameController.petUpTier()
    /// (xem case GopetCMD.UP_TIER_PET). GServer cache theo PetTemplateId1
    /// (Dictionary&lt;int, PetTier&gt; GopetManager.petTier) — nếu 2 dòng trùng
    /// PetTemplateId1, dòng nạp SAU sẽ ghi đè dòng trước (không báo lỗi), nên mỗi
    /// PetTemplateId1 chỉ nên có đúng 1 công thức.
    /// </summary>
    public sealed class PetTierDto
    {
        public int TierId { get; set; }
        // Pet chính (được tiến hoá thành PetTemplateId2).
        public int PetTemplateId1 { get; set; }
        // Pet kết quả sau tiến hoá.
        public int PetTemplateId2 { get; set; }
        // Pet mồi bắt buộc phải đúng loài này mới tiến hoá được — bị tiêu huỷ hoàn toàn.
        public int PetTemplateIdNeed { get; set; }
    }
}
