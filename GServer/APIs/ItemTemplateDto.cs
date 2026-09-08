namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu đầy đủ của bảng `item` dùng cho API — KHÔNG dùng lại class ItemTemplate
    /// (Data/item/ItemTemplate.cs) vì setter private (không nhận [FromBody]) và có field tính
    /// toán (CanFusion, IsEquip) không tương ứng cột DB. Các field mảng (ItemOption, AtkRange...)
    /// vẫn bind JSON tự động qua JsonAdapter&lt;int[]&gt; đã đăng ký global (GopetManager.cs),
    /// nên có thể dùng thẳng int[] mà không cần tự parse/serialize JSON.
    /// </summary>
    public sealed class ItemTemplateDto
    {
        public int ItemId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int Type { get; set; }
        public string? IconPath { get; set; }
        public string? FrameImgPath { get; set; }
        // -1 = không áp dụng theo giới tính.
        public sbyte Gender { get; set; }
        public bool IsStackable { get; set; }
        public int[]? ItemOption { get; set; }
        public int[]? ItemOptionValue { get; set; }
        public int[]? AtkRange { get; set; }
        public int[]? DefRange { get; set; }
        public int[]? HpRange { get; set; }
        public int[]? MpRange { get; set; }
        public int RequireStr { get; set; }
        public int RequireInt { get; set; }
        public int RequireAgi { get; set; }
        // Thời hạn sử dụng tính bằng mili-giây (khoảng thời gian, KHÔNG phải timestamp).
        public long? Expire { get; set; }
        public bool IsOnSky { get; set; }
        public bool CanTrade { get; set; }
        // -1 = không giới hạn phái thú cưng. Chỉ mang tính hiển thị, không bị GServer ép buộc.
        public sbyte PetNClass { get; set; }
        // -1 = không có hệ. Chỉ được GServer ép buộc với ngọc nguyên tố (socket gem).
        public sbyte Element { get; set; }
        public int Price { get; set; }
        // Số khung hình (frame) trong FrameImgPath để client cắt/chạy animation cánh (wing) —
        // chỉ có ý nghĩa với item type WING_ITEM. Mặc định 2 (client cũ hardcode 2 khung).
        public sbyte WingFrameNum { get; set; }
    }
}
