using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý chỉ số quái theo cấp độ (bảng `gopet_mob`, DB game gopettae_tae2) — xem
    /// Mob.initMob() tra đúng dòng theo cấp random được để lấy hp, GameObject.getAtk() dùng
    /// Str/Agi/Int để tính sát thương. Khoá chính là Lvl (không auto-increment, admin tự nhập).
    ///
    /// Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc
    /// khởi động (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp
    /// thêm GopetManager.ReloadGopetMob()) hoặc restart GServer để áp dụng — giống MobLvlMapController.
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/gopet-mob")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class GopetMobController : ControllerBase
    {
        // `Int` phải bọc backtick khi dùng làm alias — INT là kiểu dữ liệu (từ khoá dành riêng) của
        // MySQL/MariaDB, dùng trần làm alias sẽ vỡ cú pháp SQL ("You have an error in your SQL syntax...").
        private const string SelectGopetMobSql =
            @"SELECT lvl AS Lvl, str AS Str, _int AS `Int`, agi AS Agi, exp AS Exp, coin AS Coin, hp AS Hp
              FROM `gopet_mob`";

        /// <summary>Danh sách chỉ số quái theo cấp độ — có phân trang, sắp theo cấp tăng dần.</summary>
        [HttpGet("/v1/gopet/api/GopetMobs")]
        public IActionResult GetGopetMobs([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `gopet_mob`");

            var rows = conn.Query<GopetMobDto>(
                $"{SelectGopetMobSql} ORDER BY lvl ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<GopetMobDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<GopetMobDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết chỉ số quái theo 1 cấp độ cụ thể.</summary>
        [HttpGet("/v1/gopet/api/GopetMobs/{lvl:int}")]
        public IActionResult GetGopetMobByLvl(int lvl)
        {
            using var conn = MYSQLManager.create();

            var row = conn.QueryFirstOrDefault<GopetMobDto>($"{SelectGopetMobSql} WHERE lvl = @lvl", new { lvl });
            if (row == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấp độ quái này", null));
            }

            return Ok(new BaseResponse<GopetMobDto>(1, "Thành công", row));
        }

        public record CreateGopetMobRequest(int Lvl, int Str = 1, int Int = 1, int Agi = 1, int Exp = 0, int Coin = 0, int Hp = 0);

        /// <summary>Thêm cấu hình chỉ số quái cho 1 cấp độ mới. Lvl do admin chỉ định.</summary>
        [HttpPost("/v1/gopet/api/GopetMobs")]
        public IActionResult CreateGopetMob([FromBody] CreateGopetMobRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `gopet_mob` WHERE lvl = @Lvl", req);
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "Cấp độ này đã tồn tại", null));
            }

            conn.Execute(
                "INSERT INTO `gopet_mob` (lvl, str, _int, agi, exp, coin, hp) VALUES (@Lvl, @Str, @Int, @Agi, @Exp, @Coin, @Hp)",
                req);

            var created = conn.QueryFirstOrDefault<GopetMobDto>($"{SelectGopetMobSql} WHERE lvl = @Lvl", req);
            return Ok(new BaseResponse<GopetMobDto?>(1, "Tạo cấu hình cấp độ quái thành công", created));
        }

        public record UpdateGopetMobRequest(int? Str, int? Int, int? Agi, int? Exp, int? Coin, int? Hp);

        /// <summary>Cập nhật 1 phần chỉ số quái theo cấp độ. Không cho đổi Lvl (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/GopetMobs/{lvl:int}")]
        public IActionResult UpdateGopetMob(int lvl, [FromBody] UpdateGopetMobRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<GopetMobDto>($"{SelectGopetMobSql} WHERE lvl = @lvl", new { lvl });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấp độ quái này", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("lvl", lvl);

            if (req?.Str is int str) { setClauses.Add("str = @str"); parameters.Add("str", str); }
            if (req?.Int is int intStat) { setClauses.Add("_int = @intStat"); parameters.Add("intStat", intStat); }
            if (req?.Agi is int agi) { setClauses.Add("agi = @agi"); parameters.Add("agi", agi); }
            if (req?.Exp is int exp) { setClauses.Add("exp = @exp"); parameters.Add("exp", exp); }
            if (req?.Coin is int coin) { setClauses.Add("coin = @coin"); parameters.Add("coin", coin); }
            if (req?.Hp is int hp) { setClauses.Add("hp = @hp"); parameters.Add("hp", hp); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `gopet_mob` SET {string.Join(", ", setClauses)} WHERE lvl = @lvl", parameters);

            var updated = conn.QueryFirstOrDefault<GopetMobDto>($"{SelectGopetMobSql} WHERE lvl = @lvl", new { lvl });
            return Ok(new BaseResponse<GopetMobDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá cấu hình chỉ số quái theo cấp độ.</summary>
        [HttpDelete("/v1/gopet/api/GopetMobs/{lvl:int}")]
        public IActionResult DeleteGopetMob(int lvl)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<GopetMobDto>($"{SelectGopetMobSql} WHERE lvl = @lvl", new { lvl });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấp độ quái này", null));
            }

            conn.Execute("DELETE FROM `gopet_mob` WHERE lvl = @lvl", new { lvl });

            return Ok(new BaseResponse<GopetMobDto?>(1, "Xoá cấu hình thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
