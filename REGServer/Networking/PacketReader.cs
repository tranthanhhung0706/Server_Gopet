using System.Text;

namespace REGServer.Networking;

/// <summary>
/// Đọc dữ liệu từ payload của 1 gói tin, tương đương DataInputStream cũ nhưng dùng byte[]
/// thay vì sbyte[]. Định dạng số vẫn big-endian giống bản gốc để tương thích client.
/// </summary>
public sealed class PacketReader
{
    private readonly byte[] _buffer;
    private int _pos;

    public PacketReader(byte[] buffer)
    {
        _buffer = buffer;
    }

    public int Available => _buffer.Length - _pos;

    public byte ReadByte()
    {
        if (_pos < _buffer.Length)
        {
            return _buffer[_pos++];
        }
        _pos = _buffer.Length;
        return 0;
    }

    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    public bool ReadBool() => ReadByte() != 0;

    public short ReadShort()
    {
        short value = 0;
        for (int i = 0; i < 2; i++)
        {
            value = (short)((value << 8) | (ReadByte() & 0xFF));
        }
        return value;
    }

    public ushort ReadUnsignedShort()
    {
        ushort value = 0;
        for (int i = 0; i < 2; i++)
        {
            value = (ushort)((value << 8) | (ReadByte() & 0xFF));
        }
        return value;
    }

    public int ReadInt()
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            value = (value << 8) | (ReadByte() & 0xFF);
        }
        return value;
    }

    public long ReadLong()
    {
        long value = 0;
        for (int i = 0; i < 8; i++)
        {
            value = (value << 8) | (uint)(ReadByte() & 0xFF);
        }
        return value;
    }

    public string ReadUtf()
    {
        int length = ReadUnsignedShort();
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = ReadByte();
        }
        return Encoding.UTF8.GetString(data);
    }

    public byte[] ReadBytes(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = ReadByte();
        }
        return data;
    }
}
