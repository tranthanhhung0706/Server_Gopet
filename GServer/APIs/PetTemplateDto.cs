namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu đầy đủ của bảng `gopet_pet` dùng cho API — KHÔNG dùng lại class PetTemplate
    /// (Data/pet/PetTemplate.cs) vì class đó thiếu property hp/mp (chỉ có getHp()/getMp() tính
    /// công thức, Dapper không bind được cột hp/mp thật vào đó), setter private (không nhận
    /// [FromBody]), và tên field `_int` xấu cho public API — DTO này expose sạch là "Int".
    /// </summary>
    public sealed class PetTemplateDto
    {
        public int PetId { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public string? FrameImg { get; set; }
        public int FrameNum { get; set; }
        public int VY { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public int Str { get; set; }
        public int Int { get; set; }
        public int Agi { get; set; }
        public int Type { get; set; }
        public int Nclass { get; set; }
        public int Element { get; set; }
        public int GymUpLevel { get; set; }
        public int FusionScore { get; set; }
    }
}
