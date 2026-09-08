using System.Collections.Concurrent;
using REGServer.Networking;
using REGServer.Server;

namespace REGServer.Zone;

/// <summary>
/// Tương đương Place/Place.cs cũ (1 zone/map instance chứa danh sách người chơi).
/// Đơn giản hoá: bỏ tick loop update() riêng — nếu cần vòng lặp game (di chuyển quái, hồi máu...),
/// thêm 1 background loop gọi Tick() định kỳ (xem Runtime/ trong GServer để tham khảo cách cũ làm).
/// </summary>
public class Zone
{
    public int ZoneId { get; }
    public int MaxPlayers { get; init; } = 50;

    private readonly ConcurrentDictionary<Guid, Player> _players = new();

    public IReadOnlyCollection<Player> Players => (IReadOnlyCollection<Player>)_players.Values;

    public int PlayerCount => _players.Count;

    public Zone(int zoneId)
    {
        ZoneId = zoneId;
    }

    public virtual bool CanAdd(Player player) => PlayerCount < MaxPlayers;

    public virtual void Add(Player player) => _players[player.Id] = player;

    public virtual void Remove(Player player) => _players.TryRemove(player.Id, out _);

    public void Broadcast(Packet packet)
    {
        foreach (Player player in _players.Values)
        {
            player.Send(packet);
        }
    }
}
