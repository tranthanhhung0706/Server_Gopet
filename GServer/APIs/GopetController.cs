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
    /// Quản lý pet template/species (bảng `gopet_pet`, DB game gopettae_tae2 — KHÁC với
    /// gopettae_gopet_web mà UserController dùng). Đây là bảng "danh mục" species pet (base
    /// stats, tên, icon...), KHÔNG phải pet của từng người chơi (cái đó lưu JSON trong
    /// player.pets, không có bảng riêng — xem PlayerData.cs/Pet.cs).
    ///
    /// Khác User: `gopet_pet` có FK THẬT (InnoDB) — element → pet_element, nclass → pet_class,
    /// và bị boss/pet_tier tham chiếu ngược lại petId. Nên create/update bắt lỗi FK 1452 (giá trị
    /// element/nclass không tồn tại), delete bắt lỗi FK 1451 (đang bị bảng khác tham chiếu).
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/pet")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class GopetController : ControllerBase
    {
        // Cột thật trong DB là `_int` (đặt tên vậy vì "int" là từ khoá C#/SQL nhạy cảm) — alias
        // sạch sang "Int" cho public API. Phải bọc backtick `Int` vì INT là keyword dành riêng
        // của MySQL/MariaDB (kiểu dữ liệu) — dùng trần làm alias sẽ lỗi cú pháp SQL.
        private const string SelectPetTemplateSql =
            @"SELECT petId AS PetId, name AS Name, icon AS Icon, frameImg AS FrameImg, frameNum AS FrameNum,
                     vY AS VY, hp AS Hp, mp AS Mp, str AS Str, _int AS `Int`, agi AS Agi, type AS Type,
                     nclass AS Nclass, element AS Element, gymUpLevel AS GymUpLevel, FusionScore AS FusionScore
              FROM `gopet_pet`";

        private const string IconFolder = "icons";
        private const string FrameImgFolder = "petFrame3";

        /// <summary>
        /// Tự thêm prefix thư mục asset đúng convention hiện có trong DB (vd "icons/1.icon.png",
        /// "petFrame3/1.png" — xem HttpServer.cs UseStaticFiles) nếu admin chỉ gõ tên file trần
        /// (vd "phuonghoang2"). Không thêm nếu đã có prefix rồi (tránh lặp "icons/icons/...").
        /// </summary>
        private static string? NormalizeAssetPath(string? value, string folder)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{folder}/{trimmed}";
        }

        /// <summary>
        /// Danh sách pet template — có phân trang, tìm theo tên, lọc theo nclass/element/type.
        /// </summary>
        [HttpGet("/v1/gopet/api/Pets")]
        public IActionResult GetPets([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] string? search = null, [FromQuery] int? nclass = null,
            [FromQuery] int? element = null, [FromQuery] int? type = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("name LIKE @search");
                parameters.Add("search", $"%{search.Trim()}%");
            }
            if (nclass.HasValue)
            {
                where.Add("nclass = @nclass");
                parameters.Add("nclass", nclass.Value);
            }
            if (element.HasValue)
            {
                where.Add("element = @element");
                parameters.Add("element", element.Value);
            }
            if (type.HasValue)
            {
                where.Add("type = @type");
                parameters.Add("type", type.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `gopet_pet` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var pets = conn.Query<PetTemplateDto>(
                $"{SelectPetTemplateSql} {whereSql} ORDER BY petId ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<PetTemplateDto>(pets, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<PetTemplateDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 pet template theo petId.</summary>
        [HttpGet("/v1/gopet/api/Pets/{id:int}")]
        public IActionResult GetPetById(int id)
        {
            using var conn = MYSQLManager.create();

            var pet = conn.QueryFirstOrDefault<PetTemplateDto>($"{SelectPetTemplateSql} WHERE petId = @id", new { id });
            if (pet == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy pet", null));
            }

            return Ok(new BaseResponse<PetTemplateDto>(1, "Thành công", pet));
        }

        public record CreatePetRequest(
            int PetId,
            string Name,
            string? Icon,
            string? FrameImg,
            int FrameNum = 2,
            int VY = 0,
            int Hp = 0,
            int Mp = 0,
            int Str = 0,
            int Int = 0,
            int Agi = 0,
            int Type = 0,
            int Nclass = 0,
            int Element = 0,
            int GymUpLevel = 3,
            int FusionScore = 0);

        /// <summary>Tạo pet template mới. petId do admin chỉ định (không auto-increment, giống dữ liệu gốc).</summary>
        [HttpPost("/v1/gopet/api/Pets")]
        public IActionResult CreatePet([FromBody] CreatePetRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
            }

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `gopet_pet` WHERE petId = @PetId", req);
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "petId đã tồn tại", null));
            }

            var insertParams = new
            {
                req.PetId,
                req.Name,
                Icon = NormalizeAssetPath(req.Icon, IconFolder),
                FrameImg = NormalizeAssetPath(req.FrameImg, FrameImgFolder),
                req.FrameNum,
                req.VY,
                req.Hp,
                req.Mp,
                req.Str,
                req.Int,
                req.Agi,
                req.Type,
                req.Nclass,
                req.Element,
                req.GymUpLevel,
                req.FusionScore,
            };

            try
            {
                conn.Execute(
                    @"INSERT INTO `gopet_pet`
                        (petId, name, icon, frameImg, frameNum, vY, hp, mp, str, _int, agi, type, nclass, element, gymUpLevel, FusionScore)
                      VALUES
                        (@PetId, @Name, @Icon, @FrameImg, @FrameNum, @VY, @Hp, @Mp, @Str, @Int, @Agi, @Type, @Nclass, @Element, @GymUpLevel, @FusionScore)",
                    insertParams);
            }
            catch (MySqlException ex) when (ex.Number == 1452)
            {
                return BadRequest(new BaseResponse<object?>(0, "element hoặc nclass không tồn tại trong danh mục pet_element/pet_class", null));
            }

            var created = conn.QueryFirstOrDefault<PetTemplateDto>($"{SelectPetTemplateSql} WHERE petId = @PetId", req);
            return Ok(new BaseResponse<PetTemplateDto?>(1, "Tạo pet thành công", created));
        }

        public record UpdatePetRequest(string? Name, string? Icon, string? FrameImg, int? FrameNum, int? VY,
            int? Hp, int? Mp, int? Str, int? Int, int? Agi, int? Type, int? Nclass, int? Element,
            int? GymUpLevel, int? FusionScore);

        /// <summary>Cập nhật 1 phần pet template. Không cho đổi petId (khoá chính, bị boss/pet_tier tham chiếu).</summary>
        [HttpPatch("/v1/gopet/api/Pets/{id:int}")]
        public IActionResult UpdatePet(int id, [FromBody] UpdatePetRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<PetTemplateDto>($"{SelectPetTemplateSql} WHERE petId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy pet", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Icon != null) { setClauses.Add("icon = @icon"); parameters.Add("icon", NormalizeAssetPath(req.Icon, IconFolder)); }
            if (req?.FrameImg != null) { setClauses.Add("frameImg = @frameImg"); parameters.Add("frameImg", NormalizeAssetPath(req.FrameImg, FrameImgFolder)); }
            if (req?.FrameNum is int frameNum) { setClauses.Add("frameNum = @frameNum"); parameters.Add("frameNum", frameNum); }
            if (req?.VY is int vY) { setClauses.Add("vY = @vY"); parameters.Add("vY", vY); }
            if (req?.Hp is int hp) { setClauses.Add("hp = @hp"); parameters.Add("hp", hp); }
            if (req?.Mp is int mp) { setClauses.Add("mp = @mp"); parameters.Add("mp", mp); }
            if (req?.Str is int str) { setClauses.Add("str = @str"); parameters.Add("str", str); }
            if (req?.Int is int intVal) { setClauses.Add("_int = @intVal"); parameters.Add("intVal", intVal); }
            if (req?.Agi is int agi) { setClauses.Add("agi = @agi"); parameters.Add("agi", agi); }
            if (req?.Type is int type) { setClauses.Add("type = @type"); parameters.Add("type", type); }
            if (req?.Nclass is int nclass) { setClauses.Add("nclass = @nclass"); parameters.Add("nclass", nclass); }
            if (req?.Element is int element) { setClauses.Add("element = @element"); parameters.Add("element", element); }
            if (req?.GymUpLevel is int gymUpLevel) { setClauses.Add("gymUpLevel = @gymUpLevel"); parameters.Add("gymUpLevel", gymUpLevel); }
            if (req?.FusionScore is int fusionScore) { setClauses.Add("FusionScore = @fusionScore"); parameters.Add("fusionScore", fusionScore); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            try
            {
                conn.Execute($"UPDATE `gopet_pet` SET {string.Join(", ", setClauses)} WHERE petId = @id", parameters);
            }
            catch (MySqlException ex) when (ex.Number == 1452)
            {
                return BadRequest(new BaseResponse<object?>(0, "element hoặc nclass không tồn tại trong danh mục pet_element/pet_class", null));
            }

            var updated = conn.QueryFirstOrDefault<PetTemplateDto>($"{SelectPetTemplateSql} WHERE petId = @id", new { id });
            return Ok(new BaseResponse<PetTemplateDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá pet template. Thất bại nếu đang bị `boss`/`pet_tier` tham chiếu (FK).</summary>
        [HttpDelete("/v1/gopet/api/Pets/{id:int}")]
        public IActionResult DeletePet(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<PetTemplateDto>($"{SelectPetTemplateSql} WHERE petId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy pet", null));
            }

            try
            {
                conn.Execute("DELETE FROM `gopet_pet` WHERE petId = @id", new { id });
            }
            catch (MySqlException ex) when (ex.Number == 1451)
            {
                return Conflict(new BaseResponse<object?>(0, "Không thể xoá — pet này đang được tham chiếu bởi Boss hoặc Pet Tier khác", null));
            }

            return Ok(new BaseResponse<PetTemplateDto?>(1, "Xoá pet thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
