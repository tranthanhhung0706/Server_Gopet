using System.Collections.Concurrent;

namespace REGServer.Server;

/// <summary>Tương đương Manager/PlayerManager.cs cũ — danh sách người chơi đang online.</summary>
public sealed class PlayerManager
{
    public static readonly PlayerManager Instance = new();

    private readonly ConcurrentDictionary<Guid, Player> _players = new();

    public int OnlineCount => _players.Count;

    public IReadOnlyCollection<Player> Online => (IReadOnlyCollection<Player>)_players.Values;

    public void Add(Player player) => _players[player.Id] = player;

    public void Remove(Player player) => _players.TryRemove(player.Id, out _);

    public Player? FindByUsername(string username) =>
        _players.Values.FirstOrDefault(p => string.Equals(p.AccountUsername, username, StringComparison.OrdinalIgnoreCase));
}
