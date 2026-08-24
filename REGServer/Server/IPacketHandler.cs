using REGServer.Networking;

namespace REGServer.Server;

/// <summary>Tương đương IHandleMessage.cs cũ, chuyển sang async.</summary>
public interface IPacketHandler
{
    Task OnMessageAsync(Packet packet, CancellationToken ct);

    Task OnDisconnectedAsync();
}
