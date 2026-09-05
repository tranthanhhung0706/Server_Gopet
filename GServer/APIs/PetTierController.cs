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
    /// Quản lý công thức tiến hoá pet (bảng `pet_tier`, DB game gopettae_tae2) — dùng bởi
    /// GameController.petUpTier() (tiêu 2 pet: chính + mồi, ra 1 pet loài mới). Sửa/thêm/xoá ở
    /// đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc khởi động
    /// (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp thêm
    /// GopetManager.ReloadPetTier()) hoặc restart GServer để áp dụng.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/pet-tier")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class PetTierController : ControllerBase
    {
        private const string SelectPetTierSql =
            @"SELECT tierId AS TierId, petTemplateId1 AS PetTemplateId1, petTemplateId2 AS PetTemplateId2,
                     petTemplateIdNeed AS PetTemplateIdNeed
              FROM `pet_tier`";

        /// <summary>Danh sách công thức tiến hoá — có phân trang.</summary>
        [HttpGet("/v1/gopet/api/PetTiers")]
        public IActionResult GetPetTiers([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `pet_tier`");

            var petTiers = conn.Query<PetTierDto>(
                $"{SelectPetTierSql} ORDER BY petTemplateId1 ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<PetTierDto>(petTiers, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<PetTierDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 công thức theo id.</summary>
        [HttpGet("/v1/gopet/api/PetTiers/{id:int}")]
        public IActionResult GetPetTierById(int id)
        {
            using var conn = MYSQLManager.create();

            var petTier = conn.QueryFirstOrDefault<PetTierDto>($"{SelectPetTierSql} WHERE tierId = @id", new { id });
            if (petTier == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức tiến hoá", null));
            }

            return Ok(new BaseResponse<PetTierDto>(1, "Thành công", petTier));
        }

        public record CreatePetTierRequest(int PetTemplateId1, int PetTemplateId2, int PetTemplateIdNeed);

        /// <summary>Tạo công thức tiến hoá mới. tierId tự tăng.</summary>
        [HttpPost("/v1/gopet/api/PetTiers")]
        public IActionResult CreatePetTier([FromBody] CreatePetTierRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetTemplateId1))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetTemplateId1} (pet chính)", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetTemplateId2))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetTemplateId2} (pet kết quả)", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetTemplateIdNeed))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetTemplateIdNeed} (pet mồi)", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `pet_tier` (petTemplateId1, petTemplateId2, petTemplateIdNeed)
                  VALUES (@PetTemplateId1, @PetTemplateId2, @PetTemplateIdNeed);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<PetTierDto>($"{SelectPetTierSql} WHERE tierId = @id", new { id = newId });
            return Ok(new BaseResponse<PetTierDto?>(1, "Tạo công thức tiến hoá thành công", created));
        }

        public record UpdatePetTierRequest(int? PetTemplateId1, int? PetTemplateId2, int? PetTemplateIdNeed);

        /// <summary>Cập nhật 1 phần công thức. Không cho đổi tierId (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/PetTiers/{id:int}")]
        public IActionResult UpdatePetTier(int id, [FromBody] UpdatePetTierRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<PetTierDto>($"{SelectPetTierSql} WHERE tierId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức tiến hoá", null));
            }

            if (req?.PetTemplateId1 is int p1Val && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(p1Val))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {p1Val} (pet chính)", null));
            }
            if (req?.PetTemplateId2 is int p2Val && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(p2Val))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {p2Val} (pet kết quả)", null));
            }
            if (req?.PetTemplateIdNeed is int pNeedVal && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(pNeedVal))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {pNeedVal} (pet mồi)", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.PetTemplateId1 is int petTemplateId1) { setClauses.Add("petTemplateId1 = @petTemplateId1"); parameters.Add("petTemplateId1", petTemplateId1); }
            if (req?.PetTemplateId2 is int petTemplateId2) { setClauses.Add("petTemplateId2 = @petTemplateId2"); parameters.Add("petTemplateId2", petTemplateId2); }
            if (req?.PetTemplateIdNeed is int petTemplateIdNeed) { setClauses.Add("petTemplateIdNeed = @petTemplateIdNeed"); parameters.Add("petTemplateIdNeed", petTemplateIdNeed); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `pet_tier` SET {string.Join(", ", setClauses)} WHERE tierId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<PetTierDto>($"{SelectPetTierSql} WHERE tierId = @id", new { id });
            return Ok(new BaseResponse<PetTierDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá công thức tiến hoá.</summary>
        [HttpDelete("/v1/gopet/api/PetTiers/{id:int}")]
        public IActionResult DeletePetTier(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<PetTierDto>($"{SelectPetTierSql} WHERE tierId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức tiến hoá", null));
            }

            conn.Execute("DELETE FROM `pet_tier` WHERE tierId = @id", new { id });

            return Ok(new BaseResponse<PetTierDto?>(1, "Xoá công thức tiến hoá thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
