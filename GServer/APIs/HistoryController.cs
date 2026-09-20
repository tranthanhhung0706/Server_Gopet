using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Xem lịch sử thao tác người chơi (bảng `history`, DB gp_log — cùng bảng HistoryManager ghi vào
    /// qua MYSQLManager.createLogConnection()). CHỈ ĐỌC, không có sửa/xoá — log là bằng chứng nên
    /// không cho admin panel xoá.
    ///
    /// Bảng chỉ có index targetId + charname, KHÔNG có index timeDB/log: lọc theo targetId/charname
    /// nhanh, còn sắp xếp toàn bảng theo thời gian, lọc theo khoảng ngày hoặc tìm trong log
    /// (LIKE '%..%') sẽ quét toàn bảng — chậm khi bảng lên hàng triệu dòng. Nên thêm index timeDB:
    ///   ALTER TABLE `history` ADD KEY `timeDB` (`timeDB`);
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/history")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class HistoryController : ControllerBase
    {
        // Cắt log (2000 ký tự) / obj (4000 ký tự) để trang danh sách không tải cả PlayerData JSON
        // hàng trăm KB mỗi dòng.
        private const string SelectHistorySql =
            @"SELECT targetId AS TargetId, charname AS CharName, LEFT(log, 2000) AS Log,
                     LEFT(obj, 4000) AS Obj, COALESCE(CHAR_LENGTH(obj), 0) AS ObjLength, timeDB AS TimeDb
              FROM `history`";

        /// <summary>
        /// Danh sách lịch sử, mới nhất trước. Lọc: targetId (user_id chính xác), charName (khớp chính xác
        /// — dùng index; charNameLike=true thì khớp chứa chuỗi), search (chứa trong log), from/to (timeDB).
        /// </summary>
        [HttpGet("/v1/gopet/api/Histories")]
        public IActionResult GetHistories(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 50,
            [FromQuery] int? targetId = null,
            [FromQuery] string? charName = null,
            [FromQuery] bool charNameLike = false,
            [FromQuery] string? search = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();
            if (targetId.HasValue)
            {
                where.Add("targetId = @targetId");
                parameters.Add("targetId", targetId.Value);
            }
            if (!string.IsNullOrWhiteSpace(charName))
            {
                if (charNameLike)
                {
                    where.Add("charname LIKE @charName");
                    parameters.Add("charName", $"%{EscapeLike(charName.Trim())}%");
                }
                else
                {
                    where.Add("charname = @charName");
                    parameters.Add("charName", charName.Trim());
                }
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("log LIKE @search");
                parameters.Add("search", $"%{EscapeLike(search.Trim())}%");
            }
            if (from.HasValue)
            {
                where.Add("timeDB >= @from");
                parameters.Add("from", from.Value);
            }
            if (to.HasValue)
            {
                where.Add("timeDB <= @to");
                parameters.Add("to", to.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.createLogConnection();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `history` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var rows = conn.Query<HistoryDto>(
                $"{SelectHistorySql} {whereSql} ORDER BY timeDB DESC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<HistoryDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<HistoryDto>>(1, "Thành công", paginated));
        }

        /// <summary>Escape % _ \ để ký tự người dùng gõ không bị hiểu là wildcard của LIKE.</summary>
        private static string EscapeLike(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
