using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý vật phẩm rớt ra khi quái chết theo map (bảng `drop_item`, DB game gopettae_tae2) —
    /// xem PetBattle.cs (đoạn quái chết) để biết chính xác cách roll (random chọn 1 dòng trong danh
    /// sách map đó, rồi roll Percent của đúng dòng được chọn). Khoá chính DropId tự tăng.
    ///
    /// Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc
    /// khởi động (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp
    /// thêm GopetManager.ReloadDropItem()) hoặc restart GServer để áp dụng — giống MobLvlMapController.
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/drop-item")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class DropItemController : ControllerBase
    {
        private const string SelectDropItemSql =
            @"SELECT dropId AS DropId, mapId AS MapId, itemTemplateId AS ItemTemplateId,
                     percent AS Percent, lvlRange AS LvlRange, count AS Count
              FROM `drop_item`";

        /// <summary>Danh sách cấu hình rớt đồ — có phân trang, lọc theo mapId/itemTemplateId.</summary>
        [HttpGet("/v1/gopet/api/DropItems")]
        public IActionResult GetDropItems([FromQuery] int page = 1, [FromQuery] int limit = 50,
            [FromQuery] int? mapId = null, [FromQuery] int? itemTemplateId = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();
            if (mapId.HasValue)
            {
                where.Add("mapId = @mapId");
                parameters.Add("mapId", mapId.Value);
            }
            if (itemTemplateId.HasValue)
            {
                where.Add("itemTemplateId = @itemTemplateId");
                parameters.Add("itemTemplateId", itemTemplateId.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `drop_item` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var rows = conn.Query<DropItemDto>(
                $"{SelectDropItemSql} {whereSql} ORDER BY mapId ASC, dropId ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<DropItemDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<DropItemDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 dòng cấu hình rớt đồ theo dropId.</summary>
        [HttpGet("/v1/gopet/api/DropItems/{id:int}")]
        public IActionResult GetDropItemById(int id)
        {
            using var conn = MYSQLManager.create();

            var row = conn.QueryFirstOrDefault<DropItemDto>($"{SelectDropItemSql} WHERE dropId = @id", new { id });
            if (row == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấu hình rớt đồ", null));
            }

            return Ok(new BaseResponse<DropItemDto>(1, "Thành công", row));
        }

        public record CreateDropItemRequest(int MapId, int ItemTemplateId, float Percent = 10, int[]? LvlRange = null, int Count = 1);

        /// <summary>Tạo cấu hình rớt đồ mới. dropId tự tăng.</summary>
        [HttpPost("/v1/gopet/api/DropItems")]
        public IActionResult CreateDropItem([FromBody] CreateDropItemRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.mapTemplate.ContainsKey(req.MapId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {req.MapId}", null));
            }
            if (!GopetManager.itemTemplate.ContainsKey(req.ItemTemplateId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy item id = {req.ItemTemplateId}", null));
            }
            if (req.LvlRange != null && req.LvlRange.Length != 2)
            {
                return BadRequest(new BaseResponse<object?>(0, "lvlRange phải có đúng 2 phần tử [từ, đến] hoặc để trống", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `drop_item` (mapId, itemTemplateId, percent, lvlRange, count)
                  VALUES (@MapId, @ItemTemplateId, @Percent, @LvlRange, @Count);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<DropItemDto>($"{SelectDropItemSql} WHERE dropId = @id", new { id = newId });
            return Ok(new BaseResponse<DropItemDto?>(1, "Tạo cấu hình rớt đồ thành công", created));
        }

        public record UpdateDropItemRequest(int? MapId, int? ItemTemplateId, float? Percent, int[]? LvlRange, bool ClearLvlRange = false, int? Count = null);

        /// <summary>Cập nhật 1 phần cấu hình rớt đồ. Không cho đổi dropId (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/DropItems/{id:int}")]
        public IActionResult UpdateDropItem(int id, [FromBody] UpdateDropItemRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<DropItemDto>($"{SelectDropItemSql} WHERE dropId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấu hình rớt đồ", null));
            }
            if (req?.MapId is int mapId && !GopetManager.mapTemplate.ContainsKey(mapId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {mapId}", null));
            }
            if (req?.ItemTemplateId is int itemTemplateId && !GopetManager.itemTemplate.ContainsKey(itemTemplateId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy item id = {itemTemplateId}", null));
            }
            if (req?.LvlRange != null && req.LvlRange.Length != 2)
            {
                return BadRequest(new BaseResponse<object?>(0, "lvlRange phải có đúng 2 phần tử [từ, đến] hoặc để trống", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.MapId is int newMapId) { setClauses.Add("mapId = @mapId"); parameters.Add("mapId", newMapId); }
            if (req?.ItemTemplateId is int newItemTemplateId) { setClauses.Add("itemTemplateId = @itemTemplateId"); parameters.Add("itemTemplateId", newItemTemplateId); }
            if (req?.Percent is float percent) { setClauses.Add("percent = @percent"); parameters.Add("percent", percent); }
            if (req?.Count is int count) { setClauses.Add("count = @count"); parameters.Add("count", count); }
            // LvlRange = null (mảng) có 2 nghĩa khác nhau tuỳ có gửi lên hay không — dùng cờ
            // ClearLvlRange riêng để phân biệt "không đổi" (bỏ qua field) với "xoá về null" (không
            // giới hạn cấp) một cách tường minh, tránh JSON null bị hiểu nhầm thành "giữ nguyên".
            if (req?.LvlRange != null)
            {
                setClauses.Add("lvlRange = @lvlRange");
                parameters.Add("lvlRange", req.LvlRange);
            }
            else if (req?.ClearLvlRange == true)
            {
                setClauses.Add("lvlRange = NULL");
            }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `drop_item` SET {string.Join(", ", setClauses)} WHERE dropId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<DropItemDto>($"{SelectDropItemSql} WHERE dropId = @id", new { id });
            return Ok(new BaseResponse<DropItemDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá cấu hình rớt đồ.</summary>
        [HttpDelete("/v1/gopet/api/DropItems/{id:int}")]
        public IActionResult DeleteDropItem(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<DropItemDto>($"{SelectDropItemSql} WHERE dropId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấu hình rớt đồ", null));
            }

            conn.Execute("DELETE FROM `drop_item` WHERE dropId = @id", new { id });

            return Ok(new BaseResponse<DropItemDto?>(1, "Xoá cấu hình rớt đồ thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
