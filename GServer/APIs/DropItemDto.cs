namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `drop_item` (DB game gopettae_tae2) — cấu hình vật phẩm rớt ra khi quái chết
    /// trên từng map. Xem PetBattle.cs (đoạn xử lý quái chết): random chọn 1 dòng bất kỳ trong danh
    /// sách cấu hình của map đó (đồng xác suất giữa các dòng, KHÔNG theo Percent), rồi roll số 0-100
    /// so với Percent của đúng dòng đã chọn — qua thì mới thực sự rớt Count cái ItemTemplateId đó.
    /// LvlRange (vd [0,50], null = không giới hạn) lọc theo cấp quái vừa chết trước khi chọn dòng.
    /// Khoá chính DropId tự tăng (auto-increment).
    /// </summary>
    public sealed class DropItemDto
    {
        public int DropId { get; set; }
        public int MapId { get; set; }
        public int ItemTemplateId { get; set; }
        public float Percent { get; set; } = 10;
        public int[]? LvlRange { get; set; }
        public int Count { get; set; } = 1;
    }
}
