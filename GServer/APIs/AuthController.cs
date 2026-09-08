using Dapper;
using Gopet.Shared.Helper;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using static Gopet.APIs.GopetApiExtentsion;

namespace Gopet.APIs
{
    /// <summary>
    /// Đăng nhập cho trang quản trị web (buff_gopet). Không có endpoint register — tài khoản
    /// đã tồn tại sẵn trong bảng `user` (web_db, gopettae_gopet_web), tạo qua client game như cũ.
    /// Chỉ backend Next.js (BFF) được gọi endpoint này (bảo vệ bởi RequireApiKeyAttribute),
    /// browser không bao giờ gọi thẳng GServer.
    /// </summary>
    [Route("v1/gopet/api/auth")]
    [ApiController]
    [RequireApiKey]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class AuthController : ControllerBase
    {
        public record LoginRequest(string Username, string Password);

        /// <summary>
        /// Xác thực username/password theo bảng `user`, chỉ cho phép role = 3 (Admin, theo bảng
        /// user_duty) đăng nhập. Trả về JWT ký bởi JwtSecretKey để backend Next.js set httpOnly cookie.
        /// </summary>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Username) || string.IsNullOrWhiteSpace(req?.Password))
            {
                return BadRequest(CreateFailedRepository("Thiếu username hoặc password"));
            }

            using var conn = MYSQLManager.createWebMySqlConnection();
            UserData? user = conn.QueryFirstOrDefault<UserData>(
                "SELECT * FROM `user` WHERE `username` = @username LIMIT 1",
                new { username = req.Username.Trim() });

            if (user == null || !GopetHashHelper.VerifyHash(user.password, req.Password.Trim()))
            {
                return Unauthorized(CreateFailedRepository("Sai tài khoản hoặc mật khẩu"));
            }

            if (user.isBaned != 0)
            {
                return StatusCode(403, CreateFailedRepository($"Tài khoản đã bị khoá: {user.banReason}"));
            }

            if (user.role != 3)
            {
                return StatusCode(403, CreateFailedRepository("Tài khoản không có quyền truy cập trang quản trị"));
            }

            var (token, expiresAt) = JwtHelper.GenerateToken(user);

            return Ok(CreateOKRepository(new
            {
                token,
                expiresAt,
                user = new
                {
                    id = user.user_id,
                    username = user.username,
                    role = user.role,
                    email = user.email,
                },
            }));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
