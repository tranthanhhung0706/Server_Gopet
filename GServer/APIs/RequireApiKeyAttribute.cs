using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Configuration;

namespace Gopet.APIs
{
    /// <summary>
    /// Bắt buộc request phải gửi kèm header "X-Api-Key" đúng với giá trị cấu hình trong App.config
    /// (appSettings key "ApiSecretKey") thì mới cho chạy action. Nếu chưa cấu hình key thì chặn hết
    /// (fail-closed) thay vì mặc định mở, tránh quên cấu hình rồi để API bị lộ.
    /// </summary>
    public class RequireApiKeyAttribute : ActionFilterAttribute
    {
        private const string HeaderName = "X-Api-Key";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            string configuredKey = ConfigurationManager.AppSettings["ApiSecretKey"];
            if (string.IsNullOrEmpty(configuredKey))
            {
                context.Result = new ObjectResult("API chưa được cấu hình ApiSecretKey trong App.config, tạm thời bị khóa.")
                {
                    StatusCode = 503
                };
                return;
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedKey) || providedKey != configuredKey)
            {
                context.Result = new UnauthorizedObjectResult("Thiếu hoặc sai API key. Vui lòng gửi kèm header 'X-Api-Key'.");
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
