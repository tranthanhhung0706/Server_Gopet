using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý mẫu danh hiệu (bảng `achievement`, DB game gopettae_tae2). Không có FK nào tham
    /// chiếu tới bảng này (kiểm tra information_schema.KEY_COLUMN_USAGE) nên xoá không cần bắt lỗi
    /// FK như GopetController (gopet_pet).
    ///
    /// Bảo mật giống GopetController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/achievement")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class AchievementController : ControllerBase
    {
        // Cột thật trong DB là `Int` — phải bọc backtick vì INT là kiểu dữ liệu dành riêng của
        // MySQL/MariaDB, dùng trần làm tên cột sẽ lỗi cú pháp SQL.
        private const string SelectAchievementSql =
            @"SELECT IdTemplate AS IdTemplate, Name AS Name, Description AS Description,
                     IconPath AS IconPath, FramePath AS FramePath, IsVertically AS IsVertically,
                     FrameNum AS FrameNum, Atk AS Atk, Def AS Def, Hp AS Hp, Mp AS Mp,
                     `Int` AS `Int`, Str AS Str, Agi AS Agi, Expire AS Expire, vX AS VX, vY AS VY
              FROM `achievement`";

        private const string AssetFolder = "achievement";

        /// <summary>
        /// Tự thêm prefix thư mục asset đúng convention hiện có trong DB (vd
        /// "achievement/icon_daigia.png" — xem HttpServer.cs UseStaticFiles) nếu admin chỉ gõ tên
        /// file trần. Không thêm nếu đã có prefix rồi (tránh lặp "achievement/achievement/...").
        /// </summary>
        private static string? NormalizeAssetPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith(AssetFolder + "/", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{AssetFolder}/{trimmed}";
        }

        /// <summary>Upload ảnh icon danh hiệu — trả về path để điền vào field IconPath.</summary>
        [HttpPost("/v1/gopet/api/Achievements/upload/icon")]
        [RequestSizeLimit(AssetUploadHelper.MaxImageUploadBytes)]
        public Task<IActionResult> UploadIcon(IFormFile? file)
        {
            return AssetUploadHelper.SaveUploadedImage(file, AssetFolder);
        }

        /// <summary>Upload ảnh khung (frame) danh hiệu — trả về path để điền vào field FramePath.</summary>
        [HttpPost("/v1/gopet/api/Achievements/upload/frame")]
        [RequestSizeLimit(AssetUploadHelper.MaxImageUploadBytes)]
        public Task<IActionResult> UploadFrame(IFormFile? file)
        {
            return AssetUploadHelper.SaveUploadedImage(file, AssetFolder);
        }

        /// <summary>Danh sách danh hiệu — có phân trang, tìm theo tên.</summary>
        [HttpGet("/v1/gopet/api/Achievements")]
        public IActionResult GetAchievements([FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("Name LIKE @search");
                parameters.Add("search", $"%{search.Trim()}%");
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `achievement` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var rows = conn.Query<AchievementDto>(
                $"{SelectAchievementSql} {whereSql} ORDER BY IdTemplate ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<AchievementDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<AchievementDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 danh hiệu theo IdTemplate.</summary>
        [HttpGet("/v1/gopet/api/Achievements/{id:int}")]
        public IActionResult GetAchievementById(int id)
        {
            using var conn = MYSQLManager.create();

            var row = conn.QueryFirstOrDefault<AchievementDto>($"{SelectAchievementSql} WHERE IdTemplate = @id", new { id });
            if (row == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy danh hiệu", null));
            }

            return Ok(new BaseResponse<AchievementDto>(1, "Thành công", row));
        }

        public record CreateAchievementRequest(
            string Name,
            string? Description,
            string? IconPath,
            string? FramePath,
            bool IsVertically = true,
            int FrameNum = 2,
            int Atk = 0,
            int Def = 0,
            int Hp = 0,
            int Mp = 0,
            int Int = 0,
            int Str = 0,
            int Agi = 0,
            long Expire = 0,
            int VX = 0,
            int VY = 0);

        /// <summary>Tạo danh hiệu mới. IdTemplate tự tăng, không cho admin tự chỉ định.</summary>
        [HttpPost("/v1/gopet/api/Achievements")]
        public IActionResult CreateAchievement([FromBody] CreateAchievementRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu Name", null));
            }

            using var conn = MYSQLManager.create();

            var insertParams = new
            {
                req.Name,
                Description = req.Description ?? "",
                IconPath = NormalizeAssetPath(req.IconPath) ?? "",
                FramePath = NormalizeAssetPath(req.FramePath) ?? "",
                req.IsVertically,
                req.FrameNum,
                req.Atk,
                req.Def,
                req.Hp,
                req.Mp,
                req.Int,
                req.Str,
                req.Agi,
                req.Expire,
                req.VX,
                req.VY,
            };

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `achievement`
                    (Name, Description, IconPath, FramePath, IsVertically, FrameNum, Atk, Def, Hp, Mp, `Int`, Str, Agi, Expire, vX, vY)
                  VALUES
                    (@Name, @Description, @IconPath, @FramePath, @IsVertically, @FrameNum, @Atk, @Def, @Hp, @Mp, @Int, @Str, @Agi, @Expire, @VX, @VY);
                  SELECT LAST_INSERT_ID();",
                insertParams);

            var created = conn.QueryFirstOrDefault<AchievementDto>($"{SelectAchievementSql} WHERE IdTemplate = @id", new { id = newId });
            return Ok(new BaseResponse<AchievementDto?>(1, "Tạo danh hiệu thành công", created));
        }

        public record UpdateAchievementRequest(string? Name, string? Description, string? IconPath, string? FramePath,
            bool? IsVertically, int? FrameNum, int? Atk, int? Def, int? Hp, int? Mp, int? Int, int? Str, int? Agi,
            long? Expire, int? VX, int? VY);

        /// <summary>Cập nhật 1 phần danh hiệu. Không cho đổi IdTemplate (khoá chính, tự tăng).</summary>
        [HttpPatch("/v1/gopet/api/Achievements/{id:int}")]
        public IActionResult UpdateAchievement(int id, [FromBody] UpdateAchievementRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<AchievementDto>($"{SelectAchievementSql} WHERE IdTemplate = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy danh hiệu", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("Name = @name"); parameters.Add("name", req.Name); }
            if (req?.Description != null) { setClauses.Add("Description = @description"); parameters.Add("description", req.Description); }
            if (req?.IconPath != null) { setClauses.Add("IconPath = @iconPath"); parameters.Add("iconPath", NormalizeAssetPath(req.IconPath)); }
            if (req?.FramePath != null) { setClauses.Add("FramePath = @framePath"); parameters.Add("framePath", NormalizeAssetPath(req.FramePath)); }
            if (req?.IsVertically is bool isVertically) { setClauses.Add("IsVertically = @isVertically"); parameters.Add("isVertically", isVertically); }
            if (req?.FrameNum is int frameNum) { setClauses.Add("FrameNum = @frameNum"); parameters.Add("frameNum", frameNum); }
            if (req?.Atk is int atk) { setClauses.Add("Atk = @atk"); parameters.Add("atk", atk); }
            if (req?.Def is int def) { setClauses.Add("Def = @def"); parameters.Add("def", def); }
            if (req?.Hp is int hp) { setClauses.Add("Hp = @hp"); parameters.Add("hp", hp); }
            if (req?.Mp is int mp) { setClauses.Add("Mp = @mp"); parameters.Add("mp", mp); }
            if (req?.Int is int intVal) { setClauses.Add("`Int` = @intVal"); parameters.Add("intVal", intVal); }
            if (req?.Str is int str) { setClauses.Add("Str = @str"); parameters.Add("str", str); }
            if (req?.Agi is int agi) { setClauses.Add("Agi = @agi"); parameters.Add("agi", agi); }
            if (req?.Expire is long expire) { setClauses.Add("Expire = @expire"); parameters.Add("expire", expire); }
            if (req?.VX is int vX) { setClauses.Add("vX = @vX"); parameters.Add("vX", vX); }
            if (req?.VY is int vY) { setClauses.Add("vY = @vY"); parameters.Add("vY", vY); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `achievement` SET {string.Join(", ", setClauses)} WHERE IdTemplate = @id", parameters);

            var updated = conn.QueryFirstOrDefault<AchievementDto>($"{SelectAchievementSql} WHERE IdTemplate = @id", new { id });
            return Ok(new BaseResponse<AchievementDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá danh hiệu. Không có bảng nào FK tới achievement nên không cần bắt lỗi khoá ngoại.</summary>
        [HttpDelete("/v1/gopet/api/Achievements/{id:int}")]
        public IActionResult DeleteAchievement(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<AchievementDto>($"{SelectAchievementSql} WHERE IdTemplate = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy danh hiệu", null));
            }

            conn.Execute("DELETE FROM `achievement` WHERE IdTemplate = @id", new { id });

            return Ok(new BaseResponse<AchievementDto?>(1, "Xoá danh hiệu thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
