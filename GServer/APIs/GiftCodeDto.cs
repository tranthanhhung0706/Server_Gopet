using System;

namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu đầy đủ của bảng `gift_code` (DB game gopettae_tae2). GiftData và
    /// UsersOfUseThis trả về NGUYÊN VĂN chuỗi JSON như lưu trong DB (không parse thành object
    /// C# có cấu trúc) — phía Next.js tự JSON.parse để hiển thị/sửa, tránh phải mô hình hoá lại
    /// toàn bộ 16 loại gift_data (xem GiftController cho bảng tra type).
    /// </summary>
    public sealed class GiftCodeDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public int CurrentUser { get; set; }
        public int MaxUser { get; set; }
        public string GiftData { get; set; } = "";
        public DateTime Expire { get; set; }
        public string UsersOfUseThis { get; set; } = "[]";
        public bool IsClanCode { get; set; }
        public bool IsForNonActiveUser { get; set; }
    }
}
