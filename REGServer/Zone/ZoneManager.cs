using System.Collections.Concurrent;

namespace REGServer.Zone;

/// <summary>Tương đương Manager/MapManager.cs cũ, thu gọn lại: chỉ quản lý registry các Zone theo id.</summary>
public static class ZoneManager
{
    private static readonly ConcurrentDictionary<int, Zone> Zones = new();

    public static Zone GetOrCreate(int zoneId)
    {
        return Zones.GetOrAdd(zoneId, id => new Zone(id));
    }

    public static Zone? Find(int zoneId) => Zones.GetValueOrDefault(zoneId);
}
