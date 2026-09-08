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
    /// Quản lý công thức trùng sinh pet (bảng `reincarnation`, DB game gopettae_tae2) — dùng bởi
    /// NPC Tiên Nữ, option "Trùng sinh pet" (xem MenuController.selectMenu.cs case
    /// MENU_OPTION_PET_REINCARNATION). Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay —
    /// GServer chỉ nạp bảng này vào RAM lúc khởi động (GopetManager.init()), cần gọi POST
    /// /v1/gopet/api/server/reload-catalog (đã gộp thêm GopetManager.ReloadReincarnation()) hoặc
    /// restart GServer để áp dụng.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/reincarnation")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ReincarnationController : ControllerBase
    {
        private const string SelectReincarnationSql =
            @"SELECT Id, PetId, NumCard, PetIdReincarnation FROM `reincarnation`";

        /// <summary>Danh sách công thức trùng sinh — có phân trang.</summary>
        [HttpGet("/v1/gopet/api/Reincarnations")]
        public IActionResult GetReincarnations([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `reincarnation`");

            var reincarnations = conn.Query<ReincarnationDto>(
                $"{SelectReincarnationSql} ORDER BY PetId ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<ReincarnationDto>(reincarnations, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<ReincarnationDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 công thức theo id.</summary>
        [HttpGet("/v1/gopet/api/Reincarnations/{id:int}")]
        public IActionResult GetReincarnationById(int id)
        {
            using var conn = MYSQLManager.create();

            var reincarnation = conn.QueryFirstOrDefault<ReincarnationDto>($"{SelectReincarnationSql} WHERE Id = @id", new { id });
            if (reincarnation == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức trùng sinh", null));
            }

            return Ok(new BaseResponse<ReincarnationDto>(1, "Thành công", reincarnation));
        }

        public record CreateReincarnationRequest(int PetId, int NumCard, int PetIdReincarnation);

        /// <summary>Tạo công thức trùng sinh mới. Id tự tăng.</summary>
        [HttpPost("/v1/gopet/api/Reincarnations")]
        public IActionResult CreateReincarnation([FromBody] CreateReincarnationRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (req.NumCard <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Số thẻ trùng sinh phải > 0", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetId}", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetIdReincarnation))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {req.PetIdReincarnation} (pet sau trùng sinh)", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `reincarnation` (PetId, NumCard, PetIdReincarnation)
                  VALUES (@PetId, @NumCard, @PetIdReincarnation);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<ReincarnationDto>($"{SelectReincarnationSql} WHERE Id = @id", new { id = newId });
            return Ok(new BaseResponse<ReincarnationDto?>(1, "Tạo công thức trùng sinh thành công", created));
        }

        public record UpdateReincarnationRequest(int? PetId, int? NumCard, int? PetIdReincarnation);

        /// <summary>Cập nhật 1 phần công thức. Không cho đổi Id (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/Reincarnations/{id:int}")]
        public IActionResult UpdateReincarnation(int id, [FromBody] UpdateReincarnationRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ReincarnationDto>($"{SelectReincarnationSql} WHERE Id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức trùng sinh", null));
            }

            if (req?.NumCard is int numCardVal && numCardVal <= 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Số thẻ trùng sinh phải > 0", null));
            }
            if (req?.PetId is int petIdVal && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(petIdVal))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {petIdVal}", null));
            }
            if (req?.PetIdReincarnation is int petIdReinVal && !GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(petIdReinVal))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet template id = {petIdReinVal} (pet sau trùng sinh)", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.PetId is int petId) { setClauses.Add("PetId = @petId"); parameters.Add("petId", petId); }
            if (req?.NumCard is int numCard) { setClauses.Add("NumCard = @numCard"); parameters.Add("numCard", numCard); }
            if (req?.PetIdReincarnation is int petIdReincarnation) { setClauses.Add("PetIdReincarnation = @petIdReincarnation"); parameters.Add("petIdReincarnation", petIdReincarnation); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `reincarnation` SET {string.Join(", ", setClauses)} WHERE Id = @id", parameters);

            var updated = conn.QueryFirstOrDefault<ReincarnationDto>($"{SelectReincarnationSql} WHERE Id = @id", new { id });
            return Ok(new BaseResponse<ReincarnationDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá công thức trùng sinh.</summary>
        [HttpDelete("/v1/gopet/api/Reincarnations/{id:int}")]
        public IActionResult DeleteReincarnation(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ReincarnationDto>($"{SelectReincarnationSql} WHERE Id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức trùng sinh", null));
            }

            conn.Execute("DELETE FROM `reincarnation` WHERE Id = @id", new { id });

            return Ok(new BaseResponse<ReincarnationDto?>(1, "Xoá công thức trùng sinh thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
