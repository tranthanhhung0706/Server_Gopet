using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý item template/danh mục vật phẩm (bảng `item`, DB game gopettae_tae2 — KHÁC với
    /// gopettae_gopet_web mà UserController dùng). Đây là bảng "danh mục" vật phẩm (base stats,
    /// tên, icon...), KHÔNG phải vật phẩm của từng người chơi (cái đó lưu JSON trong
    /// player.items, không có bảng riêng — xem PlayerData.cs/Item.cs).
    ///
    /// Khác gopet_pet: `item` không có FK ràng buộc đi ra (element/petNClass/gender chỉ là số,
    /// không tham chiếu bảng danh mục nào) nên create/update không cần bắt lỗi FK 1452. Nhưng bị
    /// `hidden_stat` tham chiếu ngược lại itemId (IdArmour/IdGlove/IdHat/IdShoe/IdWeapon) nên
    /// delete vẫn cần bắt lỗi FK 1451.
    ///
    /// Bảo mật giống UserController: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/item")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ItemController : ControllerBase
    {
        private const string SelectItemTemplateSql =
            @"SELECT itemId AS ItemId, name AS Name, description AS Description, type AS Type,
                     iconPath AS IconPath, frameImgPath AS FrameImgPath, gender AS Gender, isStackable AS IsStackable,
                     itemOption AS ItemOption, itemOptionValue AS ItemOptionValue,
                     atkRange AS AtkRange, defRange AS DefRange, hpRange AS HpRange, mpRange AS MpRange,
                     requireStr AS RequireStr, requireInt AS RequireInt, requireAgi AS RequireAgi,
                     expire AS Expire, isOnSky AS IsOnSky, canTrade AS CanTrade,
                     petNClass AS PetNClass, element AS Element, price AS Price, wingFrameNum AS WingFrameNum
              FROM `item`";

        private const string AssetFolder = "items";

        /// <summary>
        /// Thư mục asset theo item type — soi trực tiếp dữ liệu thật trong DB (group by type,
        /// lấy prefix thư mục của iconPath/frameImgPath) thay vì đoán: đa số type dùng chung
        /// "items/", chỉ 4 type có convention riêng (số liệu đa số/thiểu số, phần thiểu số là dữ
        /// liệu nhập không đồng nhất, admin vẫn override tay được — xem NormalizeAssetPath):
        /// - SKIN_ITEM (4): anim_characters/ (84/94 item)
        /// - WING_ITEM (5): anim_wings/ (34/34 item, không có ngoại lệ)
        /// - ITEM_PART_PET (9): icons/ (141/149 item)
        /// - ITEM_THẺ_KỸ_NĂNG (27): skills/ (6/8 item)
        /// Các type còn lại (equip, gem, buff...) đều dùng "items/".
        /// </summary>
        private static string GetAssetFolderForType(int type) => type switch
        {
            GopetManager.SKIN_ITEM => "anim_characters",
            GopetManager.WING_ITEM => "anim_wings",
            GopetManager.ITEM_PART_PET => "icons",
            GopetManager.ITEM_THẺ_KỸ_NĂNG => "skills",
            _ => AssetFolder,
        };

        /// <summary>
        /// Tự thêm prefix thư mục asset đúng convention hiện có trong DB theo item type (xem
        /// GetAssetFolderForType) nếu admin chỉ gõ tên file trần. Nếu giá trị đã có sẵn prefix
        /// thư mục (chứa "/") thì giữ nguyên, không suy đoán lại theo type — tránh lặp/prefix sai
        /// khi value đã được normalize từ trước, và vẫn cho admin override tay các item ngoại lệ.
        /// </summary>
        private static string? NormalizeAssetPath(string? value, int type)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string trimmed = value.Trim();
            return trimmed.Contains('/') ? trimmed : $"{GetAssetFolderForType(type)}/{trimmed}";
        }

        /// <summary>
        /// Upload ảnh icon item — trả về path để điền vào field IconPath. `type` do client gửi
        /// kèm (giá trị đang chọn trong form) để chọn đúng thư mục lưu — xem GetAssetFolderForType.
        /// </summary>
        [HttpPost("/v1/gopet/api/Items/upload/icon")]
        [RequestSizeLimit(AssetUploadHelper.MaxImageUploadBytes)]
        public Task<IActionResult> UploadIcon(IFormFile? file, [FromForm] int type = 0)
        {
            return AssetUploadHelper.SaveUploadedImage(file, GetAssetFolderForType(type));
        }

        /// <summary>Upload ảnh frame item — trả về path để điền vào field FrameImgPath.</summary>
        [HttpPost("/v1/gopet/api/Items/upload/frame")]
        [RequestSizeLimit(AssetUploadHelper.MaxImageUploadBytes)]
        public Task<IActionResult> UploadFrameImg(IFormFile? file, [FromForm] int type = 0)
        {
            return AssetUploadHelper.SaveUploadedImage(file, GetAssetFolderForType(type));
        }

        /// <summary>
        /// Danh sách item template — có phân trang, tìm theo tên, lọc theo type.
        /// </summary>
        [HttpGet("/v1/gopet/api/Items")]
        public IActionResult GetItems([FromQuery] int page = 1, [FromQuery] int limit = 20,
            [FromQuery] string? search = null, [FromQuery] int? type = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            int offset = (page - 1) * limit;

            var where = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("name LIKE @search");
                parameters.Add("search", $"%{search.Trim()}%");
            }
            if (type.HasValue)
            {
                where.Add("type = @type");
                parameters.Add("type", type.Value);
            }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            using var conn = MYSQLManager.create();

            int total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM `item` {whereSql}", parameters);

            parameters.Add("limit", limit);
            parameters.Add("offset", offset);
            var items = conn.Query<ItemTemplateDto>(
                $"{SelectItemTemplateSql} {whereSql} ORDER BY itemId ASC LIMIT @limit OFFSET @offset",
                parameters).ToList();

            var paginated = new PaginatedData<ItemTemplateDto>(items, total, page, limit);
            return Ok(new BaseResponse<PaginatedData<ItemTemplateDto>>(1, "Thành công", paginated));
        }

        /// <summary>Chi tiết 1 item template theo itemId.</summary>
        [HttpGet("/v1/gopet/api/Items/{id:int}")]
        public IActionResult GetItemById(int id)
        {
            using var conn = MYSQLManager.create();

            var item = conn.QueryFirstOrDefault<ItemTemplateDto>($"{SelectItemTemplateSql} WHERE itemId = @id", new { id });
            if (item == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy item", null));
            }

            return Ok(new BaseResponse<ItemTemplateDto>(1, "Thành công", item));
        }

        public record CreateItemRequest(
            int ItemId,
            string Name,
            string? Description,
            int Type = 0,
            string? IconPath = null,
            string? FrameImgPath = null,
            sbyte Gender = -1,
            bool IsStackable = true,
            int[]? ItemOption = null,
            int[]? ItemOptionValue = null,
            int[]? AtkRange = null,
            int[]? DefRange = null,
            int[]? HpRange = null,
            int[]? MpRange = null,
            int RequireStr = 0,
            int RequireInt = 0,
            int RequireAgi = 0,
            long? Expire = null,
            bool IsOnSky = false,
            bool CanTrade = true,
            sbyte PetNClass = -1,
            sbyte Element = -1,
            int Price = 10,
            sbyte WingFrameNum = 2);

        /// <summary>Tạo item template mới. itemId do admin chỉ định (không auto-increment, giống dữ liệu gốc).</summary>
        [HttpPost("/v1/gopet/api/Items")]
        public IActionResult CreateItem([FromBody] CreateItemRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu name", null));
            }

            using var conn = MYSQLManager.create();

            int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `item` WHERE itemId = @ItemId", req);
            if (existing > 0)
            {
                return Conflict(new BaseResponse<object?>(0, "itemId đã tồn tại", null));
            }

            var insertParams = new
            {
                req.ItemId,
                req.Name,
                Description = req.Description ?? "",
                req.Type,
                IconPath = NormalizeAssetPath(req.IconPath, req.Type),
                FrameImgPath = NormalizeAssetPath(req.FrameImgPath, req.Type),
                req.Gender,
                req.IsStackable,
                req.ItemOption,
                req.ItemOptionValue,
                req.AtkRange,
                req.DefRange,
                req.HpRange,
                req.MpRange,
                req.RequireStr,
                req.RequireInt,
                req.RequireAgi,
                req.Expire,
                req.IsOnSky,
                req.CanTrade,
                req.PetNClass,
                req.Element,
                req.Price,
                req.WingFrameNum,
            };

            conn.Execute(
                @"INSERT INTO `item`
                    (itemId, name, description, type, iconPath, frameImgPath, gender, isStackable,
                     itemOption, itemOptionValue, atkRange, defRange, hpRange, mpRange,
                     requireStr, requireInt, requireAgi, expire, isOnSky, canTrade, petNClass, element, price, wingFrameNum)
                  VALUES
                    (@ItemId, @Name, @Description, @Type, @IconPath, @FrameImgPath, @Gender, @IsStackable,
                     @ItemOption, @ItemOptionValue, @AtkRange, @DefRange, @HpRange, @MpRange,
                     @RequireStr, @RequireInt, @RequireAgi, @Expire, @IsOnSky, @CanTrade, @PetNClass, @Element, @Price, @WingFrameNum)",
                insertParams);

            var created = conn.QueryFirstOrDefault<ItemTemplateDto>($"{SelectItemTemplateSql} WHERE itemId = @ItemId", req);
            return Ok(new BaseResponse<ItemTemplateDto?>(1, "Tạo item thành công", created));
        }

        public record UpdateItemRequest(string? Name, string? Description, int? Type, string? IconPath,
            string? FrameImgPath, sbyte? Gender, bool? IsStackable, int[]? ItemOption, int[]? ItemOptionValue,
            int[]? AtkRange, int[]? DefRange, int[]? HpRange, int[]? MpRange, int? RequireStr, int? RequireInt,
            int? RequireAgi, long? Expire, bool? IsOnSky, bool? CanTrade, sbyte? PetNClass, sbyte? Element, int? Price,
            sbyte? WingFrameNum);

        /// <summary>Cập nhật 1 phần item template. Không cho đổi itemId (khoá chính, bị hidden_stat tham chiếu).</summary>
        [HttpPatch("/v1/gopet/api/Items/{id:int}")]
        public IActionResult UpdateItem(int id, [FromBody] UpdateItemRequest? req)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ItemTemplateDto>($"{SelectItemTemplateSql} WHERE itemId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy item", null));
            }

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("id", id);

            if (req?.Name != null) { setClauses.Add("name = @name"); parameters.Add("name", req.Name); }
            if (req?.Description != null) { setClauses.Add("description = @description"); parameters.Add("description", req.Description); }
            if (req?.Type is int type) { setClauses.Add("type = @type"); parameters.Add("type", type); }
            int effectiveType = req?.Type ?? existing.Type;
            if (req?.IconPath != null) { setClauses.Add("iconPath = @iconPath"); parameters.Add("iconPath", NormalizeAssetPath(req.IconPath, effectiveType)); }
            if (req?.FrameImgPath != null) { setClauses.Add("frameImgPath = @frameImgPath"); parameters.Add("frameImgPath", NormalizeAssetPath(req.FrameImgPath, effectiveType)); }
            if (req?.Gender is sbyte gender) { setClauses.Add("gender = @gender"); parameters.Add("gender", gender); }
            if (req?.IsStackable is bool isStackable) { setClauses.Add("isStackable = @isStackable"); parameters.Add("isStackable", isStackable); }
            if (req?.ItemOption != null) { setClauses.Add("itemOption = @itemOption"); parameters.Add("itemOption", req.ItemOption); }
            if (req?.ItemOptionValue != null) { setClauses.Add("itemOptionValue = @itemOptionValue"); parameters.Add("itemOptionValue", req.ItemOptionValue); }
            if (req?.AtkRange != null) { setClauses.Add("atkRange = @atkRange"); parameters.Add("atkRange", req.AtkRange); }
            if (req?.DefRange != null) { setClauses.Add("defRange = @defRange"); parameters.Add("defRange", req.DefRange); }
            if (req?.HpRange != null) { setClauses.Add("hpRange = @hpRange"); parameters.Add("hpRange", req.HpRange); }
            if (req?.MpRange != null) { setClauses.Add("mpRange = @mpRange"); parameters.Add("mpRange", req.MpRange); }
            if (req?.RequireStr is int requireStr) { setClauses.Add("requireStr = @requireStr"); parameters.Add("requireStr", requireStr); }
            if (req?.RequireInt is int requireInt) { setClauses.Add("requireInt = @requireInt"); parameters.Add("requireInt", requireInt); }
            if (req?.RequireAgi is int requireAgi) { setClauses.Add("requireAgi = @requireAgi"); parameters.Add("requireAgi", requireAgi); }
            if (req?.Expire is long expire) { setClauses.Add("expire = @expire"); parameters.Add("expire", expire); }
            if (req?.IsOnSky is bool isOnSky) { setClauses.Add("isOnSky = @isOnSky"); parameters.Add("isOnSky", isOnSky); }
            if (req?.CanTrade is bool canTrade) { setClauses.Add("canTrade = @canTrade"); parameters.Add("canTrade", canTrade); }
            if (req?.PetNClass is sbyte petNClass) { setClauses.Add("petNClass = @petNClass"); parameters.Add("petNClass", petNClass); }
            if (req?.Element is sbyte element) { setClauses.Add("element = @element"); parameters.Add("element", element); }
            if (req?.Price is int price) { setClauses.Add("price = @price"); parameters.Add("price", price); }
            if (req?.WingFrameNum is sbyte wingFrameNum) { setClauses.Add("wingFrameNum = @wingFrameNum"); parameters.Add("wingFrameNum", wingFrameNum); }

            if (setClauses.Count == 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Không có trường nào để cập nhật", null));
            }

            conn.Execute($"UPDATE `item` SET {string.Join(", ", setClauses)} WHERE itemId = @id", parameters);

            var updated = conn.QueryFirstOrDefault<ItemTemplateDto>($"{SelectItemTemplateSql} WHERE itemId = @id", new { id });
            return Ok(new BaseResponse<ItemTemplateDto?>(1, "Cập nhật thành công", updated));
        }

        /// <summary>Xoá item template. Thất bại nếu đang bị `hidden_stat` tham chiếu (FK).</summary>
        [HttpDelete("/v1/gopet/api/Items/{id:int}")]
        public IActionResult DeleteItem(int id)
        {
            using var conn = MYSQLManager.create();

            var existing = conn.QueryFirstOrDefault<ItemTemplateDto>($"{SelectItemTemplateSql} WHERE itemId = @id", new { id });
            if (existing == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy item", null));
            }

            try
            {
                conn.Execute("DELETE FROM `item` WHERE itemId = @id", new { id });
            }
            catch (MySqlException ex) when (ex.Number == 1451)
            {
                return Conflict(new BaseResponse<object?>(0, "Không thể xoá — item này đang được tham chiếu bởi Hidden Stat", null));
            }

            return Ok(new BaseResponse<ItemTemplateDto?>(1, "Xoá item thành công", existing));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
