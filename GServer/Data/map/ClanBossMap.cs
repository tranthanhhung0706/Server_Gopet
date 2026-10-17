using Gopet.Data.Collections;
using ClanEntity = Gopet.Data.GopetClan.Clan;
using Gopet.Data.Mob;
using Gopet.Util;
using System.Collections.Concurrent;

namespace Gopet.Data.Map
{
    /// <summary>
    /// Map 36 "Boss lãnh địa": mỗi bang có 1 khu riêng (ClanBossPlace, zone = clanId), vào qua mũi tên ở Khu vực bang
    /// hội (map 30). Người không có bang không vào được.
    /// </summary>
    public class ClanBossMap : GopetMap
    {
        public const int MAP_ID = 36;

        private readonly ConcurrentDictionary<int, ClanBossPlace> clanPlaces = new();

        public ClanBossMap(int mapId_, bool canUpdate, MapTemplate mapTemplate) : base(mapId_, canUpdate, mapTemplate)
        {
        }

        /// <summary>Không tạo 30 khu mặc định như map thường — khu được tạo theo từng bang khi có người vào.</summary>
        public override void createZoneDefault()
        {
        }

        public override void addPlace(Place place)
        {
            throw new UnsupportedOperationException("Khu boss lãnh địa được tạo theo bang");
        }

        public override void addRandom(Player player)
        {
            var clanMember = player.controller.getClan();
            if (clanMember == null)
            {
                player.redDialog(player.Language.MapRequireYouHaveClan);
                return;
            }
            ClanEntity clan = clanMember.getClan();
            ClanBossPlace place = clanPlaces.GetOrAdd(clan.clanId, _ => new ClanBossPlace(this, clan));
            place.add(player);
        }

        public override void update()
        {
            foreach (var place in clanPlaces.Values)
            {
                try
                {
                    place.update();
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
            }
        }
    }

    /// <summary>
    /// Khu boss lãnh địa của 1 bang. Luôn chỉ có 1 boss: bắt đầu từ boss TYPE_CLAN_BOSS cấp thấp nhất, hạ xong thì sau
    /// <see cref="RESPAWN_DELAY_MS"/> xuất hiện boss cấp kế tiếp, tới boss cấp cao nhất thì giữ ở cấp đó (hạ xong hồi lại
    /// chính nó). Danh sách boss lấy từ bảng `boss` (typeBoss = 5), sắp theo lvl.
    ///
    /// Phần thưởng: người hạ (đòn kết liễu) nhận quà theo cột `gift` của boss như mọi boss khác — cấu hình quỹ bang
    /// [12, số điểm], vàng [0, số], ngọc [1, số]. Tiến trình (đang ở cấp nào) giữ trong RAM, khởi động lại server thì bắt
    /// đầu lại từ cấp thấp nhất.
    /// </summary>
    public class ClanBossPlace : GopetPlace
    {
        public const long RESPAWN_DELAY_MS = 10000;
        private const int BOSS_X = 430;
        private const int BOSS_Y = 290;

        public ClanEntity Clan { get; }
        private int levelIndex = 0;
        private Boss? currentBoss;
        private long nextSpawnAt = 0;

        public ClanBossPlace(GopetMap m, ClanEntity clan) : base(m, clan.clanId)
        {
            this.Clan = clan;
            this.maxPlayer = int.MaxValue;
        }

        /// <summary>Boss lãnh địa sắp theo cấp (đọc mỗi lần để admin thêm/sửa boss + reload-catalog là áp dụng).</summary>
        public static BossTemplate[] Templates =>
            GopetManager.boss.Values.Where(b => b.typeBoss == BossTemplate.TYPE_CLAN_BOSS).OrderBy(b => b.lvl).ThenBy(b => b.bossId).ToArray();

        public override void update()
        {
            base.update();
            long now = Utilities.CurrentTimeMillis;

            if (currentBoss != null && !mobs.Contains(currentBoss))
            {
                bool killed = currentBoss.hp <= 0;
                var templates = Templates;
                if (killed && levelIndex < templates.Length - 1)
                {
                    levelIndex++;
                }
                currentBoss = null;
                nextSpawnAt = now + RESPAWN_DELAY_MS;
                if (killed)
                {
                    foreach (Player p in players)
                    {
                        p.Popup($"Đã hạ boss lãnh địa! Boss tiếp theo xuất hiện sau {RESPAWN_DELAY_MS / 1000} giây");
                    }
                }
            }

            if (currentBoss == null && now >= nextSpawnAt)
            {
                SpawnBoss();
            }
        }

        private void SpawnBoss()
        {
            var templates = Templates;
            if (templates.Length == 0)
            {
                // Chưa cấu hình boss nào → thử lại sau, tránh tạo lỗi mỗi nhịp
                nextSpawnAt = Utilities.CurrentTimeMillis + 60000;
                return;
            }
            levelIndex = Math.Clamp(levelIndex, 0, templates.Length - 1);
            BossTemplate template = templates[levelIndex];
            Boss boss = new Boss(template.bossId, new MobLocation(map.mapID, BOSS_X, BOSS_Y));
            boss.TimeOut = DateTime.MaxValue; // không tự biến mất theo giờ như boss thế giới
            boss.OwnerClan = Clan;            // chỉ thành viên bang này đánh được
            addNewMob(boss);
            currentBoss = boss;
            sendMob(new JArrayList<Gopet.Data.Mob.Mob> { boss });
            foreach (Player p in players)
            {
                p.Popup($"Boss lãnh địa cấp {template.lvl} xuất hiện!");
            }
        }

        /// <summary>Cấp hiện tại / tổng số boss — dùng cho hiển thị.</summary>
        public (int index, int total) Progress => (levelIndex, Templates.Length);
    }
}
