namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// 1 dòng công thức chế tạo hộp quà Trung Thu 2026 (bảng `trung_thu_recipe`) — admin quản lý
    /// qua trang buff_gopet, đọc vào RAM qua GopetManager.trungThuRecipe (khoá theo BoxItemId).
    /// </summary>
    public class TrungThuRecipe
    {
        public int RecipeId { get; set; }
        public int BoxItemId { get; set; }
        public string Name { get; set; } = "";
        public int FlourCount { get; set; }
        public int EggCount { get; set; }
        public int MungBeanCount { get; set; }
        public int LotusSeedCount { get; set; }
        public int MoonCakeCount { get; set; }
        public long CoinCost { get; set; }
        public long GoldCost { get; set; }
    }
}
