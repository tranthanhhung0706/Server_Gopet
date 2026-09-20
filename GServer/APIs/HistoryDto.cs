namespace Gopet.APIs
{
    /// <summary>
    /// 1 dòng bảng `history` (DB gp_log, ghi bởi HistoryManager). Bảng KHÔNG có khoá chính nên không
    /// có Id; Log/Obj bị cắt bớt (xem HistoryController) để trang danh sách không bị nặng — Obj nhiều
    /// dòng là cả PlayerData serialize ra JSON (vd log "Thao tác gửi giao diện ADMIN").
    /// </summary>
    public sealed class HistoryDto
    {
        // user_id của tài khoản (không phải id nhân vật).
        public int TargetId { get; set; }
        public string CharName { get; set; } = "";
        public string Log { get; set; } = "";
        public string? Obj { get; set; }
        // Độ dài THẬT của Obj trước khi cắt — để UI biết Obj có bị cắt không.
        public int ObjLength { get; set; }
        public DateTime TimeDb { get; set; }
    }
}
