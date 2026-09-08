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
    /// Quản lý danh mục Boss (bảng `boss`, DB game gopettae_tae2) — chỉ số chiến đấu + phần
    /// thưởng khi hạ boss (xem PetBattle.cs: chỉ người ra đòn giết cuối cùng mới nhận Gift).
    /// Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc
    /// khởi động (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp
    /// thêm GopetManager.ReloadBoss()) hoặc restart GServer để áp dụng.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/boss")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class BossController : ControllerBase
    {
        private const string SelectBossSql =
            @"SELECT bossId AS BossId, name AS Name, petTemplateId AS PetTemplateId, str AS Str,
                     _int AS _int, agi AS Agi, lvl AS Lvl, typeBoss AS TypeBoss, gift AS Gift,
                     exp AS Exp, hp AS Hp, atk AS Atk, HourSummon AS HourSummon,
                     BossMapSummon AS BossMapSummon
              FROM `boss`";

        /// <summary>Danh sách boss — có phân trang, lọc theo typeBoss nếu truyền vào.</summary>
        [HttpGet("/v1/gopet/api/Bosses")]
        public IActionResult GetBosses([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] sbyte? typeBoss = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            string whereClause = typeBoss.HasValue ? "WHERE typeBoss = @typeBoss" : "";
            var parameters = new { typeBoss, limit, offset };

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `boss` {whereClause}", parameters);

            var bosses = conn.Query<BossDto>(
                $"{SelectBossSql} {whereClause} ORDER BY typeBoss ASC, lvl ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<BossDto>(bosses, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<BossDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 boss theo id.</summary>
        [HttpGet("/v1/gopet/api/Bosses/{id:int}")]
        public IActionResult GetBossById(int id)
        {
            using var conn = MYSQLManager.create();

            var boss = conn.QueryFirstOrDefault<BossDto>($"{SelectBossSql} WHERE bossId = @id", new { id });
            if (boss == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy boss", null));
            }

            return Ok(new BaseResponse<BossDto>(1, "Thành công", boss));
        }

        public record CreateBossRequest(string Name, int PetTemplateId, int Str, int _int, int Agi, int Lvl,
            sbyte TypeBoss, string Gift, int Exp, int Hp, int Atk, string? HourSummon, string? BossMapSummon);

        /// <summary>Tạo boss mới. bossId tự tăng.</summary>
        [HttpPost("/v1/gopet/api/Bosses")]
        public IActionResult CreateBoss([FromBody] CreateBossRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
            }
            if (!IsValidGiftData(req.Gift, out string? giftError))
            {
                return BadRequest(new BaseResponse<object?>(0, giftError, null));
            }
            if (!IsValidIntArray(req.HourSummon, out string? hourError))
            {
                return BadRequest(new BaseResponse<object?>(0, hourError, null));
            }
            if (!IsValidIntArray(req.BossMapSummon, out string? mapError))
            {
                return BadRequest(new BaseResponse<object?>(0, mapError, null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetTemplateId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetTemplateId}", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `boss` (name, petTemplateId, str, _int, agi, lvl, typeBoss, gift, exp, hp, atk, HourSummon, BossMapSummon)
                  VALUES (@Name, @PetTemplateId, @Str, @_int, @Agi, @Lvl, @TypeBoss, @Gift, @Exp, @Hp, @Atk, @HourSummon, @BossMapSummon);
                  SELECT LAST_INSERT_ID();",
                new
                {
                    req.Name,
                    req.PetTemplateId,
                    req.Str,
                    req._int,
                    req.Agi,
                    req.Lvl,
                    req.TypeBoss,
                    req.Gift,
                    req.Exp,
                    req.Hp,
                    req.Atk,
                    HourSummon = req.HourSummon ?? "[]",
                    BossMapSummon = req.BossMapSummon ?? "[]",
                });

            var created = conn.QueryFirstOrDefault<BossDto>($"{SelectBossSql} WHERE bossId = @id", new { id = newId });
            return Ok(new BaseResponse<BossDto?>(1, "Tạo boss thành công", created));
        }

        public record UpdateBossRequest(string? Name, int? PetTemplateId, int? Str, int? _int, int? Agi, int? Lvl,
            sbyte? TypeBoss, string? Gift, int? Exp, int? Hp, int? Atk, string? HourSummon, string? BossMapSummon);

        /// <summary>Cập nhật 1 phần boss. Không cho đổi bossId (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/Bosses/{id:int}")]
        public IActionResult UpdateBoss(int id, [FromBody] UpdateBossRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<BossDto>($"{SelectBossSql} WHERE bossId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy boss", null));
            }

            if (req?.Gift != null && !IsValidGiftData(req.Gift, out string? giftError))
            {
                return BadRequest(new BaseResponse<object?>(0, giftError, null));
            }
            if (req?.HourSummon != null && !IsValidIntArray(req.HourSummon, out string? hourError))
            {
                return BadRequest(new BaseResponse<object?>(0, hourError, null));
            }
            if (req?.BossMapSummon != null && !IsValidIntArray(req.BossMapSummon, out string? mapError))
            {
                return BadRequest(new BaseResponse<object?>(0, mapError, null));
            }
            if (req?.PetTemplateId is int petIdVal && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(petIdVal))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {petIdVal}", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.PetTemplateId is int petTemplateId) { setClauses.Add("petTemplateId = @petTemplateId"); parameters.Add("petTemplateId", petTemplateId); }
            if (req?.Str is int str) { setClauses.Add("str = @str"); parameters.Add("str", str); }
            if (req?._int is int intVal) { setClauses.Add("_int = @intVal"); parameters.Add("intVal", intVal); }
            if (req?.Agi is int agi) { setClauses.Add("agi = @agi"); parameters.Add("agi", agi); }
            if (req?.Lvl is int lvl) { setClauses.Add("lvl = @lvl"); parameters.Add("lvl", lvl); }
            if (req?.TypeBoss is sbyte typeBoss) { setClauses.Add("typeBoss = @typeBoss"); parameters.Add("typeBoss", typeBoss); }
            if (req?.Gift != null) { setClauses.Add("gift = @gift"); parameters.Add("gift", req.Gift); }
            if (req?.Exp is int exp) { setClauses.Add("exp = @exp"); parameters.Add("exp", exp); }
            if (req?.Hp is int hp) { setClauses.Add("hp = @hp"); parameters.Add("hp", hp); }
            if (req?.Atk is int atk) { setClauses.Add("atk = @atk"); parameters.Add("atk", atk); }
            if (req?.HourSummon != null) { setClauses.Add("HourSummon = @hourSummon"); parameters.Add("hourSummon", req.HourSummon); }
            if (req?.BossMapSummon != null) { setClauses.Add("BossMapSummon = @bossMapSummon"); parameters.Add("bossMapSummon", req.BossMapSummon); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `boss` SET {string.Join(", ", setClauses)} WHERE bossId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<BossDto>($"{SelectBossSql} WHERE bossId = @id", new { id });
            return Ok(new BaseResponse<BossDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá boss.</summary>
        [HttpDelete("/v1/gopet/api/Bosses/{id:int}")]
        public IActionResult DeleteBoss(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<BossDto>($"{SelectBossSql} WHERE bossId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy boss", null));
            }

            conn.Execute("DELETE FROM `boss` WHERE bossId = @id", new { id });

            return Ok(new BaseResponse<BossDto?>(1, "Xoá boss thành công", existing));
        }

        /// <summary>Kiểm tra giftData là JSON mảng 2 chiều số nguyên hợp lệ (giống format gift_code.gift_data).</summary>
        private static bool IsValidGiftData(string? giftData, out string? error)
        {
            if (string.IsNullOrWhiteSpace(giftData))
            {
                error = "Thiếu gift";
                return false;
            }
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<int[][]>(giftData);
                if (parsed == null || parsed.Length == 0 || parsed.Any(row => row.Length < 2))
                {
                    error = "gift phải là mảng 2 chiều, mỗi phần tử tối thiểu [type, id]";
                    return false;
                }
            }
            catch
            {
                error = "gift không phải JSON hợp lệ (vd [[9,3,1,182]])";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Kiểm tra là JSON mảng 1 chiều số nguyên hợp lệ (HourSummon/BossMapSummon) — cho phép mảng rỗng.</summary>
        private static bool IsValidIntArray(string? value, out string? error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Thiếu dữ liệu";
                return false;
            }
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<int[]>(value);
                if (parsed == null)
                {
                    error = "Phải là JSON mảng số nguyên (vd [8,20] hoặc [])";
                    return false;
                }
            }
            catch
            {
                error = "Phải là JSON mảng số nguyên hợp lệ (vd [8,20] hoặc [])";
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
