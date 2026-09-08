using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
[assembly: ApiController]
namespace Gopet.APIs
{
    
    public class HttpServer
    {
        private WebApplication Application { get;  }


        public int Port { get; }

        public HttpServer(int port) : this()
        {
            Port = port;
        }

        private const string CorsPolicyName = "GopetWebCors";

        protected HttpServer()
        {
            var builder = WebApplication.CreateBuilder(Array.Empty<string>());

            builder.Services.AddControllers();


            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Gopet API", Description = "Documention of GopetServer", Version = "v1" });
            });

            // Cho phép trang quản trị (buff_gopet, chạy trên domain riêng) gọi thẳng GServer từ
            // trình duyệt. Origin lấy từ App.config key "CorsAllowedOrigins" (phân tách bằng dấu
            // phẩy), mặc định "http://localhost:3000" nếu chưa cấu hình.
            string[] allowedOrigins = (ConfigurationManager.AppSettings["CorsAllowedOrigins"] ?? "http://localhost:3000")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicyName, policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });

            Application = builder.Build();


            Application.UseHttpsRedirection();

            Application.UseCors(CorsPolicyName);

            // Serve trực tiếp thư mục assets (icon/frame pet, ảnh item...) qua HTTP tại
            // /assets/... — vd GET /assets/icons/1.icon.png, /assets/petFrame3/1.png. Đây là
            // ảnh game công khai (client nào chơi cũng thấy), không phải dữ liệu nhạy cảm nên
            // KHÔNG bắt RequireApiKey/RequireAdminBearer như các API còn lại — ai có URL ảnh
            // (lấy từ field icon/frameImg trong API pet) đều xem được, không cần đăng nhập.
            string assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "assets");
            Application.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(assetsPath),
                RequestPath = "/assets",
            });

            Application.UseAuthorization();

            Application.MapControllers();

            Application.UseRouting();

            Application.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            Application.UseSwagger();

            Application.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "GopetServer API V1");
                c.RoutePrefix = "api/Gopet";
            });
        }

        public void Start()
        {
            Application.RunAsync($"http://0.0.0.0:{this.Port}");
        }

        public void Stop()
        {
            Application.StopAsync();
        }
    }
}
