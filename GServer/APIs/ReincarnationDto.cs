namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `reincarnation` (DB game gopettae_tae2) — công thức trùng sinh pet, dùng
    /// bởi MenuController.selectMenu.cs case MENU_OPTION_PET_REINCARNATION. GServer cache theo
    /// PetId (Dictionary&lt;int, PetReincarnation&gt; GopetManager.Reincarnations) — nếu 2 dòng
    /// trùng PetId, dòng nạp SAU sẽ ghi đè dòng trước trong cache (không báo lỗi), nên mỗi PetId
    /// chỉ nên có đúng 1 công thức.
    /// </summary>
    public sealed class ReincarnationDto
    {
        public int Id { get; set; }
        // Pet gốc cần trùng sinh.
        public int PetId { get; set; }
        // Số thẻ trùng sinh (ID_ITEM_CARD_REINCARNATION) cần tiêu để thực hiện.
        public int NumCard { get; set; } = 1;
        // Pet thành sau khi trùng sinh — bằng PetId nghĩa là giữ nguyên loài, chỉ đổi hệ phái
        // (Fighter→Archer, Wizard→Angel, Assassin→Demon — xem MenuController.selectMenu.cs).
        public int PetIdReincarnation { get; set; }
    }
}
