using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý gift code (bảng `gift_code`, DB game gopettae_tae2 — cùng DB với GopetController,
    /// khác gopettae_gopet_web mà UserController dùng).
    ///
    /// `gift_data` là chuỗi JSON dạng int[][] — mỗi phần tử con [type, arg1, arg2, ...], `type`
    /// quyết định các arg còn lại là gì (0=Gold, 1=Coin, 2=Item, 4=Item%, 7=Exp, 8=Energy,
    /// 9=RandomItem, 10=ItemMaxOption, 11=EventPoint, 12=FundClan, 13=Title, 14=Skin,
    /// 15=PetTrial — xem GameController.onReiceiveGift để biết chi tiết arg từng type; type
    /// 3/5/6 có định nghĩa nhưng KHÔNG được xử lý, dùng sẽ không tặng gì). API này chỉ validate
    /// gift_data là JSON int[][] hợp lệ, KHÔNG validate ý nghĩa từng type/arg — sai type/arg vẫn
    /// lưu được, chỉ là lúc redeem trong game sẽ không cho đúng thứ mong muốn.
    ///
    /// `currentUser`/`usersOfUseThis` do gameplay tự quản lý khi người chơi redeem (xem
    /// MenuController.inputDialog.cs) — API này chỉ đọc để hiển thị, KHÔNG cho sửa qua create/update.
    ///
    /// `isForNonActiveUser` (mặc định false): false = code chỉ dùng được cho tài khoản ĐÃ kích hoạt
    /// (role != UserData.ROLE_NON_ACTIVE), true = ngược lại chỉ dùng được cho tài khoản CHƯA kích
    /// hoạt (vd code tân thủ) — 2 nhóm loại trừ nhau, xem check trong MenuController.inputDialog.cs.
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/gift")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class GiftController : ControllerBase
    {
        private const string SelectGiftCodeSql =
            @"SELECT id AS Id, code AS Code, currentUser AS CurrentUser, maxUser AS MaxUser,
                     gift_data AS GiftData, expire AS Expire, usersOfUseThis AS UsersOfUseThis, isClanCode AS IsClanCode,
                     isForNonActiveUser AS IsForNonActiveUser
              FROM `gift_code`";

        /// <summary>gift_data phải là JSON hợp lệ dạng mảng lồng số nguyên (int[][]), không rỗng.</summary>
        private static bool IsValidGiftData(string giftData, out string? error)
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<int[][]>(giftData);
                if (parsed == null || parsed.Length == 0)
                {
                    error = "gift_data phải là mảng khác rỗng, vd [[2,181,100]]";
                    return false;
                }
                foreach (var entry in parsed)
                {
                    if (entry == null || entry.Length == 0)
                    {
                        error = "Mỗi phần tử gift_data phải là mảng số nguyên khác rỗng, vd [2,181,100]";
                        return false;
                    }
                }
                error = null;
                return true;
            }
            catch
            {
                error = "gift_data không phải JSON hợp lệ (phải là mảng lồng số nguyên, vd [[2,181,100]])";
                return false;
            }
        }

        /// <summary>
        /// Danh sách gift code — có phân trang, tìm theo code, lọc isClanCode.
        /// </summary>
        [HttpGet("/v1/gopet/api/GiftCodes")]
        public IActionResult GetGiftCodes([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] string? search = null, [FromQuery] bool? isClanCode = null, [FromQuery] bool? isForNonActiveUser = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("code LIKE @search");
                parameters.Add("search", $"%{search.Trim()}%");
            }
            if (isClanCode.HasValue)
            {
                where.Add("isClanCode = @isClanCode");
                parameters.Add("isClanCode", isClanCode.Value);
            }
            if (isForNonActiveUser.HasValue)
            {
                where.Add("isForNonActiveUser = @isForNonActiveUser");
                parameters.Add("isForNonActiveUser", isForNonActiveUser.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `gift_code` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var codes = conn.Query<GiftCodeDto>(
                $"{SelectGiftCodeSql} {whereSql} ORDER BY id DESC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<GiftCodeDto>(codes, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<GiftCodeDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 gift code theo id.</summary>
        [HttpGet("/v1/gopet/api/GiftCodes/{id:int}")]
        public IActionResult GetGiftCodeById(int id)
        {
            using var conn = MYSQLManager.create();

            var code = conn.QueryFirstOrDefault<GiftCodeDto>($"{SelectGiftCodeSql} WHERE id = @id", new { id });
            if (code == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy gift code", null));
            }

            return Ok(new BaseResponse<GiftCodeDto>(1, "Thành công", code));
        }

        public record CreateGiftCodeRequest(string Code, string GiftData, DateTime Expire, int MaxUser = 1, bool IsClanCode = false, bool IsForNonActiveUser = false);

        /// <summary>Tạo gift code mới. currentUser=0, usersOfUseThis=[] mặc định (gameplay tự cập nhật).</summary>
        [HttpPost("/v1/gopet/api/GiftCodes")]
        public IActionResult CreateGiftCode([FromBody] CreateGiftCodeRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu code", null));
            }

            string code = req.Code.Trim();
            if (!Regex.IsMatch(code, "^[a-zA-Z0-9]+$"))
            {
                return BadRequest(new BaseResponse<object?>(0, "Code chỉ được chứa chữ và số (không dấu, không khoảng trắng)", null));
            }
            if (req.MaxUser < 1)
            {
                return BadRequest(new BaseResponse<object?>(0, "maxUser phải >= 1", null));
            }
            if (!IsValidGiftData(req.GiftData, out string? giftDataError))
            {
                return BadRequest(new BaseResponse<object?>(0, giftDataError!, null));
            }

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `gift_code` WHERE code = @code", new { code });
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "Code đã tồn tại", null));
            }

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `gift_code` (code, currentUser, maxUser, gift_data, expire, usersOfUseThis, isClanCode, isForNonActiveUser)
                  VALUES (@code, 0, @maxUser, @giftData, @expire, '[]', @isClanCode, @isForNonActiveUser);
                  SELECT LAST_INSERT_ID();",
                new { code, maxUser = req.MaxUser, giftData = req.GiftData, expire = req.Expire, isClanCode = req.IsClanCode, isForNonActiveUser = req.IsForNonActiveUser });

            var created = conn.QueryFirstOrDefault<GiftCodeDto>($"{SelectGiftCodeSql} WHERE id = @id", new { id = newId });
            return Ok(new BaseResponse<GiftCodeDto?>(1, "Tạo gift code thành công", created));
        }

        public record UpdateGiftCodeRequest(string? GiftData, int? MaxUser, DateTime? Expire, bool? IsClanCode, bool? IsForNonActiveUser);

        /// <summary>Cập nhật 1 phần gift code. Không cho đổi `code` (định danh mà người chơi đã biết/dùng).</summary>
        [HttpPatch("/v1/gopet/api/GiftCodes/{id:int}")]
        public IActionResult UpdateGiftCode(int id, [FromBody] UpdateGiftCodeRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<GiftCodeDto>($"{SelectGiftCodeSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy gift code", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.GiftData != null)
            {
                if (!IsValidGiftData(req.GiftData, out string? giftDataError))
                {
                    return BadRequest(new BaseResponse<object?>(0, giftDataError!, null));
                }
                setClauses.Add("gift_data = @giftData");
                parameters.Add("giftData", req.GiftData);
            }
            if (req?.MaxUser is int maxUser)
            {
                if (maxUser < 1)
                {
                    return BadRequest(new BaseResponse<object?>(0, "maxUser phải >= 1", null));
                }
                setClauses.Add("maxUser = @maxUser");
                parameters.Add("maxUser", maxUser);
            }
            if (req?.Expire is DateTime expire)
            {
                setClauses.Add("expire = @expire");
                parameters.Add("expire", expire);
            }
            if (req?.IsClanCode is bool isClanCode)
            {
                setClauses.Add("isClanCode = @isClanCode");
                parameters.Add("isClanCode", isClanCode);
            }
            if (req?.IsForNonActiveUser is bool isForNonActiveUser)
            {
                setClauses.Add("isForNonActiveUser = @isForNonActiveUser");
                parameters.Add("isForNonActiveUser", isForNonActiveUser);
            }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `gift_code` SET {string.Join(", ", setClauses)} WHERE id = @id", parameters);

            var updated = conn.QueryFirstOrDefault<GiftCodeDto>($"{SelectGiftCodeSql} WHERE id = @id", new { id });
            return Ok(new BaseResponse<GiftCodeDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá gift code. Không có bảng nào FK tới gift_code nên không cần bắt lỗi FK.</summary>
        [HttpDelete("/v1/gopet/api/GiftCodes/{id:int}")]
        public IActionResult DeleteGiftCode(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<GiftCodeDto>($"{SelectGiftCodeSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy gift code", null));
            }

            conn.Execute("DELETE FROM `gift_code` WHERE id = @id", new { id });

            return Ok(new BaseResponse<GiftCodeDto?>(1, "Xoá gift code thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
