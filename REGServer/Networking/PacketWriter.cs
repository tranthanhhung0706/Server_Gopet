using System.Text;

namespace REGServer.Networking;

/// <summary>
/// Ghi dữ liệu để tạo payload gói tin, tương đương DataOutputStream&lt;T&gt; cũ nhưng gọn hơn
/// (dùng MemoryStream trực tiếp thay vì generic + các hàm write* thủ công byte-by-byte).
/// </summary>
public sealed class PacketWriter
{
    private readonly MemoryStream _stream = new();

    public void WriteByte(int value) => _stream.WriteByte((byte)(value & 0xFF));

    public void WriteBool(bool value) => WriteByte(value ? 1 : 0);

    public void WriteShort(int value)
    {
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
        _stream.WriteByte((byte)(value & 0xFF));
    }

    public void WriteInt(int value)
    {
        for (int i = 3; i >= 0; i--)
        {
            _stream.WriteByte((byte)((value >> (i * 8)) & 0xFF));
        }
    }

    public void WriteLong(long value)
    {
        for (int i = 7; i >= 0; i--)
        {
            _stream.WriteByte((byte)((value >> (i * 8)) & 0xFF));
        }
    }

    public void WriteUtf(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteShort(bytes.Length);
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[] value) => _stream.Write(value, 0, value.Length);

    public byte[] ToArray() => _stream.ToArray();
}
