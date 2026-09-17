using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý danh mục "mốc dùng hộp quà Trung Thu" (bảng `trung_thu_milestone`, DB game
    /// gopettae_tae2) — HOÀN TOÀN TÁCH RIÊNG khỏi NapMocController (mốc tổng nạp). Mốc so với
    /// player.NumUseMoonCakeBoxNormal2026 (BoxType=0) hoặc NumUseMoonCakeBoxVip2026 (BoxType=1),
    /// không cần query DB web như nap_moc_reward. Client chọn từng mốc từ danh sách
    /// (MenuController.cs MENU_TRUNG_THU_MILESTONE_NORMAL/VIP) rồi xem thông tin hoặc nhận
    /// (GameController.DescribeGiftData/ClaimTrungThuMilestone). Sửa/thêm/xoá mốc ở đây áp dụng NGAY
    /// cho lần mở danh sách tiếp theo — không cần restart GServer (luôn query thẳng DB, không cache).
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/trung-thu-milestone")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class TrungThuMilestoneController : ControllerBase
    {
        private const string SelectMilestoneSql =
            @"SELECT id AS Id, boxType AS BoxType, name AS Name, threshold AS Threshold,
                     giftData AS GiftData, usersOfUseThis AS UsersOfUseThis
              FROM `trung_thu_milestone`";

        /// <summary>Danh sách mốc — lọc theo boxType (0 = thường, 1 = VIP), sắp theo threshold tăng dần.</summary>
        [HttpGet("/v1/gopet/api/TrungThuMilestones")]
        public IActionResult GetTrungThuMilestones([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] sbyte? boxType = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            string whereSql = boxType.HasValue ? "WHERE boxType = @boxType" : "";
            var parameters = new DynamicParameters();
            if (boxType.HasValue)
            {
                parameters.Add("boxType", boxType.Value);
            }

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `trung_thu_milestone` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var milestones = conn.Query<TrungThuMilestoneDto>(
                $"{SelectMilestoneSql} {whereSql} ORDER BY boxType ASC, threshold ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<TrungThuMilestoneDto>(milestones, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<TrungThuMilestoneDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 mốc theo id.</summary>
        [HttpGet("/v1/gopet/api/TrungThuMilestones/{id:int}")]
        public IActionResult GetTrungThuMilestoneById(int id)
        {
            using var conn = MYSQLManager.create();

            var milestone = conn.QueryFirstOrDefault<TrungThuMilestoneDto>($"{SelectMilestoneSql} WHERE id = @id", new { id });
            if (milestone == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc", null));
            }

            return Ok(new BaseResponse<TrungThuMilestoneDto>(1, "Thành công", milestone));
        }

        public record CreateTrungThuMilestoneRequest(sbyte BoxType, string Name, int Threshold, string GiftData);

        /// <summary>Tạo mốc mới. id tự tăng. (boxType, threshold) phải là duy nhất (bảng có UNIQUE KEY).</summary>
        [HttpPost("/v1/gopet/api/TrungThuMilestones")]
        public IActionResult CreateTrungThuMilestone([FromBody] CreateTrungThuMilestoneRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
            }
            if (req.BoxType != 0 && req.BoxType != 1)
            {
                return BadRequest(new BaseResponse<object?>(0, "boxType phải là 0 (thường) hoặc 1 (VIP)", null));
            }
            if (req.Threshold <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Threshold phải > 0", null));
            }
            if (!IsValidGiftData(req.GiftData, out string? error))
            {
                return BadRequest(new BaseResponse<object?>(0, error, null));
            }

            using var conn = MYSQLManager.create();

            try
            {
                int newId = conn.ExecuteScalar<int>(
                    @"INSERT INTO `trung_thu_milestone` (boxType, name, threshold, giftData)
                      VALUES (@BoxType, @Name, @Threshold, @GiftData);
                      SELECT LAST_INSERT_ID();",
                    req);

                var created = conn.QueryFirstOrDefault<TrungThuMilestoneDto>($"{SelectMilestoneSql} WHERE id = @id", new { id = newId });
                return Ok(new BaseResponse<TrungThuMilestoneDto?>(1, "Tạo mốc thành công", created));
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new BaseResponse<object?>(0, "Threshold này đã tồn tại cho loại hộp này", null));
            }
        }

        public record UpdateTrungThuMilestoneRequest(string? Name, int? Threshold, string? GiftData);

        /// <summary>Cập nhật 1 phần mốc. Không cho đổi id/boxType (khoá).</summary>
        [HttpPatch("/v1/gopet/api/TrungThuMilestones/{id:int}")]
        public IActionResult UpdateTrungThuMilestone(int id, [FromBody] UpdateTrungThuMilestoneRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TrungThuMilestoneDto>($"{SelectMilestoneSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc", null));
            }

            if (req?.GiftData != null && !IsValidGiftData(req.GiftData, out string? giftError))
            {
                return BadRequest(new BaseResponse<object?>(0, giftError, null));
            }
            if (req?.Threshold is int threshold0 && threshold0 <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Threshold phải > 0", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Threshold is int threshold) { setClauses.Add("threshold = @threshold"); parameters.Add("threshold", threshold); }
            if (req?.GiftData != null) { setClauses.Add("giftData = @giftData"); parameters.Add("giftData", req.GiftData); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            try
            {
                conn.Execute($"UPDATE `trung_thu_milestone` SET {string.Join(", ", setClauses)} WHERE id = @id", parameters);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new BaseResponse<object?>(0, "Threshold này đã tồn tại cho loại hộp này", null));
            }

            var updated = conn.QueryFirstOrDefault<TrungThuMilestoneDto>($"{SelectMilestoneSql} WHERE id = @id", new { id });
            return Ok(new BaseResponse<TrungThuMilestoneDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá mốc. Không có bảng nào khác tham chiếu ngược nên không cần bắt lỗi FK.</summary>
        [HttpDelete("/v1/gopet/api/TrungThuMilestones/{id:int}")]
        public IActionResult DeleteTrungThuMilestone(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TrungThuMilestoneDto>($"{SelectMilestoneSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc", null));
            }

            conn.Execute("DELETE FROM `trung_thu_milestone` WHERE id = @id", new { id });

            return Ok(new BaseResponse<TrungThuMilestoneDto?>(1, "Xoá mốc thành công", existing));
        }

        /// <summary>Kiểm tra giftData là JSON mảng 2 chiều số nguyên hợp lệ (giống format gift_code.gift_data).</summary>
        private static bool IsValidGiftData(string? giftData, out string? error)
        {
            if (string.IsNullOrWhiteSpace(giftData))
            {
                error = "Thiếu giftData";
                return false;
            }
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<int[][]>(giftData);
                if (parsed == null || parsed.Length == 0 || parsed.Any(row => row.Length < 2))
                {
                    error = "giftData phải là mảng 2 chiều, mỗi phần tử tối thiểu [type, id]";
                    return false;
                }
            }
            catch
            {
                error = "giftData không phải JSON hợp lệ (vd [[2,198,3,0]])";
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
