using System.Buffers.Binary;

namespace REGServer.Networking;

/// <summary>
/// Đóng khung / giải khung gói tin trên 1 Stream TCP, tương đương cặp MsgReader/MsgSender cũ
/// nhưng viết theo kiểu async hiện đại thay vì 2 thread thăm dò riêng cho mỗi kết nối.
///
/// Khung dữ liệu trên wire (giữ nguyên như bản gốc để tương thích client):
///   [Int32 BE: length][Byte: isEncrypted][length-1 byte: payload (id + field), có thể bị TEA mã hoá]
/// </summary>
public sealed class PacketStream
{
    private const int MaxPayloadSize = 10000;

    private readonly Stream _stream;
    private readonly Tea _tea;

    public PacketStream(Stream stream, Tea tea)
    {
        _stream = stream;
        _tea = tea;
    }

    public async Task<Packet?> ReadPacketAsync(CancellationToken ct = default)
    {
        byte[] header = new byte[4];
        try
        {
            await _stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        int totalLength = BinaryPrimitives.ReadInt32BigEndian(header);
        int payloadLength = totalLength - 1;
        if (payloadLength <= 0)
        {
            return null;
        }
        if (payloadLength > MaxPayloadSize)
        {
            throw new IOException("Dữ liệu quá lớn");
        }

        byte[] flagAndPayload = new byte[1 + payloadLength];
        await _stream.ReadExactlyAsync(flagAndPayload, ct).ConfigureAwait(false);

        bool isEncrypted = flagAndPayload[0] == 1;
        byte[] payload = flagAndPayload[1..];

        if (isEncrypted)
        {
            byte[]? decrypted = _tea.Decrypt(payload);
            if (decrypted == null)
            {
                return null;
            }
            payload = decrypted;
        }

        return new Packet(payload);
    }

    public async Task WritePacketAsync(Packet packet, CancellationToken ct = default)
    {
        byte[] data = packet.ToBuffer();
        if (packet.IsEncrypted)
        {
            data = _tea.Encrypt(data) ?? throw new IOException("Mã hoá gói tin thất bại");
        }

        byte[] frame = new byte[4 + 1 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, data.Length + 1);
        frame[4] = (byte)(packet.IsEncrypted ? 1 : 0);
        Buffer.BlockCopy(data, 0, frame, 5, data.Length);

        await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
