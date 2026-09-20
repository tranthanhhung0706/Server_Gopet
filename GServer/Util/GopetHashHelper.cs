

using System.Security.Cryptography;
using System.Text;

namespace Gopet.Shared.Helper
{
    public class GopetHashHelper
    {
        public static string ComputeHash(string Text)
        {
            return BCrypt.Net.BCrypt.HashPassword(Text, 12);
        }

        public static bool VerifyHash(string Hash, string Text)
        {
            return BCrypt.Net.BCrypt.Verify(Text, Hash);
        }

        // Hash bcrypt (cùng cost 12) của 1 mật khẩu không ai dùng — đăng nhập vào username KHÔNG TỒN TẠI
        // vẫn chạy VerifyHash với hash này để tốn thời gian y hệt username có thật. Nếu bỏ qua bước
        // bcrypt khi username không có, thời gian phản hồi nhanh hơn hẳn (vài trăm ms) nên đo thời gian
        // là dò ra được username nào có thật.
        private static readonly Lazy<string> dummyHash = new(() => ComputeHash("gopet-dummy-password-not-used"));

        public static void BurnVerifyTime(string Text)
        {
            BCrypt.Net.BCrypt.Verify(Text ?? "", dummyHash.Value);
        }
    }
}