namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `trade_gift` dùng cho API admin — 1 dòng = 1 vật phẩm có thể rơi ra khi
    /// người chơi "đổi thỏi" ở NPC Tiên Nữ (xem MenuController.Trade()/ShowTradeGiftPool()).
    /// Type: 0 = đổi bằng Thỏi Bạc (kèm Ngọc, hoặc dùng chung cho đổi bằng Lúa), 1 = đổi bằng
    /// Thỏi Vàng (kèm Vàng) — xem TradeGiftTemplate.TYPE_COIN/TYPE_GOLD/TYPE_LUA và
    /// GopetManager.TradeGiftPrice để biết chính xác giá mỗi lượt đổi.
    /// </summary>
    public sealed class TradeGiftDto
    {
        public int TradeId { get; set; }
        public int ItemTemplateId { get; set; }
        public int Count { get; set; } = 1;
        public sbyte Type { get; set; }
        public float Percent { get; set; }
    }
}
