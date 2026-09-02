using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý nhân vật (bảng `player`, DB game gopettae_tae2 — cùng DB với GopetController).
    /// 1 tài khoản `user` (web_db) có thể có nhiều `player` (nhân vật) qua `user_id`.
    ///
    /// KHÔNG có endpoint tạo mới (POST) — khác Users/GopetPets/GiftCodes. Lý do: nhân vật cần
    /// nhiều cột JSON hợp lệ (items/pets/skin/wing...) để client game load được, tạo tay qua SQL
    /// insert rất dễ ra bản ghi "hỏng" làm crash lúc load. Việc tạo nhân vật nên đi qua đúng luồng
    /// tạo nhân vật thật của game client.
    ///
    /// UPDATE chỉ cho sửa field số/tên đơn giản (gold, coin, lua, star, clanId, isAdmin, các loại
    /// điểm...) — KHÔNG cho sửa items/pets/skin/wing/achievements... (JSON lồng nhiều tầng, sửa
    /// sai định dạng dễ hỏng save của người chơi). Xem PlayerDetail.cs.
    ///
    /// ⚠️ Player đang ONLINE: sửa/xoá qua API này không đồng bộ với phiên đang chạy trong RAM
    /// server — server có thể ghi đè lại DB theo dữ liệu cũ trong RAM ở lần autosave tiếp theo,
    /// khiến thay đổi qua API "biến mất". Chỉ nên dùng khi chắc chắn player đó đang offline.
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/player")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class PlayerController : ControllerBase
    {
        private const string SelectPlayerListItemSql =
            @"SELECT ID AS Id, user_id AS UserId, name AS Name, gender AS Gender, gold AS Gold,
                     coin AS Coin, lua AS Lua, star AS Star, clanId AS ClanId, isAdmin AS IsAdmin,
                     loginDate AS LoginDate, LastTimeOnline AS LastTimeOnline
              FROM `player`";

        /// <summary>Danh sách player — có phân trang, tìm theo tên, lọc theo user_id/clanId.</summary>
        [HttpGet("/v1/gopet/api/Players")]
        public IActionResult GetPlayers([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] string? search = null, [FromQuery] int? userId = null, [FromQuery] int? clanId = null)
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
            if (userId.HasValue)
            {
                where.Add("user_id = @userId");
                parameters.Add("userId", userId.Value);
            }
            if (clanId.HasValue)
            {
                where.Add("clanId = @clanId");
                parameters.Add("clanId", clanId.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `player` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var players = conn.Query<PlayerListItem>(
                $"{SelectPlayerListItemSql} {whereSql} ORDER BY ID DESC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<PlayerListItem>(players, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<PlayerListItem>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 player theo ID (khác user_id — 1 user có thể có nhiều player).</summary>
        [HttpGet("/v1/gopet/api/Players/{id:int}")]
        public IActionResult GetPlayerById(int id)
        {
            using var conn = MYSQLManager.create();

            var player = conn.QueryFirstOrDefault<PlayerDetail>(
                @"SELECT ID AS Id, user_id AS UserId, name AS Name, gender AS Gender, gold AS Gold,
                         spendGold AS SpendGold, coin AS Coin, lua AS Lua, star AS Star, clanId AS ClanId,
                         isAdmin AS IsAdmin, isOnSky AS IsOnSky, isFirstFree AS IsFirstFree, avatarPath AS AvatarPath,
                         AccumulatedPoint AS AccumulatedPoint, ArenaPoint AS ArenaPoint, EventPoint AS EventPoint,
                         KioskFund AS KioskFund, pkPoint AS PkPoint, CurrentAchievementId AS CurrentAchievementId,
                         loginDate AS LoginDate, LastTimeOnline AS LastTimeOnline,
                         items AS ItemsJson, pets AS PetsJson, petSelected AS PetSelectedJson,
                         skin AS SkinJson, wing AS WingJson, achievements AS AchievementsJson
                  FROM `player` WHERE ID = @id",
                new { id });

            if (player == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy player", null));
            }

            return Ok(new BaseResponse<PlayerDetail>(1, "Thành công", player));
        }

        public record UpdatePlayerRequest(string? Name, int? Gender, long? Gold, long? Coin, long? Lua,
            int? Star, int? ClanId, bool? IsAdmin, int? AccumulatedPoint, int? ArenaPoint,
            int? EventPoint, int? KioskFund, int? PkPoint);

        /// <summary>
        /// Cập nhật 1 phần player — CHỈ field số/tên đơn giản. Không nhận items/pets/skin/wing...
        /// qua endpoint này (xem class doc phía trên).
        /// </summary>
        [HttpPatch("/v1/gopet/api/Players/{id:int}")]
        public IActionResult UpdatePlayer(int id, [FromBody] UpdatePlayerRequest? req)
        {
            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `player` WHERE ID = @id", new { id });
            if (existing == 0)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy player", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Gender is int gender) { setClauses.Add("gender = @gender"); parameters.Add("gender", gender); }
            if (req?.Gold is long gold) { setClauses.Add("gold = @gold"); parameters.Add("gold", gold); }
            if (req?.Coin is long coin) { setClauses.Add("coin = @coin"); parameters.Add("coin", coin); }
            if (req?.Lua is long lua) { setClauses.Add("lua = @lua"); parameters.Add("lua", lua); }
            if (req?.Star is int star) { setClauses.Add("star = @star"); parameters.Add("star", star); }
            if (req?.ClanId is int clanId) { setClauses.Add("clanId = @clanId"); parameters.Add("clanId", clanId); }
            if (req?.IsAdmin is bool isAdmin) { setClauses.Add("isAdmin = @isAdmin"); parameters.Add("isAdmin", isAdmin); }
            if (req?.AccumulatedPoint is int accPoint) { setClauses.Add("AccumulatedPoint = @accPoint"); parameters.Add("accPoint", accPoint); }
            if (req?.ArenaPoint is int arenaPoint) { setClauses.Add("ArenaPoint = @arenaPoint"); parameters.Add("arenaPoint", arenaPoint); }
            if (req?.EventPoint is int eventPoint) { setClauses.Add("EventPoint = @eventPoint"); parameters.Add("eventPoint", eventPoint); }
            if (req?.KioskFund is int kioskFund) { setClauses.Add("KioskFund = @kioskFund"); parameters.Add("kioskFund", kioskFund); }
            if (req?.PkPoint is int pkPoint) { setClauses.Add("pkPoint = @pkPoint"); parameters.Add("pkPoint", pkPoint); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `player` SET {string.Join(", ", setClauses)} WHERE ID = @id", parameters);

            var updated = conn.QueryFirstOrDefault<PlayerListItem>($"{SelectPlayerListItemSql} WHERE ID = @id", new { id });
            return Ok(new BaseResponse<PlayerListItem?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá nhân vật. Không có FK nào ràng buộc bảng `player` nên xoá luôn thành công (không bắt lỗi FK).</summary>
        [HttpDelete("/v1/gopet/api/Players/{id:int}")]
        public IActionResult DeletePlayer(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<PlayerListItem>($"{SelectPlayerListItemSql} WHERE ID = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy player", null));
            }

            conn.Execute("DELETE FROM `player` WHERE ID = @id", new { id });

            return Ok(new BaseResponse<PlayerListItem?>(1, "Xoá player thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
