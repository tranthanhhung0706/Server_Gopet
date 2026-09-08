namespace REGServer.Networking;

/// <summary>
/// Cài đặt lại thuật toán TEA y hệt Server/IO/TEA.cs của GServer (bit-for-bit),
/// chỉ đổi kiểu dữ liệu sbyte[] (kiểu Java) sang byte[] cho gọn — kết quả trên wire không đổi
/// vì thuật toán gốc luôn mask "&amp; 255" trước khi dùng nên không phụ thuộc dấu của byte.
/// KHÔNG được sửa phần brew/unbrew/pack/unpack nếu muốn giữ tương thích với client cũ.
/// </summary>
public sealed class Tea
{
    private const int Delta = unchecked((int)0x9E3779B9);

    private readonly int[] _s = new int[4];

    public Tea(long key)
    {
        byte[] expanded = new byte[16];
        ExpandKey(key, expanded);

        int off = 0;
        for (int i = 0; i < 4; i++)
        {
            _s[i] = (expanded[off++] & 255)
                  | (expanded[off++] & 255) << 8
                  | (expanded[off++] & 255) << 16
                  | (expanded[off++] & 255) << 24;
        }
    }

    /// <summary>
    /// Suy ra khoá long từ 9 byte đầu tiên client gửi khi bắt tay (giống Session.readKey() cũ).
    /// Byte đầu tiên (index 0) không được dùng, y hệt bản gốc.
    /// </summary>
    public static long DeriveHandshakeKey(byte[] nineBytes)
    {
        long time = 0;
        for (int i = 1; i <= 7; i++)
        {
            time ^= nineBytes[i] & 0xFFL;
            time <<= 8;
        }
        time ^= nineBytes[8] & 0xFFL;
        return time;
    }

    private static void ExpandKey(long value, byte[] dest)
    {
        for (int half = 0; half < 2; half++)
        {
            int baseOff = half * 8;
            for (int i = 0; i < 8; i++)
            {
                dest[baseOff + i] = (byte)((value >> (56 - i * 8)) & 0xFF);
            }
        }
    }

    public byte[]? Encrypt(byte[] clear)
    {
        int paddedSize = ((clear.Length >> 3) + (clear.Length % 8 == 0 ? 0 : 1)) << 1;
        int[] buffer = new int[paddedSize + 1];
        buffer[0] = clear.Length;
        if (!Pack(clear, buffer, 1))
        {
            return null;
        }
        Brew(buffer);
        return Unpack(buffer, 0, buffer.Length << 2);
    }

    public byte[]? Decrypt(byte[] crypt)
    {
        if (crypt.Length % 4 != 0 || (crypt.Length >> 2) % 2 != 1)
        {
            return null;
        }
        int[] buffer = new int[crypt.Length >> 2];
        if (!Pack(crypt, buffer, 0))
        {
            return null;
        }
        Unbrew(buffer);
        return Unpack(buffer, 1, buffer[0]);
    }

    private void Brew(int[] buf)
    {
        if (buf.Length % 2 != 1)
        {
            return;
        }

        for (int i = 1; i < buf.Length; i += 2)
        {
            int v0 = buf[i];
            int v1 = buf[i + 1];
            int sum = 0;

            for (int n = 32; n-- > 0;)
            {
                unchecked
                {
                    sum -= Delta;
                    v0 += (((v1 << 4) + _s[0]) ^ v1) + (sum ^ (v1 >>> 5)) + _s[1];
                    v1 += (((v0 << 4) + _s[2]) ^ v0) + (sum ^ (v0 >>> 5)) + _s[3];
                }
            }

            buf[i] = v0;
            buf[i + 1] = v1;
        }
    }

    private void Unbrew(int[] buf)
    {
        if (buf.Length % 2 != 1)
        {
            return;
        }

        for (int i = 1; i < buf.Length; i += 2)
        {
            int v0 = buf[i];
            int v1 = buf[i + 1];
            int sum = unchecked((int)0xC71C71C0); // -957401312, giống bản gốc

            for (int n = 32; n-- > 0;)
            {
                unchecked
                {
                    v1 -= (((v0 << 4) + _s[2]) ^ v0) + (sum ^ (v0 >>> 5)) + _s[3];
                    v0 -= (((v1 << 4) + _s[0]) ^ v1) + (sum ^ (v1 >>> 5)) + _s[1];
                    sum += Delta;
                }
            }

            buf[i] = v0;
            buf[i + 1] = v1;
        }
    }

    private static bool Pack(byte[] src, int[] dest, int destOffset)
    {
        if (destOffset + (src.Length >> 2) > dest.Length)
        {
            return false;
        }

        int i = 0;
        int shift = 24;
        int j = destOffset;
        dest[destOffset] = 0;

        for (; i < src.Length; ++i)
        {
            dest[j] |= (src[i] & 255) << shift;
            if (shift == 0)
            {
                shift = 24;
                ++j;
                if (j < dest.Length)
                {
                    dest[j] = 0;
                }
            }
            else
            {
                shift -= 8;
            }
        }

        return true;
    }

    private static byte[]? Unpack(int[] src, int srcOffset, int destLength)
    {
        if (destLength < 0 || destLength > (src.Length - srcOffset) << 2)
        {
            return null;
        }

        byte[] dest = new byte[destLength];
        int i = srcOffset;
        int count = 0;

        for (int j = 0; j < destLength; ++j)
        {
            dest[j] = (byte)((src[i] >> (24 - (count << 3))) & 255);
            ++count;
            if (count == 4)
            {
                count = 0;
                ++i;
            }
        }

        return dest;
    }
}
