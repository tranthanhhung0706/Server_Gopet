namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu bảng `tattoo` (DB game gopettae_tae2) dùng cho API admin — itemOption/
    /// itemOptionValue để dạng string (JSON thô, giống GiftCodeDto.GiftData) để admin sửa trực
    /// tiếp, không auto-bind int[] như Data/pet/PetTattoTemplate.cs (class nội bộ GopetManager
    /// dùng lúc chạy game — có type handler JsonAdapter tự parse).
    ///
    /// itemOption/itemOptionValue: hiệu ứng combat đặc biệt (phản đòn/hút máu/định thân...), cùng
    /// định dạng ItemInfo dùng cho cánh (xem Item.ExtractBattleOptions()) — mỗi hiệu ứng là 1 nhóm
    /// 4 phần tử liên tiếp trong itemOption bắt đầu bằng giá trị 13 (OPTION_BATTLE), 4 giá trị
    /// tương ứng trong itemOptionValue là [effectTypeId, turn, percentValue*100, isActiveOnWearer
    /// (1/0)]. Vd xăm "hoả kì lân (hút máu)": itemOption=[13,14,15,16], itemOptionValue=
    /// [24,99999,2500,1] (24=RECOVERY_HP, turn=99999≈vĩnh viễn, 2500=25%, áp cho người mang).
    /// Không có hiệu ứng đặc biệt thì để "[]".
    /// </summary>
    public sealed class TattooDto
    {
        public int TattooId { get; set; }
        public string Name { get; set; } = "";
        public sbyte Type { get; set; }
        public string IconPath { get; set; } = "";
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public float Percent { get; set; }
        public string ItemOption { get; set; } = "[]";
        public string ItemOptionValue { get; set; } = "[]";
    }
}
