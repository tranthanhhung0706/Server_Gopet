namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu đầy đủ của bảng `shop` dùng cho API — KHÔNG dùng lại class ShopTemplateItem
    /// (Data/shop/ShopTemplateItem.cs) vì class đó có rất nhiều field KHÔNG map cột DB nào (vd
    /// isSpceial, nameSpeceial, spceialType, hasId, menuId, isLock, TimeNeedReset, NeedFund — các
    /// entry đặc biệt được dựng thẳng trong code, xem ShopClan.cs), dùng field trần (không có
    /// setter chuẩn cho [FromBody]). DTO này chỉ expose đúng 11 cột thật của bảng `shop`.
    /// </summary>
    public sealed class ShopTemplateItemDto
    {
        public int Id { get; set; }
        public int ShopId { get; set; }
        // Cột có trong DB nhưng KHÔNG được ShopTemplateItem.cs đọc (Dapper bỏ qua vì không có
        // field khớp tên) — xác nhận qua code review, hiện không ảnh hưởng gameplay. Vẫn hiện ra
        // đây để admin thấy đúng dữ liệu thô trong DB.
        public sbyte InventoryType { get; set; }
        public int? ItemTemTempleId { get; set; }
        public int? PetId { get; set; }
        public int Count { get; set; }
        public bool IsSellItem { get; set; }
        // Danh sách loại tiền tệ được CHẤP NHẬN (không phải cộng dồn) — mỗi phần tử ghép với
        // Price cùng index tạo thành 1 lựa chọn thanh toán riêng (xem MenuController.cs:865-871).
        // Mã tiền tệ: 0=Vàng 1=Ngọc 2=Thỏi bạc 3=Thỏi vàng 4=Ngọc máu 5=Quỹ Clan
        // 6=Điểm tăng trưởng Clan 7=Ngọc pha lê 8=Lúa 9=Điểm hoa vàng 10=Điểm hoa ngọc
        // 11=Xu vuông 12=Xu trụ (xem GopetManager.cs MONEY_TYPE_*).
        public sbyte[]? MoneyType { get; set; }
        // Giá tương ứng — MoneyType[i] và Price[i] PHẢI cùng độ dài, ghép theo index.
        public int[]? Price { get; set; }
        // Có field/getter nhưng KHÔNG nơi nào trong code so sánh/gate theo giá trị này — cột hiện
        // không ảnh hưởng gameplay (shop Clan thật sự dựng động trong ShopClan.cs, không đọc từ
        // bảng này).
        public int ClanLvl { get; set; }
        // Chỉ đọc để hiện "còn lại x{Count-PerCount}" trong tên món (ShopTemplateItem.getName)
        // nhưng KHÔNG bao giờ bị tăng lúc runtime (setPerCount chỉ gọi trong Clone()) — là giá
        // trị tĩnh từ DB, không phải số lượng tồn kho thật đang giảm dần.
        public int PerCount { get; set; }
    }
}
