using REGServer.Logging;
using REGServer.Networking;

namespace REGServer.Server;

/// <summary>
/// Bảng định tuyến opcode -> handler, thay cho chuỗi if/switch khổng lồ trong GameController.cs cũ.
/// Đăng ký handler 1 lần lúc khởi động (xem Program.cs), Player chỉ việc gọi Dispatch mỗi khi nhận gói tin.
/// </summary>
public static class OpcodeRouter
{
    public delegate Task Handler(Player player, Packet packet, CancellationToken ct);

    private static readonly Dictionary<sbyte, Handler> Handlers = new();

    public static void Register(sbyte opcode, Handler handler) => Handlers[opcode] = handler;

    public static async Task DispatchAsync(Player player, Packet packet, CancellationToken ct)
    {
        if (Handlers.TryGetValue(packet.Id, out Handler? handler))
        {
            await handler(player, packet, ct).ConfigureAwait(false);
        }
        else
        {
            Log.Warning($"Không có handler cho opcode {packet.Id} (player={player.DisplayName})");
        }
    }
}
