using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Bảng xếp hạng điểm sự kiện Vượt Ải 2026 cho trang admin (bảng `event_challenge_score`, xem
    /// Data/Event/Year2026/ChallengeEvent2026.cs) — luôn query thẳng DB, không cache, nên điểm vừa cộng
    /// trong game hiện ngay (khác bảng xếp hạng trong game cập nhật 5 phút/lần).
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/challenge-event")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ChallengeEventController : ControllerBase
    {
        public record ChallengeScoreDto(int UserId, string Name, long Points, int MobKills, int BossKills, DateTime? UpdatedAt);

        /// <summary>Danh sách điểm sự kiện Vượt Ải — phân trang, sắp theo điểm giảm dần, tìm theo tên nhân vật.</summary>
        [HttpGet("/v1/gopet/api/ChallengeEventLeaderboard")]
        public IActionResult GetLeaderboard([FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            string where = "WHERE Points > 0";
            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(search))
            {
                string escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                where += " AND name LIKE @search";
                parameters.Add("search", $"%{escaped}%");
            }
            parameters.Add("limit", limit);
            parameters.Add("offset", offset);

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `event_challenge_score` {where}", parameters);

            var rows = conn.Query<ChallengeScoreDto>(
                $@"SELECT user_id AS UserId, name AS Name, Points, MobKills, BossKills, UpdatedAt
                   FROM `event_challenge_score` {where}
                   ORDER BY Points DESC, UpdatedAt ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<ChallengeScoreDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<ChallengeScoreDto>>(1, "Thành công", paginated));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
