using Dapper;
using Gopet.Shared.Helper;
using Gopet.Util;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý tài khoản web (bảng `user`, web_db gopettae_gopet_web). Bảo mật giống
    /// ServerController: [RequireApiKey] (backend Next.js gọi vào) + [RequireAdminBearer]
    /// (JWT của admin đang đăng nhập).
    /// </summary>
    [Route("v1/gopet/api/user")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class UserController : ControllerBase
    {
        // Hình chiếu an toàn dùng chung cho GET/POST/PATCH — không select password/secretKey/otp.
        private const string SelectUserListItemSql =
            @"SELECT user_id AS Id, username AS Username, email AS Email, role AS Role,
                     coin AS Coin, isBaned AS IsBaned, create_date AS CreateDate
              FROM `user`";

        /// <summary>
        /// Danh sách tài khoản trong bảng `user` — có phân trang, tìm theo username, lọc theo role.
        /// </summary>
        [HttpGet("/v1/gopet/api/Users")]
        public IActionResult GetUsers([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] string? search = null, [FromQuery] int? role = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("username LIKE @search");
                parameters.Add("search", $"%{search.Trim()}%");
            }
            if (role.HasValue)
            {
                where.Add("role = @role");
                parameters.Add("role", role.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.createWebMySqlConnection();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `user` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var users = conn.Query<UserListItem>(
                $"{SelectUserListItemSql} {whereSql} ORDER BY user_id DESC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<UserListItem>(users, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<UserListItem>>(1, "Thành công", paginated));
        }

        /// <summary>
        /// Chi tiết đầy đủ 1 user theo user_id — nhiều field hơn GetUsers (list): thêm phone,
        /// tongnap, banTime/banReason, ipv4Create, avatar, time_online/post/cmt, updateinfo,
        /// update_date. Vẫn không trả password/secretKey/otp (xem UserDetail.cs).
        /// KHÔNG select cột `isOnline` của DB — cột đó GServer không hề ghi (luôn = 0, xem
        /// GetUserOnlineStatus bên dưới để biết trạng thái online THẬT).
        /// </summary>
        [HttpGet("/v1/gopet/api/Users/{id:int}")]
        public IActionResult GetUserById(int id)
        {
            using var conn = MYSQLManager.createWebMySqlConnection();

            var user = conn.QueryFirstOrDefault<UserDetail>(
                @"SELECT user_id AS Id, username AS Username, email AS Email, phone AS Phone, role AS Role,
                         coin AS Coin, tongnap AS TongNap, isBaned AS IsBaned, banTime AS BanTime, banReason AS BanReason,
                         ipv4Create AS IpCreate, avatar AS Avatar,
                         time_online AS TimeOnline, time_post AS TimePost, time_cmt AS TimeCmt, updateinfo AS UpdateInfo,
                         create_date AS CreateDate, update_date AS UpdateDate
                  FROM `user` WHERE user_id = @id",
                new { id });

            if (user == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy user", null));
            }

            return Ok(new BaseResponse<UserDetail>(1, "Thành công", user));
        }

        /// <summary>
        /// Trạng thái online THẬT của user — tra trực tiếp PlayerManager.players (in-memory, cập
        /// nhật ngay lúc login/disconnect ở Player.cs), KHÔNG dùng cột `user.isOnline` (DB, chết,
        /// GServer không bao giờ ghi). 1 user có thể có nhiều player (nhân vật) — chỉ cần 1 nhân
        /// vật đang kết nối là coi user đó online.
        /// </summary>
        [HttpGet("/v1/gopet/api/Users/{id:int}/online")]
        public IActionResult GetUserOnlineStatus(int id)
        {
            bool isOnline = PlayerManager.players.Any(p => p?.user != null && p.user.user_id == id);
            return Ok(new BaseResponse<bool>(1, "Thành công", isOnline));
        }

        public record GetUsersByIdsRequest(List<int> Ids);

        /// <summary>
        /// Lấy nhiều user cùng lúc theo danh sách user_id — vd để hiển thị tên user từ
        /// gift_code.usersOfUseThis (mảng user_id) thành username thay vì để trần số id.
        /// Dùng POST (không GET) vì danh sách id có thể dài, tránh giới hạn độ dài query string.
        /// Response bọc PaginatedData giống hệt GetUsers để Next.js dùng chung 1 type — page luôn
        /// là 1 (không phân trang thật), limit = số id được yêu cầu, total = số user tìm thấy.
        /// </summary>
        [HttpPost("/v1/gopet/api/Users/batch")]
        public IActionResult GetUsersByIds([FromBody] GetUsersByIdsRequest? req)
        {
            if (req?.Ids == null || req.Ids.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu danh sách ids", null));
            }
            if (req.Ids.Count > 200)
            {
                return BadRequest(new BaseResponse<object?>(0, "Tối đa 200 id mỗi lần gọi", null));
            }

            using var conn = MYSQLManager.createWebMySqlConnection();

            var users = conn.Query<UserListItem>(
                $"{SelectUserListItemSql} WHERE user_id IN @ids",
                new { ids = req.Ids }).ToList();

            var paginated = new PaginatedData<UserListItem>(users, users.Count, 1, req.Ids.Count);
            return Ok(new BaseResponse<PaginatedData<UserListItem>>(1, "Thành công", paginated));
        }

        public record CreateUserRequest(string Username, string Password, string? Email, int Role = 1);

        /// <summary>
        /// Tạo tài khoản mới trong bảng `user` — dùng cho admin tạo tài khoản trực tiếp
        /// (khác doRegister trong Player.cs vốn dành cho client game tự đăng ký, nên không áp
        /// dụng danh sách BANNAME ở đó; admin có thể chủ động đặt role bất kỳ, kể cả tạo Admin khác).
        /// </summary>
        [HttpPost("/v1/gopet/api/Users")]
        public IActionResult CreateUser([FromBody] CreateUserRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Username) || string.IsNullOrWhiteSpace(req?.Password))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu username hoặc password", null));
            }

            string username = req.Username.Trim();
            string password = req.Password.Trim();

            if (!Regex.IsMatch(username, "^[a-z0-9]+$"))
            {
                return BadRequest(new BaseResponse<object?>(0, "Username chỉ được chứa chữ thường và số", null));
            }
            if (username.Length < 6 || username.Length >= 25)
            {
                return BadRequest(new BaseResponse<object?>(0, "Username phải từ 6-24 ký tự", null));
            }
            if (password.Length < 6 || password.Length >= 60)
            {
                return BadRequest(new BaseResponse<object?>(0, "Password phải từ 6-59 ký tự", null));
            }

            using var conn = MYSQLManager.createWebMySqlConnection();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `user` WHERE username = @username", new { username });
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "Username đã tồn tại", null));
            }

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `user` (username, password, email, role, ipv4Create, dayCreate)
                  VALUES (@username, @password, @email, @role, @ipv4Create, @dayCreate);
                  SELECT LAST_INSERT_ID();",
                new
                {
                    username,
                    password = GopetHashHelper.ComputeHash(password),
                    email = req.Email?.Trim() ?? "",
                    role = req.Role,
                    ipv4Create = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                    dayCreate = Utilities.CurrentTimeMillis,
                });

            var created = conn.QueryFirstOrDefault<UserListItem>(
                $"{SelectUserListItemSql} WHERE user_id = @id", new { id = newId });

            return Ok(new BaseResponse<UserListItem?>(1, "Tạo tài khoản thành công", created));
        }

        public record UpdateUserRequest(string? Email, int? Role, int? Coin, int? IsBaned,
            string? BanReason, long? BanTime, string? Password);

        /// <summary>
        /// Cập nhật 1 phần thông tin user (chỉ field nào có trong body mới bị đổi) — email, role,
        /// coin, trạng thái ban, hoặc reset password. Không cho đổi username (gắn với danh tính
        /// tài khoản game, đổi được sẽ vỡ dữ liệu liên quan).
        /// </summary>
        [HttpPatch("/v1/gopet/api/Users/{id:int}")]
        public IActionResult UpdateUser(int id, [FromBody] UpdateUserRequest? req)
        {
            using var conn = MYSQLManager.createWebMySqlConnection();

            var existing = conn.QueryFirstOrDefault<UserListItem>($"{SelectUserListItemSql} WHERE user_id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy user", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Email != null)
            {
                setClauses.Add("email = @email");
                parameters.Add("email", req.Email.Trim());
            }
            if (req?.Role is int role)
            {
                setClauses.Add("role = @role");
                parameters.Add("role", role);
            }
            if (req?.Coin is int coin)
            {
                setClauses.Add("coin = @coin");
                parameters.Add("coin", coin);
            }
            if (req?.IsBaned is int isBaned)
            {
                setClauses.Add("isBaned = @isBaned");
                parameters.Add("isBaned", isBaned);
            }
            if (req?.BanReason != null)
            {
                setClauses.Add("banReason = @banReason");
                parameters.Add("banReason", req.BanReason);
            }
            if (req?.BanTime is long banTime)
            {
                setClauses.Add("banTime = @banTime");
                parameters.Add("banTime", banTime);
            }
            if (!string.IsNullOrWhiteSpace(req?.Password))
            {
                string password = req.Password.Trim();
                if (password.Length < 6 || password.Length >= 60)
                {
                    return BadRequest(new BaseResponse<object?>(0, "Password phải từ 6-59 ký tự", null));
                }
                setClauses.Add("password = @password");
                parameters.Add("password", GopetHashHelper.ComputeHash(password));
            }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `user` SET {string.Join(", ", setClauses)} WHERE user_id = @id", parameters);

            var updated = conn.QueryFirstOrDefault<UserListItem>($"{SelectUserListItemSql} WHERE user_id = @id", new { id });
            return Ok(new BaseResponse<UserListItem?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>
        /// Xoá cứng user khỏi bảng `user`. Bảng dùng engine MyISAM nên không có FK ràng buộc —
        /// xoá sẽ KHÔNG đụng tới dữ liệu liên quan ở bảng khác (bank_trans, login_history,
        /// posts...), các bản ghi đó thành "mồ côi" chứ không tự mất theo. Không cho tự xoá
        /// chính tài khoản admin đang đăng nhập (tránh tự khoá quyền truy cập của chính mình).
        /// </summary>
        [HttpDelete("/v1/gopet/api/Users/{id:int}")]
        public IActionResult DeleteUser(int id)
        {
            if (HttpContext.Items[RequireAdminBearerAttribute.CurrentAdminUserIdKey] is int currentAdminId
                && currentAdminId == id)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không thể tự xoá tài khoản đang đăng nhập", null));
            }

            using var conn = MYSQLManager.createWebMySqlConnection();

            var existing = conn.QueryFirstOrDefault<UserListItem>($"{SelectUserListItemSql} WHERE user_id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy user", null));
            }

            conn.Execute("DELETE FROM `user` WHERE user_id = @id", new { id });

            return Ok(new BaseResponse<UserListItem?>(1, "Xoá tài khoản thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
