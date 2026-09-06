using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý danh mục Xăm (bảng `tattoo`, DB game gopettae_tae2) — chỉ số cộng thêm khi pet mang
    /// xăm (atk/def/hp/mp qua Pet.applyInfo()) và hiệu ứng combat đặc biệt tuỳ chọn (phản đòn/hút
    /// máu/định thân... qua itemOption/itemOptionValue, xem PetBattle.AddTattoBattleBuff()).
    /// Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc
    /// khởi động (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp
    /// thêm GopetManager.ReloadTattoo()) hoặc restart GServer để áp dụng.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/tattoo")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class TattooController : ControllerBase
    {
        private const int OPTION_BATTLE = 13;

        private const string SelectTattooSql =
            @"SELECT tattooId AS TattooId, name AS Name, type AS Type, iconPath AS IconPath,
                     atk AS Atk, def AS Def, hp AS Hp, mp AS Mp, percent AS Percent,
                     itemOption AS ItemOption, itemOptionValue AS ItemOptionValue
              FROM `tattoo`";

        /// <summary>Danh sách xăm — có phân trang, tìm theo tên.</summary>
        [HttpGet("/v1/gopet/api/Tattoos")]
        public IActionResult GetTattoos([FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            string whereClause = string.IsNullOrWhiteSpace(search) ? "" : "WHERE name LIKE @search";
            var parameters = new { search = $"%{search?.Trim()}%", limit, offset };

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `tattoo` {whereClause}", parameters);

            var tattoos = conn.Query<TattooDto>(
                $"{SelectTattooSql} {whereClause} ORDER BY tattooId ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<TattooDto>(tattoos, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<TattooDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 xăm theo id.</summary>
        [HttpGet("/v1/gopet/api/Tattoos/{id:int}")]
        public IActionResult GetTattooById(int id)
        {
            using var conn = MYSQLManager.create();

            var tattoo = conn.QueryFirstOrDefault<TattooDto>($"{SelectTattooSql} WHERE tattooId = @id", new { id });
            if (tattoo == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy xăm", null));
            }

            return Ok(new BaseResponse<TattooDto>(1, "Thành công", tattoo));
        }

        public record CreateTattooRequest(string Name, sbyte Type, string IconPath, int Atk, int Def, int Hp, int Mp,
            float Percent, string? ItemOption, string? ItemOptionValue);

        /// <summary>Tạo xăm mới. tattooId tự tăng.</summary>
        [HttpPost("/v1/gopet/api/Tattoos")]
        public IActionResult CreateTattoo([FromBody] CreateTattooRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
            }
            if (!IsValidBattleOptionArrays(req.ItemOption, req.ItemOptionValue, out string? optionError))
            {
                return BadRequest(new BaseResponse<object?>(0, optionError!, null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `tattoo` (name, type, iconPath, atk, def, hp, mp, percent, itemOption, itemOptionValue)
                  VALUES (@Name, @Type, @IconPath, @Atk, @Def, @Hp, @Mp, @Percent, @ItemOption, @ItemOptionValue);
                  SELECT LAST_INSERT_ID();",
                new
                {
                    req.Name,
                    req.Type,
                    req.IconPath,
                    req.Atk,
                    req.Def,
                    req.Hp,
                    req.Mp,
                    req.Percent,
                    ItemOption = req.ItemOption ?? "[]",
                    ItemOptionValue = req.ItemOptionValue ?? "[]",
                });

            var created = conn.QueryFirstOrDefault<TattooDto>($"{SelectTattooSql} WHERE tattooId = @id", new { id = newId });
            return Ok(new BaseResponse<TattooDto?>(1, "Tạo xăm thành công", created));
        }

        public record UpdateTattooRequest(string? Name, sbyte? Type, string? IconPath, int? Atk, int? Def, int? Hp,
            int? Mp, float? Percent, string? ItemOption, string? ItemOptionValue);

        /// <summary>Cập nhật 1 phần xăm. Không cho đổi tattooId (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/Tattoos/{id:int}")]
        public IActionResult UpdateTattoo(int id, [FromBody] UpdateTattooRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TattooDto>($"{SelectTattooSql} WHERE tattooId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy xăm", null));
            }

            if ((req?.ItemOption != null || req?.ItemOptionValue != null) &&
                !IsValidBattleOptionArrays(req?.ItemOption ?? existing.ItemOption, req?.ItemOptionValue ?? existing.ItemOptionValue, out string? optionError))
            {
                return BadRequest(new BaseResponse<object?>(0, optionError!, null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Type is sbyte type) { setClauses.Add("type = @type"); parameters.Add("type", type); }
            if (req?.IconPath != null) { setClauses.Add("iconPath = @iconPath"); parameters.Add("iconPath", req.IconPath); }
            if (req?.Atk is int atk) { setClauses.Add("atk = @atk"); parameters.Add("atk", atk); }
            if (req?.Def is int def) { setClauses.Add("def = @def"); parameters.Add("def", def); }
            if (req?.Hp is int hp) { setClauses.Add("hp = @hp"); parameters.Add("hp", hp); }
            if (req?.Mp is int mp) { setClauses.Add("mp = @mp"); parameters.Add("mp", mp); }
            if (req?.Percent is float percent) { setClauses.Add("percent = @percent"); parameters.Add("percent", percent); }
            if (req?.ItemOption != null) { setClauses.Add("itemOption = @itemOption"); parameters.Add("itemOption", req.ItemOption); }
            if (req?.ItemOptionValue != null) { setClauses.Add("itemOptionValue = @itemOptionValue"); parameters.Add("itemOptionValue", req.ItemOptionValue); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `tattoo` SET {string.Join(", ", setClauses)} WHERE tattooId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<TattooDto>($"{SelectTattooSql} WHERE tattooId = @id", new { id });
            return Ok(new BaseResponse<TattooDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá xăm.</summary>
        [HttpDelete("/v1/gopet/api/Tattoos/{id:int}")]
        public IActionResult DeleteTattoo(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TattooDto>($"{SelectTattooSql} WHERE tattooId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy xăm", null));
            }

            conn.Execute("DELETE FROM `tattoo` WHERE tattooId = @id", new { id });

            return Ok(new BaseResponse<TattooDto?>(1, "Xoá xăm thành công", existing));
        }

        /// <summary>
        /// Kiểm tra itemOption/itemOptionValue là JSON mảng số nguyên hợp lệ, cùng độ dài, và mỗi
        /// nhóm hiệu ứng OPTION_BATTLE (13) có đủ 4 phần tử [effectTypeId, turn, value, isActive] —
        /// tránh IndexOutOfRange lúc PetBattle đọc lúc vào trận. Cho phép "[]"/rỗng (không hiệu ứng).
        /// </summary>
        private static bool IsValidBattleOptionArrays(string? itemOption, string? itemOptionValue, out string? error)
        {
            string optionJson = string.IsNullOrWhiteSpace(itemOption) ? "[]" : itemOption;
            string valueJson = string.IsNullOrWhiteSpace(itemOptionValue) ? "[]" : itemOptionValue;
            int[]? option, value;
            try
            {
                option = Newtonsoft.Json.JsonConvert.DeserializeObject<int[]>(optionJson);
                value = Newtonsoft.Json.JsonConvert.DeserializeObject<int[]>(valueJson);
            }
            catch
            {
                error = "itemOption/itemOptionValue phải là JSON mảng số nguyên hợp lệ (vd [13,14,15,16] / [24,99999,2500,1])";
                return false;
            }
            option ??= Array.Empty<int>();
            value ??= Array.Empty<int>();
            if (option.Length != value.Length)
            {
                error = "itemOption và itemOptionValue phải có cùng độ dài";
                return false;
            }
            for (int i = 0; i < option.Length; i++)
            {
                if (option[i] == OPTION_BATTLE)
                {
                    if (i + 3 >= option.Length)
                    {
                        error = $"Nhóm hiệu ứng combat (giá trị 13) ở vị trí {i} thiếu đủ 4 phần tử [effectTypeId, turn, percentValue, isActive]";
                        return false;
                    }
                    i += 3;
                }
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
