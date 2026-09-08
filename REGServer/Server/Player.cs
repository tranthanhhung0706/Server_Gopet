using REGServer.Networking;

namespace REGServer.Server;

/// <summary>
/// Tương đương Server/Player.cs cũ, nhưng KHÔNG ôm hết logic game (GameController god-class).
/// Đây là chỗ giữ state của 1 người chơi đang kết nối; nghiệp vụ cụ thể (login, di chuyển, chiến
/// đấu...) nên viết thành handler riêng đăng ký qua OpcodeRouter, để mỗi tính năng nằm gọn 1 file.
/// </summary>
public sealed class Player : IPacketHandler
{
    public Guid Id { get; } = Guid.NewGuid();
    public Session Session { get; }
    public string? AccountUsername { get; set; }
    public int PlayerDbId { get; set; }
    public bool IsAuthenticated { get; set; }

    public string DisplayName => AccountUsername ?? Session.RemoteIp;

    public Player(Session session)
    {
        Session = session;
        Session.SetHandler(this);
    }

    public Task OnMessageAsync(Packet packet, CancellationToken ct) => OpcodeRouter.DispatchAsync(this, packet, ct);

    public Task OnDisconnectedAsync()
    {
        PlayerManager.Instance.Remove(this);
        return Task.CompletedTask;
    }

    public void Send(Packet packet) => Session.Send(packet);
}
