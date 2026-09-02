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
    /// Quản lý danh mục "mốc nạp" (bảng `nap_moc_reward`, DB game gopettae_tae2) — mốc tổng nạp
    /// (user.tongnap, DB web gopettae_gopet_web) người chơi cần đạt để nhận thưởng qua NPC (xem
    /// GameController.napMocDaily(), OP_NHẬN_QUÀ_MỐC_NẠP trong MenuController.cs). Sửa/thêm/xoá
    /// mốc ở đây áp dụng NGAY cho lần bấm nhận quà tiếp theo của người chơi — không cần restart
    /// GServer (napMocDaily() luôn query thẳng DB mỗi lần gọi, không cache).
    ///
    /// Khác gopet_pet/item: `nap_moc_reward` không bị bảng nào khác tham chiếu ngược (không cần
    /// bắt lỗi FK khi xoá).
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/nap-moc")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class NapMocController : ControllerBase
    {
        private const string SelectNapMocSql =
            @"SELECT id AS Id, name AS Name, threshold AS Threshold, giftData AS GiftData, usersOfUseThis AS UsersOfUseThis
              FROM `nap_moc_reward`";

        /// <summary>Danh sách mốc nạp — có phân trang, sắp theo threshold tăng dần.</summary>
        [HttpGet("/v1/gopet/api/NapMocRewards")]
        public IActionResult GetNapMocRewards([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `nap_moc_reward`");

            var rewards = conn.Query<NapMocRewardDto>(
                $"{SelectNapMocSql} ORDER BY threshold ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<NapMocRewardDto>(rewards, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<NapMocRewardDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 mốc nạp theo id.</summary>
        [HttpGet("/v1/gopet/api/NapMocRewards/{id:int}")]
        public IActionResult GetNapMocRewardById(int id)
        {
            using var conn = MYSQLManager.create();

            var reward = conn.QueryFirstOrDefault<NapMocRewardDto>($"{SelectNapMocSql} WHERE id = @id", new { id });
            if (reward == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc nạp", null));
            }

            return Ok(new BaseResponse<NapMocRewardDto>(1, "Thành công", reward));
        }

        public record CreateNapMocRewardRequest(string Name, long Threshold, string GiftData);

        /// <summary>Tạo mốc nạp mới. id tự tăng. threshold phải là duy nhất (bảng có UNIQUE KEY).</summary>
        [HttpPost("/v1/gopet/api/NapMocRewards")]
        public IActionResult CreateNapMocReward([FromBody] CreateNapMocRewardRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
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
                    @"INSERT INTO `nap_moc_reward` (name, threshold, giftData)
                      VALUES (@Name, @Threshold, @GiftData);
                      SELECT LAST_INSERT_ID();",
                    req);

                var created = conn.QueryFirstOrDefault<NapMocRewardDto>($"{SelectNapMocSql} WHERE id = @id", new { id = newId });
                return Ok(new BaseResponse<NapMocRewardDto?>(1, "Tạo mốc nạp thành công", created));
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new BaseResponse<object?>(0, "Threshold này đã tồn tại", null));
            }
        }

        public record UpdateNapMocRewardRequest(string? Name, long? Threshold, string? GiftData);

        /// <summary>Cập nhật 1 phần mốc nạp. Không cho đổi id (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/NapMocRewards/{id:int}")]
        public IActionResult UpdateNapMocReward(int id, [FromBody] UpdateNapMocRewardRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<NapMocRewardDto>($"{SelectNapMocSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc nạp", null));
            }

            if (req?.GiftData != null && !IsValidGiftData(req.GiftData, out string? giftError))
            {
                return BadRequest(new BaseResponse<object?>(0, giftError, null));
            }
            if (req?.Threshold is long threshold0 && threshold0 <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Threshold phải > 0", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Threshold is long threshold) { setClauses.Add("threshold = @threshold"); parameters.Add("threshold", threshold); }
            if (req?.GiftData != null) { setClauses.Add("giftData = @giftData"); parameters.Add("giftData", req.GiftData); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            try
            {
                conn.Execute($"UPDATE `nap_moc_reward` SET {string.Join(", ", setClauses)} WHERE id = @id", parameters);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new BaseResponse<object?>(0, "Threshold này đã tồn tại", null));
            }

            var updated = conn.QueryFirstOrDefault<NapMocRewardDto>($"{SelectNapMocSql} WHERE id = @id", new { id });
            return Ok(new BaseResponse<NapMocRewardDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá mốc nạp. Không có bảng nào khác tham chiếu ngược nên không cần bắt lỗi FK.</summary>
        [HttpDelete("/v1/gopet/api/NapMocRewards/{id:int}")]
        public IActionResult DeleteNapMocReward(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<NapMocRewardDto>($"{SelectNapMocSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy mốc nạp", null));
            }

            conn.Execute("DELETE FROM `nap_moc_reward` WHERE id = @id", new { id });

            return Ok(new BaseResponse<NapMocRewardDto?>(1, "Xoá mốc nạp thành công", existing));
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
