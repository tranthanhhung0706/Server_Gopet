using Dapper;
using Gopet.Data.item;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý pool "đổi thỏi" (bảng `trade_gift`, DB game gopettae_tae2) — vật phẩm người chơi có
    /// thể nhận khi dùng Thỏi Bạc/Thỏi Vàng đổi thưởng qua NPC Tiên Nữ (xem
    /// MenuController.Trade()/ShowTradeGiftPool()). Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho
    /// gameplay — GServer chỉ nạp bảng này vào RAM lúc khởi động (GopetManager.init()), cần gọi
    /// POST /v1/gopet/api/server/reload-catalog (đã gộp thêm GopetManager.ReloadTradeGift()) hoặc
    /// restart GServer để áp dụng.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/trade-gift")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class TradeGiftController : ControllerBase
    {
        private const string SelectTradeGiftSql =
            @"SELECT TradeId, ItemTemplateId, Count, Type, Percent FROM `trade_gift`";

        /// <summary>Danh sách vật phẩm đổi thỏi — có phân trang, lọc theo Type nếu truyền vào.</summary>
        [HttpGet("/v1/gopet/api/TradeGifts")]
        public IActionResult GetTradeGifts([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] sbyte? type = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            string whereClause = type.HasValue ? "WHERE Type = @type" : "";
            var parameters = new { type, limit, offset };

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `trade_gift` {whereClause}", parameters);

            var rewards = conn.Query<TradeGiftDto>(
                $"{SelectTradeGiftSql} {whereClause} ORDER BY Type ASC, Percent DESC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<TradeGiftDto>(rewards, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<TradeGiftDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 dòng đổi thỏi theo id.</summary>
        [HttpGet("/v1/gopet/api/TradeGifts/{id:int}")]
        public IActionResult GetTradeGiftById(int id)
        {
            using var conn = MYSQLManager.create();

            var reward = conn.QueryFirstOrDefault<TradeGiftDto>($"{SelectTradeGiftSql} WHERE TradeId = @id", new { id });
            if (reward == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy dòng đổi thỏi", null));
            }

            return Ok(new BaseResponse<TradeGiftDto>(1, "Thành công", reward));
        }

        public record CreateTradeGiftRequest(int ItemTemplateId, int Count, sbyte Type, float Percent);

        /// <summary>Tạo dòng đổi thỏi mới. TradeId tự tăng.</summary>
        [HttpPost("/v1/gopet/api/TradeGifts")]
        public IActionResult CreateTradeGift([FromBody] CreateTradeGiftRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!IsValidType(req.Type, out string? typeError))
            {
                return BadRequest(new BaseResponse<object?>(0, typeError, null));
            }
            if (req.Count <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Số lượng phải > 0", null));
            }
            if (req.Percent <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Tỉ lệ phải > 0", null));
            }
            if (!GopetManager.itemTemplate.ContainsKey(req.ItemTemplateId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy vật phẩm id = {req.ItemTemplateId}", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `trade_gift` (ItemTemplateId, Count, Type, Percent)
                  VALUES (@ItemTemplateId, @Count, @Type, @Percent);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<TradeGiftDto>($"{SelectTradeGiftSql} WHERE TradeId = @id", new { id = newId });
            return Ok(new BaseResponse<TradeGiftDto?>(1, "Tạo dòng đổi thỏi thành công", created));
        }

        public record UpdateTradeGiftRequest(int? ItemTemplateId, int? Count, sbyte? Type, float? Percent);

        /// <summary>Cập nhật 1 phần dòng đổi thỏi. Không cho đổi TradeId (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/TradeGifts/{id:int}")]
        public IActionResult UpdateTradeGift(int id, [FromBody] UpdateTradeGiftRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TradeGiftDto>($"{SelectTradeGiftSql} WHERE TradeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy dòng đổi thỏi", null));
            }

            if (req?.Type is sbyte typeVal && !IsValidType(typeVal, out string? typeError))
            {
                return BadRequest(new BaseResponse<object?>(0, typeError, null));
            }
            if (req?.Count is int countVal && countVal <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Số lượng phải > 0", null));
            }
            if (req?.Percent is float percentVal && percentVal <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Tỉ lệ phải > 0", null));
            }
            if (req?.ItemTemplateId is int itemIdVal && !GopetManager.itemTemplate.ContainsKey(itemIdVal))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy vật phẩm id = {itemIdVal}", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.ItemTemplateId is int itemId) { setClauses.Add("ItemTemplateId = @itemId"); parameters.Add("itemId", itemId); }
            if (req?.Count is int count) { setClauses.Add("Count = @count"); parameters.Add("count", count); }
            if (req?.Type is sbyte type) { setClauses.Add("Type = @type"); parameters.Add("type", type); }
            if (req?.Percent is float percent) { setClauses.Add("Percent = @percent"); parameters.Add("percent", percent); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `trade_gift` SET {string.Join(", ", setClauses)} WHERE TradeId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<TradeGiftDto>($"{SelectTradeGiftSql} WHERE TradeId = @id", new { id });
            return Ok(new BaseResponse<TradeGiftDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá dòng đổi thỏi.</summary>
        [HttpDelete("/v1/gopet/api/TradeGifts/{id:int}")]
        public IActionResult DeleteTradeGift(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TradeGiftDto>($"{SelectTradeGiftSql} WHERE TradeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy dòng đổi thỏi", null));
            }

            conn.Execute("DELETE FROM `trade_gift` WHERE TradeId = @id", new { id });

            return Ok(new BaseResponse<TradeGiftDto?>(1, "Xoá dòng đổi thỏi thành công", existing));
        }

        /// <summary>Type chỉ chấp nhận 0 (Thỏi Bạc) hoặc 1 (Thỏi Vàng) — Type 2 (Lúa) không có dữ
        /// liệu riêng trong DB, GServer tự dùng chung pool Type 0 (xem GopetManager.init()).</summary>
        private static bool IsValidType(sbyte type, out string? error)
        {
            if (type != TradeGiftTemplate.TYPE_COIN && type != TradeGiftTemplate.TYPE_GOLD)
            {
                error = "Type chỉ được 0 (Thỏi Bạc) hoặc 1 (Thỏi Vàng)";
                return false;
            }
            error = null;
            return true;
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
