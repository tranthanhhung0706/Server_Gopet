using System;

namespace Gopet.APIs
{
    /// <summary>
    /// Hình chiếu an toàn của bảng `user` để trả ra ngoài API — KHÔNG bao gồm password/secretKey/otp.
    /// </summary>
    public sealed class UserListItem
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public int Role { get; set; }
        public int Coin { get; set; }
        public int IsBaned { get; set; }
        public DateTime CreateDate { get; set; }
    }
}
