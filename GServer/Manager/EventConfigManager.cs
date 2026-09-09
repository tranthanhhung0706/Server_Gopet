using Dapper;
using System;
using System.Collections.Generic;

namespace Gopet.Manager
{
    /// <summary>
    /// Cấu hình BẬT/TẮT + khung giờ hoạt động của các sự kiện (bảng `event_config`, DB game
    /// gopettae_tae2) — cho phép admin đóng/mở sự kiện và chỉnh ngày qua trang web mà KHÔNG cần
    /// sửa code/build lại GServer. Event class nào muốn dùng cơ chế này thì Condition chỉ cần gọi
    /// EventConfigManager.IsActive("eventKey") thay vì so sánh DateTime.Now cứng trong code.
    ///
    /// Cache trong RAM (giống FieldManager) để Condition không phải query DB mỗi giây (EventManager
    /// check Condition mỗi 1s) — Reload() được gọi lại ngay sau khi admin sửa qua API để áp dụng
    /// tức thì, không cần restart GServer.
    /// </summary>
    public static class EventConfigManager
    {
        static Mutex mutex = new Mutex();

        public static Dictionary<string, EventConfig> Configs { get; private set; } = new Dictionary<string, EventConfig>();

        public static void Init()
        {
            Reload();
        }

        public static void Reload()
        {
            mutex.WaitOne();
            using (var conn = MYSQLManager.create())
            {
                var rows = conn.Query<EventConfig>("SELECT * FROM `event_config`");
                Dictionary<string, EventConfig> local = new();
                foreach (var row in rows)
                {
                    local[row.EventKey] = row;
                }
                Configs = local;
            }
            mutex.ReleaseMutex();
        }

        public static EventConfig Find(string eventKey)
        {
            Configs.TryGetValue(eventKey, out var value);
            return value;
        }

        /// <summary>
        /// true = sự kiện đang bật (isEnabled) VÀ đang trong khung giờ [startTime, endTime] (2 mốc
        /// này để trống thì coi như không giới hạn phía đó). Không tìm thấy eventKey thì false.
        /// </summary>
        public static bool IsActive(string eventKey)
        {
            EventConfig config = Find(eventKey);
            if (config == null || !config.IsEnabled)
            {
                return false;
            }
            DateTime now = DateTime.Now;
            if (config.StartTime.HasValue && now < config.StartTime.Value)
            {
                return false;
            }
            if (config.EndTime.HasValue && now > config.EndTime.Value)
            {
                return false;
            }
            return true;
        }
    }

    public class EventConfig
    {
        public string EventKey { get; set; }
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}
