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
    /// Quản lý công thức chế tạo hộp quà Halloween 2026 (bảng `halloween_recipe`) — xem
    /// Halloween2026.CraftGiftBox (đoạn NPC chế tạo) để biết chính xác cách dùng: mỗi dòng ứng với 1
    /// loại hộp (BoxItemId), số lượng 4 nguyên liệu cần + Ngọc/Vàng cần trừ khi chế tạo. Khoá chính
    /// RecipeId tự tăng, BoxItemId là duy nhất (UNIQUE) vì mỗi hộp chỉ có 1 công thức.
    ///
    /// Sửa ở đây KHÔNG áp dụng ngay cho gameplay — GServer chỉ nạp bảng này vào RAM lúc khởi động
    /// (GopetManager.init()), cần gọi POST /v1/gopet/api/server/reload-catalog (đã gộp thêm
    /// GopetManager.ReloadHalloweenRecipe()) hoặc restart GServer để áp dụng — giống DropItemController.
    ///
    /// Bảo mật giống các controller khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/halloween-recipe")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class HalloweenRecipeController : ControllerBase
    {
        private const string SelectRecipeSql =
            @"SELECT recipeId AS RecipeId, boxItemId AS BoxItemId, name AS Name,
                     pumpkinCount AS PumpkinCount, candyCount AS CandyCount, boneCount AS BoneCount,
                     monsterEyeCount AS MonsterEyeCount,
                     coinCost AS CoinCost, goldCost AS GoldCost
              FROM `halloween_recipe`";

        /// <summary>Danh sách công thức chế tạo (luôn ít, không cần phân trang thật sự nhưng vẫn giữ tham số cho đồng bộ).</summary>
        [HttpGet("/v1/gopet/api/HalloweenRecipes")]
        public IActionResult GetHalloweenRecipes([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 500);
            int offset = (page - 1) * limit;

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `halloween_recipe`");

            var rows = conn.Query<HalloweenRecipe>(
                $"{SelectRecipeSql} ORDER BY boxItemId ASC LIMIT @limit OFFSET @offset",
                new { limit, offset }).ToList();

            var paginated = new PaginatedData<HalloweenRecipe>(rows, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<HalloweenRecipe>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 công thức theo recipeId.</summary>
        [HttpGet("/v1/gopet/api/HalloweenRecipes/{id:int}")]
        public IActionResult GetHalloweenRecipeById(int id)
        {
            using var conn = MYSQLManager.create();

            var row = conn.QueryFirstOrDefault<HalloweenRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (row == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            return Ok(new BaseResponse<HalloweenRecipe>(1, "Thành công", row));
        }

        public record CreateHalloweenRecipeRequest(int BoxItemId, string Name, int PumpkinCount = 0, int CandyCount = 0,
            int BoneCount = 0, int MonsterEyeCount = 0, long CoinCost = 0, long GoldCost = 0);

        /// <summary>Tạo công thức mới. recipeId tự tăng, boxItemId phải là item có thật và chưa có công thức.</summary>
        [HttpPost("/v1/gopet/api/HalloweenRecipes")]
        public IActionResult CreateHalloweenRecipe([FromBody] CreateHalloweenRecipeRequest req)
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

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `halloween_recipe` WHERE boxItemId = @BoxItemId", req);
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "Item này đã có công thức chế tạo", null));
            }

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `halloween_recipe`
                    (boxItemId, name, pumpkinCount, candyCount, boneCount, monsterEyeCount, coinCost, goldCost)
                  VALUES
                    (@BoxItemId, @Name, @PumpkinCount, @CandyCount, @BoneCount, @MonsterEyeCount, @CoinCost, @GoldCost);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<HalloweenRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id = newId });
            return Ok(new BaseResponse<HalloweenRecipe?>(1, "Tạo công thức thành công", created));
        }

        public record UpdateHalloweenRecipeRequest(string? Name, int? PumpkinCount, int? CandyCount, int? BoneCount,
            int? MonsterEyeCount, long? CoinCost, long? GoldCost);

        /// <summary>Cập nhật 1 phần công thức. Không cho đổi recipeId/boxItemId (khoá).</summary>
        [HttpPatch("/v1/gopet/api/HalloweenRecipes/{id:int}")]
        public IActionResult UpdateHalloweenRecipe(int id, [FromBody] UpdateHalloweenRecipeRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<HalloweenRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name is string name) { setClauses.Add("name = @name"); parameters.Add("name", name); }
            if (req?.PumpkinCount is int pumpkinCount) { setClauses.Add("pumpkinCount = @pumpkinCount"); parameters.Add("pumpkinCount", pumpkinCount); }
            if (req?.CandyCount is int candyCount) { setClauses.Add("candyCount = @candyCount"); parameters.Add("candyCount", candyCount); }
            if (req?.BoneCount is int boneCount) { setClauses.Add("boneCount = @boneCount"); parameters.Add("boneCount", boneCount); }
            if (req?.MonsterEyeCount is int monsterEyeCount) { setClauses.Add("monsterEyeCount = @monsterEyeCount"); parameters.Add("monsterEyeCount", monsterEyeCount); }
            if (req?.CoinCost is long coinCost) { setClauses.Add("coinCost = @coinCost"); parameters.Add("coinCost", coinCost); }
            if (req?.GoldCost is long goldCost) { setClauses.Add("goldCost = @goldCost"); parameters.Add("goldCost", goldCost); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `halloween_recipe` SET {string.Join(", ", setClauses)} WHERE recipeId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<HalloweenRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            return Ok(new BaseResponse<HalloweenRecipe?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá công thức.</summary>
        [HttpDelete("/v1/gopet/api/HalloweenRecipes/{id:int}")]
        public IActionResult DeleteHalloweenRecipe(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<HalloweenRecipe>($"{SelectRecipeSql} WHERE recipeId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy công thức", null));
            }

            conn.Execute("DELETE FROM `halloween_recipe` WHERE recipeId = @id", new { id });

            return Ok(new BaseResponse<HalloweenRecipe?>(1, "Xoá công thức thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
