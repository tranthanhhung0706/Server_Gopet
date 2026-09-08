using System;

namespace Gopet.APIs
{
    /// <summary>
    /// Chi tiết đầy đủ 1 user — nhiều field hơn UserListItem (dùng cho list), nhưng vẫn KHÔNG
    /// có password/secretKey/otp: đó là bí mật xác thực, không phải "thông tin user", lộ ra API
    /// (dù chỉ cho Admin) là mở đường brute-force offline / bypass OTP nếu token/API-key rò rỉ.
    /// </summary>
    public sealed class UserDetail
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public int Role { get; set; }
        public int Coin { get; set; }
        public int TongNap { get; set; }
        public int IsBaned { get; set; }
        public long BanTime { get; set; }
        public string BanReason { get; set; } = "";
        public string? IpCreate { get; set; }
        public string? Avatar { get; set; }
        public string? TimeOnline { get; set; }
        public string? TimePost { get; set; }
        public string? TimeCmt { get; set; }
        public string? UpdateInfo { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
    }
}
