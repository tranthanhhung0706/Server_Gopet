using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý điểm mọc quái cố định (bảng `gopet_mob_location`, DB game gopettae_tae2) — dùng
    /// bởi GopetPlace.createNewMob() để quyết định quái/boss mọc ở đâu trên map. Sửa/thêm/xoá ở
    /// đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc khởi động
    /// (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp thêm
    /// GopetManager.ReloadMobLocation()) hoặc restart GServer để áp dụng.
    ///
    /// Bảng KHÔNG có cột khoá chính — chỉ (mapID, x, y) — nên sửa/xoá dùng chính 3 cột này làm
    /// khoá tự nhiên trên route, không bịa thêm cột id.
    ///
    /// Bảo mật giống UserController/NapMocController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/mob-location")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class MobLocationController : ControllerBase
    {
        private const string SelectMobLocationSql = @"SELECT mapID AS MapID, x AS X, y AS Y FROM `gopet_mob_location`";

        /// <summary>Danh sách điểm mọc quái — có phân trang, lọc theo mapId nếu truyền vào.</summary>
        [HttpGet("/v1/gopet/api/MobLocations")]
        public IActionResult GetMobLocations([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] int? mapId = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            string whereClause = mapId.HasValue ? "WHERE mapID = @mapId" : "";
            var parameters = new { mapId, limit, offset };

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `gopet_mob_location` {whereClause}", parameters);

            var locations = conn.Query<MobLocationDto>(
                $"{SelectMobLocationSql} {whereClause} ORDER BY mapID ASC, x ASC, y ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<MobLocationDto>(locations, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<MobLocationDto>>(1, "Thành công", paginated));
        }

        public record CreateMobLocationRequest(int MapID, int X, int Y);

        /// <summary>Tạo điểm mọc quái mới.</summary>
        [HttpPost("/v1/gopet/api/MobLocations")]
        public IActionResult CreateMobLocation([FromBody] CreateMobLocationRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.mapTemplate.ContainsKey(req.MapID))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {req.MapID}", null));
            }

            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLocationDto>(
                "SELECT mapID AS MapID, x AS X, y AS Y FROM `gopet_mob_location` WHERE mapID = @MapID AND x = @X AND y = @Y",
                req);
            if (existing != null)
            {
                return Conflict(new BaseResponse<object?>(0, "Điểm này đã tồn tại", null));
            }

            conn.Execute("INSERT INTO `gopet_mob_location` (mapID, x, y) VALUES (@MapID, @X, @Y)", req);

            return Ok(new BaseResponse<MobLocationDto>(1, "Tạo điểm mọc quái thành công", new MobLocationDto { MapID = req.MapID, X = req.X, Y = req.Y }));
        }

        public record UpdateMobLocationRequest(int MapID, int X, int Y);

        /// <summary>
        /// Sửa 1 điểm mọc quái — {mapId}/{x}/{y} trên route là toạ độ CŨ để tìm đúng dòng, body là
        /// giá trị MỚI muốn đổi thành (di chuyển điểm sang map/toạ độ khác).
        /// </summary>
        [HttpPatch("/v1/gopet/api/MobLocations/{mapId:int}/{x:int}/{y:int}")]
        public IActionResult UpdateMobLocation(int mapId, int x, int y, [FromBody] UpdateMobLocationRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.mapTemplate.ContainsKey(req.MapID))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy map id = {req.MapID}", null));
            }

            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLocationDto>(
                "SELECT mapID AS MapID, x AS X, y AS Y FROM `gopet_mob_location` WHERE mapID = @mapId AND x = @x AND y = @y",
                new { mapId, x, y });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy điểm mọc quái", null));
            }

            conn.Execute(
                "UPDATE `gopet_mob_location` SET mapID = @NewMapId, x = @NewX, y = @NewY WHERE mapID = @mapId AND x = @x AND y = @y LIMIT 1",
                new { NewMapId = req.MapID, NewX = req.X, NewY = req.Y, mapId, x, y });

            return Ok(new BaseResponse<MobLocationDto>(1, "Cập nhật thành công", new MobLocationDto { MapID = req.MapID, X = req.X, Y = req.Y }));
        }

        /// <summary>Xoá 1 điểm mọc quái theo toạ độ chính xác.</summary>
        [HttpDelete("/v1/gopet/api/MobLocations/{mapId:int}/{x:int}/{y:int}")]
        public IActionResult DeleteMobLocation(int mapId, int x, int y)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<MobLocationDto>(
                "SELECT mapID AS MapID, x AS X, y AS Y FROM `gopet_mob_location` WHERE mapID = @mapId AND x = @x AND y = @y",
                new { mapId, x, y });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy điểm mọc quái", null));
            }

            conn.Execute("DELETE FROM `gopet_mob_location` WHERE mapID = @mapId AND x = @x AND y = @y LIMIT 1", new { mapId, x, y });

            return Ok(new BaseResponse<MobLocationDto>(1, "Xoá điểm mọc quái thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
