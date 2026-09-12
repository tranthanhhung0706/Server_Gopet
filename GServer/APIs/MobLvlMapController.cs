using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý cấu hình cấp độ quái ngẫu nhiên theo map (bảng `gopet_map_moblvl`, DB game
    /// gopettae_tae2) — dùng bởi GopetPlace.createNewMob()/Mob.initMob() để quyết định map nào
    /// spawn loại quái/cấp độ nào. Sửa/thêm/xoá ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ
    /// nạp bảng này vào RAM lúc khởi động (GopetManager.init()), cần gọi
    /// POST /v1/gopet/api/server/reload-catalog (đã gộp thêm GopetManager.ReloadMobLvlMap()) hoặc
    /// restart GServer để áp dụng.
    ///
    /// Bảng KHÔNG có cột khoá chính riêng — khoá tự nhiên là cả 4 cột (mapID, petId, lvlFrom,
    /// lvlTo) — nên sửa/xoá dùng nguyên 4 cột này làm khoá trên route, không bịa thêm cột id.
    ///
    /// Bảo mật giống MobLocationController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/mob-lvl-map")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class MobLvlMapController : ControllerBase
    {
        private const string SelectMobLvlMapSql =
            @"SELECT mapID AS MapID, petId AS PetId, lvlFrom AS LvlFrom, lvlTo AS LvlTo
              FROM `gopet_map_moblvl`";

        private static bool IsValidRange(int lvlFrom, int lvlTo, out string? error)
        {
            if (lvlFrom > lvlTo)
            {
                error = "lvlFrom phải <= lvlTo";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Danh sách cấu hình cấp độ quái theo map — có phân trang, lọc theo mapId/petId.</summary>
        [HttpGet("/v1/gopet/api/MobLvlMaps")]
        public IActionResult GetMobLvlMaps([FromQuery] int page = 1, [FromQuery] int limit = 50,
            [FromQuery] int? mapId = null, [FromQuery] int? petId = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            var where = new System.Collections.Generic.List<string>();
            var parameters = new DynamicParameters();
            if (mapId.HasValue)
            {
                where.Add("mapID = @mapId");
                parameters.Add("mapId", mapId.Value);
            }
            if (petId.HasValue)
            {
                where.Add("petId = @petId");
                parameters.Add("petId", petId.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `gopet_map_moblvl` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var rows = conn.Query<MobLvlMapDto>(
                $"{SelectMobLvlMapSql} {whereSql} ORDER BY mapID ASC, petId ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<MobLvlMapDto>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<MobLvlMapDto>>(1, "Thành công", paginated));
        }

        public record CreateMobLvlMapRequest(int MapID, int PetId, int LvlFrom, int LvlTo);

        /// <summary>Thêm 1 dòng cấu hình cấp độ quái mới cho map.</summary>
        [HttpPost("/v1/gopet/api/MobLvlMaps")]
        public IActionResult CreateMobLvlMap([FromBody] CreateMobLvlMapRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.mapTemplate.ContainsKey(req.MapID))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {req.MapID}", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet id = {req.PetId}", null));
            }
            if (!IsValidRange(req.LvlFrom, req.LvlTo, out string? rangeError))
            {
                return BadRequest(new BaseResponse<object?>(0, rangeError, null));
            }

            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLvlMapDto>(
                "SELECT mapID AS MapID, petId AS PetId, lvlFrom AS LvlFrom, lvlTo AS LvlTo FROM `gopet_map_moblvl` WHERE mapID = @MapID AND petId = @PetId AND lvlFrom = @LvlFrom AND lvlTo = @LvlTo",
                req);
            if (existing != null)
            {
                return Conflict(new BaseResponse<object?>(0, "Dòng cấu hình này đã tồn tại", null));
            }

            conn.Execute("INSERT INTO `gopet_map_moblvl` (mapID, petId, lvlFrom, lvlTo) VALUES (@MapID, @PetId, @LvlFrom, @LvlTo)", req);

            return Ok(new BaseResponse<MobLvlMapDto>(1, "Tạo cấu hình cấp độ quái thành công",
                new MobLvlMapDto { MapID = req.MapID, PetId = req.PetId, LvlFrom = req.LvlFrom, LvlTo = req.LvlTo }));
        }

        public record UpdateMobLvlMapRequest(int MapID, int PetId, int LvlFrom, int LvlTo);

        /// <summary>
        /// Sửa 1 dòng cấu hình — {mapId}/{petId}/{lvlFrom}/{lvlTo} trên route là khoá CŨ để tìm
        /// đúng dòng, body là bộ giá trị MỚI muốn đổi thành.
        /// </summary>
        [HttpPatch("/v1/gopet/api/MobLvlMaps/{mapId:int}/{petId:int}/{lvlFrom:int}/{lvlTo:int}")]
        public IActionResult UpdateMobLvlMap(int mapId, int petId, int lvlFrom, int lvlTo, [FromBody] UpdateMobLvlMapRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.mapTemplate.ContainsKey(req.MapID))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {req.MapID}", null));
            }
            if (!GopetManager.PETTEMPLATE_HASH_MAP.ContainsKey(req.PetId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy pet id = {req.PetId}", null));
            }
            if (!IsValidRange(req.LvlFrom, req.LvlTo, out string? rangeError))
            {
                return BadRequest(new BaseResponse<object?>(0, rangeError, null));
            }

            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLvlMapDto>(
                "SELECT mapID AS MapID, petId AS PetId, lvlFrom AS LvlFrom, lvlTo AS LvlTo FROM `gopet_map_moblvl` WHERE mapID = @mapId AND petId = @petId AND lvlFrom = @lvlFrom AND lvlTo = @lvlTo",
                new { mapId, petId, lvlFrom, lvlTo });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy dòng cấu hình", null));
            }

            conn.Execute(
                @"UPDATE `gopet_map_moblvl`
                  SET mapID = @NewMapId, petId = @NewPetId, lvlFrom = @NewLvlFrom, lvlTo = @NewLvlTo
                  WHERE mapID = @mapId AND petId = @petId AND lvlFrom = @lvlFrom AND lvlTo = @lvlTo LIMIT 1",
                new { NewMapId = req.MapID, NewPetId = req.PetId, NewLvlFrom = req.LvlFrom, NewLvlTo = req.LvlTo, mapId, petId, lvlFrom, lvlTo });

            return Ok(new BaseResponse<MobLvlMapDto>(1, "Cập nhật thành công",
                new MobLvlMapDto { MapID = req.MapID, PetId = req.PetId, LvlFrom = req.LvlFrom, LvlTo = req.LvlTo }));
        }

        /// <summary>Xoá 1 dòng cấu hình cấp độ quái theo đúng khoá tự nhiên (mapId/petId/lvlFrom/lvlTo).</summary>
        [HttpDelete("/v1/gopet/api/MobLvlMaps/{mapId:int}/{petId:int}/{lvlFrom:int}/{lvlTo:int}")]
        public IActionResult DeleteMobLvlMap(int mapId, int petId, int lvlFrom, int lvlTo)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLvlMapDto>(
                "SELECT mapID AS MapID, petId AS PetId, lvlFrom AS LvlFrom, lvlTo AS LvlTo FROM `gopet_map_moblvl` WHERE mapID = @mapId AND petId = @petId AND lvlFrom = @lvlFrom AND lvlTo = @lvlTo",
                new { mapId, petId, lvlFrom, lvlTo });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy dòng cấu hình", null));
            }

            conn.Execute("DELETE FROM `gopet_map_moblvl` WHERE mapID = @mapId AND petId = @petId AND lvlFrom = @lvlFrom AND lvlTo = @lvlTo LIMIT 1",
                new { mapId, petId, lvlFrom, lvlTo });

            return Ok(new BaseResponse<MobLvlMapDto>(1, "Xoá cấu hình thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
