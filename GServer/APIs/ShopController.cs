using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý danh mục shop (bảng `shop`, DB game gopettae_tae2). Đây là bảng cấu hình các món
    /// hàng bán trong các shop tĩnh (vũ khí, giáp, skin, đấu trường, gian thương...) — xem
    /// MenuController.cs SHOP_* để biết ShopId nào ứng với shop nào. KHÔNG áp dụng cho shop Clan
    /// (ShopId=10) — shop đó dựng động trong code (ShopClan.cs), không đọc từ bảng này.
    ///
    /// Khác gopet_pet/item: `shop` không bị bảng nào khác tham chiếu ngược (không cần bắt lỗi FK
    /// khi xoá). `isSellItem` có FK tới bảng lookup `type_boolean` nhưng giá trị C# bool tự nhiên
    /// đã đúng miền 0/1 nên không cần bắt lỗi FK riêng.
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/shop")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ShopController : ControllerBase
    {
        private const string SelectShopSql =
            @"SELECT id AS Id, ShopId AS ShopId, inventoryType AS InventoryType,
                     itemTemTempleId AS ItemTemTempleId, petId AS PetId, count AS Count,
                     isSellItem AS IsSellItem, moneyType AS MoneyType, price AS Price,
                     clanLvl AS ClanLvl, perCount AS PerCount
              FROM `shop`";

        /// <summary>
        /// Kiểm tra MoneyType/Price hợp lệ: cả 2 đều phải có ít nhất 1 phần tử và CÙNG độ dài
        /// (mỗi cặp cùng index tạo thành 1 lựa chọn thanh toán — xem ShopTemplateItemDto).
        /// </summary>
        private static bool IsValidPayment(sbyte[]? moneyType, int[]? price, out string? error)
        {
            if (moneyType == null || moneyType.Length == 0 || price == null || price.Length == 0)
            {
                error = "Cần ít nhất 1 cặp moneyType/price";
                return false;
            }
            if (moneyType.Length != price.Length)
            {
                error = "moneyType và price phải có cùng độ dài (ghép theo index)";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Danh sách shop item — có phân trang, lọc theo ShopId, lọc theo isSellItem (bán item
        /// hay bán pet).
        /// </summary>
        [HttpGet("/v1/gopet/api/Shops")]
        public IActionResult GetShops([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] int? shopId = null, [FromQuery] bool? isSellItem = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();

            if (shopId.HasValue)
            {
                where.Add("ShopId = @shopId");
                parameters.Add("shopId", shopId.Value);
            }
            if (isSellItem.HasValue)
            {
                where.Add("isSellItem = @isSellItem");
                parameters.Add("isSellItem", isSellItem.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `shop` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var shops = conn.Query<ShopTemplateItemDto>(
                $"{SelectShopSql} {whereSql} ORDER BY id ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<ShopTemplateItemDto>(shops, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<ShopTemplateItemDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 shop item theo id.</summary>
        [HttpGet("/v1/gopet/api/Shops/{id:int}")]
        public IActionResult GetShopById(int id)
        {
            using var conn = MYSQLManager.create();

            var shop = conn.QueryFirstOrDefault<ShopTemplateItemDto>($"{SelectShopSql} WHERE id = @id", new { id });
            if (shop == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy shop item", null));
            }

            return Ok(new BaseResponse<ShopTemplateItemDto>(1, "Thành công", shop));
        }

        public record CreateShopRequest(
            int ShopId,
            int? ItemTemTempleId,
            int? PetId,
            int Count = 1,
            bool IsSellItem = true,
            sbyte[]? MoneyType = null,
            int[]? Price = null,
            int ClanLvl = 0,
            int PerCount = 0,
            sbyte InventoryType = 0);

        /// <summary>
        /// Tạo shop item mới. id tự tăng (khác petId/itemId của Pet/Item — admin không tự chỉ
        /// định). Bắt buộc chỉ định đúng itemTemTempleId (nếu IsSellItem) hoặc petId (nếu không).
        /// </summary>
        [HttpPost("/v1/gopet/api/Shops")]
        public IActionResult CreateShop([FromBody] CreateShopRequest req)
        {
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (!IsValidPayment(req.MoneyType, req.Price, out string? paymentError))
            {
                return BadRequest(new BaseResponse<object?>(0, paymentError, null));
            }
            if (req.IsSellItem && req.ItemTemTempleId == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu itemTemTempleId (đang bán item)", null));
            }
            if (!req.IsSellItem && req.PetId == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu petId (đang bán pet)", null));
            }

            using var conn = MYSQLManager.create();

            int newId = conn.ExecuteScalar<int>(
                @"INSERT INTO `shop`
                    (ShopId, inventoryType, itemTemTempleId, petId, count, isSellItem, moneyType, price, clanLvl, perCount)
                  VALUES
                    (@ShopId, @InventoryType, @ItemTemTempleId, @PetId, @Count, @IsSellItem, @MoneyType, @Price, @ClanLvl, @PerCount);
                  SELECT LAST_INSERT_ID();",
                req);

            var created = conn.QueryFirstOrDefault<ShopTemplateItemDto>($"{SelectShopSql} WHERE id = @id", new { id = newId });
            return Ok(new BaseResponse<ShopTemplateItemDto?>(1, "Tạo shop item thành công", created));
        }

        public record UpdateShopRequest(int? ShopId, sbyte? InventoryType, int? ItemTemTempleId, int? PetId,
            int? Count, bool? IsSellItem, sbyte[]? MoneyType, int[]? Price, int? ClanLvl, int? PerCount);

        /// <summary>Cập nhật 1 phần shop item. Không cho đổi id (khoá chính).</summary>
        [HttpPatch("/v1/gopet/api/Shops/{id:int}")]
        public IActionResult UpdateShop(int id, [FromBody] UpdateShopRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ShopTemplateItemDto>($"{SelectShopSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy shop item", null));
            }

            if ((req?.MoneyType != null || req?.Price != null))
            {
                sbyte[] effectiveMoneyType = req.MoneyType ?? existing.MoneyType ?? Array.Empty<sbyte>();
                int[] effectivePrice = req.Price ?? existing.Price ?? Array.Empty<int>();
                if (!IsValidPayment(effectiveMoneyType, effectivePrice, out string? paymentError))
                {
                    return BadRequest(new BaseResponse<object?>(0, paymentError, null));
                }
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.ShopId is int shopId) { setClauses.Add("ShopId = @shopId"); parameters.Add("shopId", shopId); }
            if (req?.InventoryType is sbyte inventoryType) { setClauses.Add("inventoryType = @inventoryType"); parameters.Add("inventoryType", inventoryType); }
            if (req?.ItemTemTempleId is int itemTemTempleId) { setClauses.Add("itemTemTempleId = @itemTemTempleId"); parameters.Add("itemTemTempleId", itemTemTempleId); }
            if (req?.PetId is int petId) { setClauses.Add("petId = @petId"); parameters.Add("petId", petId); }
            if (req?.Count is int count) { setClauses.Add("count = @count"); parameters.Add("count", count); }
            if (req?.IsSellItem is bool isSellItem) { setClauses.Add("isSellItem = @isSellItem"); parameters.Add("isSellItem", isSellItem); }
            if (req?.MoneyType != null) { setClauses.Add("moneyType = @moneyType"); parameters.Add("moneyType", req.MoneyType); }
            if (req?.Price != null) { setClauses.Add("price = @price"); parameters.Add("price", req.Price); }
            if (req?.ClanLvl is int clanLvl) { setClauses.Add("clanLvl = @clanLvl"); parameters.Add("clanLvl", clanLvl); }
            if (req?.PerCount is int perCount) { setClauses.Add("perCount = @perCount"); parameters.Add("perCount", perCount); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `shop` SET {string.Join(", ", setClauses)} WHERE id = @id", parameters);

            var updated = conn.QueryFirstOrDefault<ShopTemplateItemDto>($"{SelectShopSql} WHERE id = @id", new { id });
            return Ok(new BaseResponse<ShopTemplateItemDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá shop item. Không có bảng nào khác tham chiếu ngược nên không cần bắt lỗi FK.</summary>
        [HttpDelete("/v1/gopet/api/Shops/{id:int}")]
        public IActionResult DeleteShop(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ShopTemplateItemDto>($"{SelectShopSql} WHERE id = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy shop item", null));
            }

            conn.Execute("DELETE FROM `shop` WHERE id = @id", new { id });

            return Ok(new BaseResponse<ShopTemplateItemDto?>(1, "Xoá shop item thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
