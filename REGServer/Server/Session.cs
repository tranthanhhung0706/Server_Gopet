using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using REGServer.Logging;
using REGServer.Networking;

namespace REGServer.Server;

/// <summary>
/// Tương đương Server/IO/Session.cs cũ (bắt tay khoá TEA, đọc/ghi gói tin), nhưng thay 2 thread
/// (send thread + read thread) bằng 1 vòng đọc async + 1 Channel làm hàng đợi gửi — cùng ý tưởng,
/// ít tài nguyên hơn khi có nhiều kết nối.
/// </summary>
public sealed class Session
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<Packet> _sendQueue = Channel.CreateUnbounded<Packet>();
    private readonly CancellationTokenSource _cts = new();
    private PacketStream? _packetStream;

    public string RemoteIp { get; }
    public bool IsConnected { get; private set; }
    public IPacketHandler? Handler { get; private set; }

    public Session(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        RemoteIp = client.Client.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "unknown";
    }

    public void SetHandler(IPacketHandler handler) => Handler = handler;

    /// <summary>Đọc 9 byte đầu tiên client gửi để suy khoá TEA (giống Session.readKey() cũ).</summary>
    public async Task<bool> HandshakeAsync(CancellationToken ct)
    {
        byte[] keyBytes = new byte[9];
        try
        {
            await _stream.ReadExactlyAsync(keyBytes, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }

        long key = Tea.DeriveHandshakeKey(keyBytes);
        _packetStream = new PacketStream(_stream, new Tea(key));
        IsConnected = true;
        return true;
    }

    /// <summary>Gửi thông báo client OK, opcode -36 giống bản gốc (MenuController/Session.setClientOK).</summary>
    public void SendClientOk(bool ok)
    {
        var packet = new Packet(Opcodes.ClientOk);
        packet.Writer.WriteByte(ok ? 1 : 0);
        Send(packet);
    }

    public void Send(Packet packet) => _sendQueue.Writer.TryWrite(packet);

    public async Task RunAsync()
    {
        Task readTask = ReadLoopAsync(_cts.Token);
        Task sendTask = SendLoopAsync(_cts.Token);
        await Task.WhenAny(readTask, sendTask).ConfigureAwait(false);
        await CloseAsync().ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Packet? packet = await _packetStream!.ReadPacketAsync(ct).ConfigureAwait(false);
                if (packet == null)
                {
                    break;
                }
                if (Handler != null)
                {
                    await Handler.OnMessageAsync(packet, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Lỗi đọc gói tin từ {RemoteIp}", ex);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (Packet packet in _sendQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await _packetStream!.WritePacketAsync(packet, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Lỗi gửi gói tin tới {RemoteIp}", ex);
        }
    }

    public async Task CloseAsync()
    {
        if (!IsConnected)
        {
            return;
        }
        IsConnected = false;
        _cts.Cancel();
        _sendQueue.Writer.TryComplete();

        try
        {
            _client.Close();
        }
        catch
        {
            // đã đóng rồi thì thôi
        }

        if (Handler != null)
        {
            await Handler.OnDisconnectedAsync().ConfigureAwait(false);
        }
    }
}
