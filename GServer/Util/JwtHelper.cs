using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace Gopet.Shared.Helper
{
    /// <summary>
    /// Ký JWT cho phiên đăng nhập trang quản trị (web admin). Secret cấu hình trong App.config
    /// key "JwtSecretKey" (fail nếu thiếu, giống cách RequireApiKeyAttribute fail-closed với ApiSecretKey).
    /// </summary>
    public static class JwtHelper
    {
        public static (string token, DateTime expiresAt) GenerateToken(UserData user)
        {
            string secret = ConfigurationManager.AppSettings["JwtSecretKey"]
                ?? throw new InvalidOperationException("JwtSecretKey chưa được cấu hình trong App.config");
            int expireMinutes = int.TryParse(ConfigurationManager.AppSettings["JwtExpireMinutes"], out var m) ? m : 120;

            var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.user_id.ToString()),
                new Claim("username", user.username),
                new Claim("role", user.role.ToString()),
                new Claim("email", user.email ?? string.Empty),
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: expiresAt,
                signingCredentials: creds);

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }

        /// <summary>
        /// Verify chữ ký + hạn của JWT (dùng cho header "Authorization: Bearer &lt;token&gt;").
        /// Trả về ClaimsPrincipal nếu hợp lệ, null nếu sai chữ ký/hết hạn/malformed.
        /// </summary>
        public static bool TryValidateToken(string token, [NotNullWhen(true)] out ClaimsPrincipal? principal)
        {
            string secret = ConfigurationManager.AppSettings["JwtSecretKey"]
                ?? throw new InvalidOperationException("JwtSecretKey chưa được cấu hình trong App.config");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            // Mặc định JwtSecurityTokenHandler tự remap tên claim ngắn ("role", "sub"...) sang
            // URI dài (ClaimTypes.Role...) khi tạo ClaimsPrincipal — để trống map này thì claim
            // giữ nguyên tên như lúc GenerateToken ký ra ("role"), tránh check `c.Type == "role"`
            // ở nơi gọi luôn ra null dù token hợp lệ.
            var handler = new JwtSecurityTokenHandler
            {
                InboundClaimTypeMap = new Dictionary<string, string>(),
            };

            try
            {
                principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                }, out _);
                return true;
            }
            catch
            {
                principal = null;
                return false;
            }
        }
    }
}
