namespace REGServer.Networking;

/// <summary>
/// Tương đương Message.cs cũ: 1 gói tin gồm 1 byte opcode (id) + phần payload.
/// Opcode giữ kiểu sbyte vì client cũ định nghĩa opcode âm (vd -36 = ClientOK ack) — nếu port thêm
/// opcode từ GServer/Server/*.cs, copy nguyên giá trị số đó vào REGServer/Server/Handlers/Opcodes.cs.
/// </summary>
public sealed class Packet
{
    public sbyte Id { get; }
    public bool IsEncrypted { get; }

    private PacketWriter? _writer;
    private readonly PacketReader? _reader;

    /// <summary>Tạo gói tin để gửi đi.</summary>
    public Packet(int id, bool isEncrypted = false)
    {
        Id = unchecked((sbyte)id);
        IsEncrypted = isEncrypted;
    }

    /// <summary>Dựng lại gói tin nhận được từ payload đã giải mã (byte đầu = id).</summary>
    public Packet(byte[] rawPayload)
    {
        Id = unchecked((sbyte)rawPayload[0]);
        _reader = new PacketReader(rawPayload[1..]);
    }

    public PacketWriter Writer => _writer ??= new PacketWriter();

    public PacketReader Reader => _reader ?? throw new InvalidOperationException("Gói tin này không phải gói tin nhận được.");

    /// <summary>Xuất ra [id][payload] để đóng khung gửi đi.</summary>
    public byte[] ToBuffer()
    {
        byte[] payload = _writer?.ToArray() ?? [];
        byte[] buffer = new byte[payload.Length + 1];
        buffer[0] = unchecked((byte)Id);
        Buffer.BlockCopy(payload, 0, buffer, 1, payload.Length);
        return buffer;
    }
}
