using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace Gopet.APIs
{
    /// <summary>
    /// Helper dùng chung cho mọi controller có upload ảnh (GopetController — icon/frameImg pet,
    /// ItemController — iconPath/frameImgPath item, có thể mở rộng thêm sau này). Lưu bản chính
    /// vào thư mục source assets/ (bền, không mất khi dotnet clean/publish lại — xem
    /// Gopet.csproj CopyToOutputDirectory PreserveNewest chỉ copy 1 chiều source -> output),
    /// đồng thời copy thêm 1 bản vào build-output assets/ (nơi HttpServer.cs UseStaticFiles đang
    /// serve /assets/...) để xem được ngay, không cần đợi build lại đồng bộ.
    /// </summary>
    public static class AssetUploadHelper
    {
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp",
        };

        public const long MaxImageUploadBytes = 5 * 1024 * 1024;

        public record UploadAssetResponse(string Path);

        private static string? _cachedSourceAssetsDirectory;

        /// <summary>
        /// Thư mục assets THẬT trong source (SRCGOPETGOC/GServer/assets) — khác với
        /// Directory.GetCurrentDirectory()/assets lúc chạy (chỉ là bản copy ở build output). Ưu
        /// tiên đọc App.config key "AssetsSourceDirectory" nếu deploy có set sẵn; không có thì tự
        /// dò ngược thư mục cha tìm Gopet.csproj (đánh dấu thư mục project) — dò không ra (vd môi
        /// trường publish thật không có source) thì fallback về thư mục build output như cũ, còn
        /// hơn lỗi.
        /// </summary>
        private static string GetSourceAssetsDirectory()
        {
            if (_cachedSourceAssetsDirectory != null)
            {
                return _cachedSourceAssetsDirectory;
            }

            string? configured = ConfigurationManager.AppSettings["AssetsSourceDirectory"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return _cachedSourceAssetsDirectory = configured;
            }

            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (dir.GetFiles("Gopet.csproj").Length > 0)
                {
                    return _cachedSourceAssetsDirectory = Path.Combine(dir.FullName, "assets");
                }
                dir = dir.Parent;
            }

            return _cachedSourceAssetsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "assets");
        }

        /// <summary>
        /// Nhận file ảnh upload từ admin, lưu vào assets/{folder}/{tên file random}, trả về
        /// BaseResponse&lt;UploadAssetResponse&gt; sẵn dùng làm return value cho action. Tên file
        /// luôn random (GUID) — KHÔNG dùng tên gốc của file để tránh path traversal và tránh ghi
        /// đè ảnh người khác đang dùng.
        /// </summary>
        public static async Task<IActionResult> SaveUploadedImage(IFormFile? file, string folder)
        {
            if (file == null || file.Length == 0)
            {
                return new BadRequestObjectResult(new BaseResponse<object?>(0, "Thiếu file", null));
            }
            if (file.Length > MaxImageUploadBytes)
            {
                return new BadRequestObjectResult(new BaseResponse<object?>(0, "File tối đa 5MB", null));
            }

            string ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext))
            {
                return new BadRequestObjectResult(new BaseResponse<object?>(0,
                    "Chỉ chấp nhận ảnh (.png, .jpg, .jpeg, .gif, .webp)", null));
            }

            string fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";

            string sourceDir = Path.Combine(GetSourceAssetsDirectory(), folder);
            Directory.CreateDirectory(sourceDir);
            string sourcePath = Path.Combine(sourceDir, fileName);

            await using (var stream = System.IO.File.Create(sourcePath))
            {
                await file.CopyToAsync(stream);
            }

            try
            {
                string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "assets", folder);
                string outputPath = Path.Combine(outputDir, fileName);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(outputDir);
                    System.IO.File.Copy(sourcePath, outputPath, overwrite: true);
                }
            }
            catch
            {
                // Best-effort — bản source đã lưu thành công là đủ, bỏ qua lỗi copy sang output.
            }

            string relativePath = $"{folder}/{fileName}";
            return new OkObjectResult(new BaseResponse<UploadAssetResponse>(1, "Upload thành công", new UploadAssetResponse(relativePath)));
        }
    }
}
