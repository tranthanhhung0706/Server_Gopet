using Dapper;
using Gopet.Data.Event.Year2026;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý công thức chế tạo hộp quà Trung Thu 2026 (bảng `trung_thu_recipe`) — xem
    /// TrungThu2026.CraftGiftBox (đoạn NPC chế tạo) để biết chính xác cách dùng: mỗi dòng ứng với 1
    /// loại hộp (BoxItemId), số lượng 5 nguyên liệu cần + Ngọc/Vàng cần trừ khi chế tạo. Khoá chính
    /// RecipeId tự tăng, BoxItemId là duy nhất (UNIQUE) vì mỗi hộp chỉ có 1 công thức.
    ///
    /// Sửa ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc khởi động
    /// (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp thêm
    /// GopetManager.ReloadTrungThuRecipe()) hoặc restart GServer để áp dụng — giống DropItemController.
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/trung-thu-recipe")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class TrungThuRecipeController : ControllerBase
    {
        private const string SelectRecipeSql =
            @"SELECT recipeId AS RecipeId, boxItemId AS BoxItemId, name AS Name,
                     flourCount AS FlourCount, eggCount AS EggCount, mungBeanCount AS MungBeanCount,
                     lotusSeedCount AS LotusSeedCount, moonCakeCount AS MoonCakeCount,
                     coinCost AS CoinCost, goldCost AS GoldCost
              FROM `trung_thu_recipe`";

        /// <summary>Danh sách công thức chế tạo (luôn ít, không cần phân trang thật sự nhưng vẫn giữ tham số cho đồng bộ).</summary>
        [HttpGet("/v1/gopet/api/TrungThuRecipes")]
        public IActionResult GetTrungThuRecipes([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `trung_thu_recipe`");

            var rows = conn.Query<TrungThuRecipe>(
                $"{SelectRecipeSql} ORDER BY boxItemId ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<TrungThuRecipe>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<TrungThuRecipe>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 công thức theo recipeId.</summary>
        [HttpGet("/v1/gopet/api/TrungThuRecipes/{id:int}")]
        public IActionResult GetTrungThuRecipeById(int id)
        {
            using var conn = MYSQLManager.create();

            var row = conn.QueryFirstOrDefault<TrungThuRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (row == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            return Ok(new BaseResponse<TrungThuRecipe>(1, "Thành công", row));
        }

        public record CreateTrungThuRecipeRequest(int BoxItemId, string Name, int FlourCount = 0, int EggCount = 0,
            int MungBeanCount = 0, int LotusSeedCount = 0, int MoonCakeCount = 0, long CoinCost = 0, long GoldCost = 0);

        /// <summary>Tạo công thức mới. recipeId tự tăng, boxItemId phải là item có thật và chưa có công thức.</summary>
        [HttpPost("/v1/gopet/api/TrungThuRecipes")]
        public IActionResult CreateTrungThuRecipe([FromBody] CreateTrungThuRecipeRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!GopetManager.itemTemplate.ContainsKey(req.BoxItemId))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Không tìm thấy item id = {req.BoxItemId}", null));
            }

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `trung_thu_recipe` WHERE boxItemId = @BoxItemId", req);
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "Item này đã có công thức chế tạo", null));
            }

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `trung_thu_recipe`
                    (boxItemId, name, flourCount, eggCount, mungBeanCount, lotusSeedCount, moonCakeCount, coinCost, goldCost)
                  VALUES
                    (@BoxItemId, @Name, @FlourCount, @EggCount, @MungBeanCount, @LotusSeedCount, @MoonCakeCount, @CoinCost, @GoldCost);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<TrungThuRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id = newId });
            return Ok(new BaseResponse<TrungThuRecipe?>(1, "Tạo công thức thành công", created));
        }

        public record UpdateTrungThuRecipeRequest(string? Name, int? FlourCount, int? EggCount, int? MungBeanCount,
            int? LotusSeedCount, int? MoonCakeCount, long? CoinCost, long? GoldCost);

        /// <summary>Cập nhật 1 phần công thức. Không cho đổi recipeId/boxItemId (khoá).</summary>
        [HttpPatch("/v1/gopet/api/TrungThuRecipes/{id:int}")]
        public IActionResult UpdateTrungThuRecipe(int id, [FromBody] UpdateTrungThuRecipeRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TrungThuRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name is string name) { setClauses.Add("name = @name"); parameters.Add("name", name); }
            if (req?.FlourCount is int flourCount) { setClauses.Add("flourCount = @flourCount"); parameters.Add("flourCount", flourCount); }
            if (req?.EggCount is int eggCount) { setClauses.Add("eggCount = @eggCount"); parameters.Add("eggCount", eggCount); }
            if (req?.MungBeanCount is int mungBeanCount) { setClauses.Add("mungBeanCount = @mungBeanCount"); parameters.Add("mungBeanCount", mungBeanCount); }
            if (req?.LotusSeedCount is int lotusSeedCount) { setClauses.Add("lotusSeedCount = @lotusSeedCount"); parameters.Add("lotusSeedCount", lotusSeedCount); }
            if (req?.MoonCakeCount is int moonCakeCount) { setClauses.Add("moonCakeCount = @moonCakeCount"); parameters.Add("moonCakeCount", moonCakeCount); }
            if (req?.CoinCost is long coinCost) { setClauses.Add("coinCost = @coinCost"); parameters.Add("coinCost", coinCost); }
            if (req?.GoldCost is long goldCost) { setClauses.Add("goldCost = @goldCost"); parameters.Add("goldCost", goldCost); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `trung_thu_recipe` SET {string.Join(", ", setClauses)} WHERE recipeId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<TrungThuRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            return Ok(new BaseResponse<TrungThuRecipe?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá công thức.</summary>
        [HttpDelete("/v1/gopet/api/TrungThuRecipes/{id:int}")]
        public IActionResult DeleteTrungThuRecipe(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<TrungThuRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            conn.Execute("DELETE FROM `trung_thu_recipe` WHERE recipeId = @id", new { id });

            return Ok(new BaseResponse<TrungThuRecipe?>(1, "Xoá công thức thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
