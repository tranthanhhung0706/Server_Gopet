namespace Gopet.APIs
{
    /// <summary>
    /// 1 dòng = 1 địa chỉ IP đã từng đăng nhập vào tài khoản (gộp từ bảng login_history, DB web).
    /// </summary>
    public sealed class LoginIpDto
    {
        public string IpAddress { get; set; } = "";
        /// <summary>Tổng số lần thử đăng nhập từ IP này (cả thành công lẫn thất bại).</summary>
        public int TotalCount { get; set; }
        /// <summary>Số lần đăng nhập thành công.</summary>
        public int SuccessCount { get; set; }
        /// <summary>Số lần trong đó là đăng nhập qua web (không phải game).</summary>
        public int WebCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
