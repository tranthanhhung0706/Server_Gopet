namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `gopet_map_moblvl` (DB game gopettae_tae2) — cấu hình cấp độ quái ngẫu
    /// nhiên theo từng map (xem GopetPlace.createNewMob() chọn ngẫu nhiên 1 dòng khớp map, rồi
    /// Mob.initMob() random cấp trong [LvlFrom, LvlTo]). Bảng KHÔNG có cột khoá chính riêng — khoá
    /// tự nhiên là cả 4 cột (mapID, petId, lvlFrom, lvlTo) — nên API này dùng nguyên 4 cột đó để
    /// sửa/xoá, không bịa thêm cột id (tránh ALTER TABLE bảng đang chạy thật).
    /// </summary>
    public sealed class MobLvlMapDto
    {
        public int MapID { get; set; }
        public int PetId { get; set; }
        public int LvlFrom { get; set; }
        public int LvlTo { get; set; }
    }
}
