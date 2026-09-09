using Dapper;
using Gopet.Manager;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý bật/tắt + khung giờ sự kiện (bảng `event_config`, DB game gopettae_tae2) — đọc qua
    /// EventConfigManager (cache RAM). Mọi thao tác tạo/sửa/xoá ở đây đều gọi
    /// EventConfigManager.Reload() ngay để áp dụng tức thì, KHÔNG cần "Nạp lại danh mục" hay restart
    /// GServer — khác hẳn Boss/Shop/... (chỉ nạp 1 lần lúc khởi động).
    ///
    /// Sửa/thêm eventKey KHÔNG tự tạo ra sự kiện — event class (vd Boss2026) phải được viết sẵn
    /// trong code, tự đọc EventConfigManager.IsActive("eventKey") đúng chuỗi. Trang này chỉ điều
    /// khiển các eventKey mà code đã hỗ trợ.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/event-config")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class EventConfigController : ControllerBase
    {
        private const string SelectSql =
            @"SELECT eventKey AS EventKey, name AS Name, isEnabled AS IsEnabled, startTime AS StartTime, endTime AS EndTime
              FROM `event_config`";

        /// <summary>Danh sách toàn bộ sự kiện đã cấu hình (số lượng nhỏ, không phân trang).</summary>
        [HttpGet("/v1/gopet/api/EventConfigs")]
        public IActionResult GetEventConfigs()
        {
            using var conn = MYSQLManager.create();
            var list = conn.Query<EventConfigDto>($"{SelectSql} ORDER BY eventKey ASC").ToList();
            return Ok(new BaseResponse<List<EventConfigDto>>(1, "Thành công", list));
        }

        /// <summary>Chi tiết 1 sự kiện theo eventKey.</summary>
        [HttpGet("/v1/gopet/api/EventConfigs/{eventKey}")]
        public IActionResult GetEventConfigByKey(string eventKey)
        {
            using var conn = MYSQLManager.create();
            var config = conn.QueryFirstOrDefault<EventConfigDto>($"{SelectSql} WHERE eventKey = @eventKey", new { eventKey });
            if (config == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy sự kiện", null));
            }
            return Ok(new BaseResponse<EventConfigDto>(1, "Thành công", config));
        }

        public record CreateEventConfigRequest(string EventKey, string Name, bool IsEnabled = false, DateTime? StartTime = null, DateTime? EndTime = null);

        /// <summary>Tạo 1 dòng cấu hình sự kiện mới. Chỉ có tác dụng thật nếu event class trong code đọc đúng eventKey này.</summary>
        [HttpPost("/v1/gopet/api/EventConfigs")]
        public IActionResult CreateEventConfig([FromBody] CreateEventConfigRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.EventKey) || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu eventKey hoặc name", null));
            }
            string eventKey = req.EventKey.Trim();

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `event_config` WHERE eventKey = @eventKey", new { eventKey });
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "eventKey đã tồn tại", null));
            }

            conn.Execute(
                @"INSERT INTO `event_config` (eventKey, name, isEnabled, startTime, endTime)
                  VALUES (@eventKey, @name, @isEnabled, @startTime, @endTime)",
                new { eventKey, name = req.Name, isEnabled = req.IsEnabled, startTime = req.StartTime, endTime = req.EndTime });

            EventConfigManager.Reload();

            var created = conn.QueryFirstOrDefault<EventConfigDto>($"{SelectSql} WHERE eventKey = @eventKey", new { eventKey });
            return Ok(new BaseResponse<EventConfigDto?>(1, "Tạo sự kiện thành công — đã áp dụng ngay", created));
        }

        public record UpdateEventConfigRequest(string? Name, bool? IsEnabled, DateTime? StartTime, DateTime? EndTime, bool ClearStartTime = false, bool ClearEndTime = false);

        /// <summary>Cập nhật 1 phần cấu hình sự kiện — áp dụng ngay, không cần restart GServer.</summary>
        [HttpPatch("/v1/gopet/api/EventConfigs/{eventKey}")]
        public IActionResult UpdateEventConfig(string eventKey, [FromBody] UpdateEventConfigRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<EventConfigDto>($"{SelectSql} WHERE eventKey = @eventKey", new { eventKey });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy sự kiện", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("eventKey", eventKey);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.IsEnabled is bool isEnabled) { setClauses.Add("isEnabled = @isEnabled"); parameters.Add("isEnabled", isEnabled); }
            if (req?.ClearStartTime == true) { setClauses.Add("startTime = NULL"); }
            else if (req?.StartTime is DateTime startTime) { setClauses.Add("startTime = @startTime"); parameters.Add("startTime", startTime); }
            if (req?.ClearEndTime == true) { setClauses.Add("endTime = NULL"); }
            else if (req?.EndTime is DateTime endTime) { setClauses.Add("endTime = @endTime"); parameters.Add("endTime", endTime); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `event_config` SET {string.Join(", ", setClauses)} WHERE eventKey = @eventKey", parameters);

            EventConfigManager.Reload();

            var updated = conn.QueryFirstOrDefault<EventConfigDto>($"{SelectSql} WHERE eventKey = @eventKey", new { eventKey });
            return Ok(new BaseResponse<EventConfigDto?>(1, "Cập nhật thành công — đã áp dụng ngay", updated));
        }

        /// <summary>Xoá cấu hình sự kiện (event class trong code sẽ coi như chưa cấu hình -> IsActive luôn false).</summary>
        [HttpDelete("/v1/gopet/api/EventConfigs/{eventKey}")]
        public IActionResult DeleteEventConfig(string eventKey)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<EventConfigDto>($"{SelectSql} WHERE eventKey = @eventKey", new { eventKey });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy sự kiện", null));
            }

            conn.Execute("DELETE FROM `event_config` WHERE eventKey = @eventKey", new { eventKey });

            EventConfigManager.Reload();

            return Ok(new BaseResponse<EventConfigDto?>(1, "Xoá sự kiện thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
