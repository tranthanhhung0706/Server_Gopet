using Gopet.Shared.Helper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using static Gopet.APIs.GopetApiExtentsion;

namespace Gopet.APIs
{
    /// <summary>
    /// Bắt buộc request gửi kèm header "Authorization: Bearer &lt;JWT&gt;" — JWT phải do
    /// AuthController.Login ký (JwtSecretKey), còn hạn, và claim "role" = 3 (Admin).
    /// Dùng SONG SONG với RequireApiKeyAttribute (X-Api-Key xác thực service gọi vào, còn
    /// Bearer xác thực đúng phiên đăng nhập Admin thật đang gọi) — bỏ 1 trong 2 đều không đủ.
    /// </summary>
    public class RequireAdminBearerAttribute : ActionFilterAttribute
    {
        private const string AdminRoleClaimValue = "3";

        /// <summary>Key trong HttpContext.Items chứa user_id (int) của admin đang gọi — action phía
        /// sau đọc được để tự chặn thao tác nguy hiểm lên chính tài khoản mình (vd. tự xoá).</summary>
        public const string CurrentAdminUserIdKey = "CurrentAdminUserId";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
                !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new UnauthorizedObjectResult(
                    CreateFailedRepository("Thiếu header 'Authorization: Bearer <token>'"));
                return;
            }

            string token = authHeader.ToString()["Bearer ".Length..].Trim();

            if (!JwtHelper.TryValidateToken(token, out var principal))
            {
                context.Result = new UnauthorizedObjectResult(
                    CreateFailedRepository("Token không hợp lệ hoặc đã hết hạn"));
                return;
            }

            string? role = principal.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
            if (role != AdminRoleClaimValue)
            {
                context.Result = new ObjectResult(CreateFailedRepository("Token không có quyền Admin"))
                {
                    StatusCode = 403,
                };
                return;
            }

            string? sub = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            if (int.TryParse(sub, out int adminUserId))
            {
                context.HttpContext.Items[CurrentAdminUserIdKey] = adminUserId;
            }

            base.OnActionExecuting(context);
        }
    }
}
