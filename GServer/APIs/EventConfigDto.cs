using System;

namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `event_config` (DB game gopettae_tae2) — bật/tắt + khung giờ hoạt động của
    /// các sự kiện đọc qua EventConfigManager. eventKey phải khớp đúng chuỗi mà event class dùng
    /// (vd Boss2026.EVENT_KEY = "boss2026") thì mới có tác dụng thật — sửa/thêm eventKey mới ở đây
    /// không tự tạo ra sự kiện, event class vẫn phải được viết sẵn trong code và đọc đúng key này.
    /// </summary>
    public sealed class EventConfigDto
    {
        public string EventKey { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}
