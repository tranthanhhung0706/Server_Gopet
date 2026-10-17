using Dapper;
using Gopet.Data.Collections;
using Gopet.Data.Map;
using Gopet.Data.GopetClan;
using Gopet.Data.Mob;
using Gopet.Data.User;
using Gopet.IO;
using Gopet.Server.IO;
using Gopet.Manager;
using Gopet.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Sự kiện bang hội 2026 — "Boss Hộ Vệ Bang".
    ///
    /// Cơ chế: mỗi giờ (đúng đầu giờ) có 1 đợt xuất hiện ở 1 khu ngẫu nhiên của 1 map săn quái ngẫu nhiên:
    /// - 1 Boss Hộ Vệ Bang (petTemplateId 3044, bảng `boss` typeBoss = 7) đứng chính giữa.
    /// - Tối đa <see cref="GUARD_COUNT"/> quái Hộ Vệ (petTemplateId 3043, bảng `boss` typeBoss = 8) đứng quanh.
    /// - Phải hạ HẾT hộ vệ mới đánh được boss (xem <see cref="IsBossLocked"/>, gọi từ GopetPlace.startFightMob).
    /// - Boss và hộ vệ biến mất sau TIME_BOSS_DISPOINTED (10 phút) nếu không ai hạ.
    ///
    /// Phần thưởng (điểm sự kiện, vàng, ngọc) đọc từ cột `gift` của từng dòng boss như mọi boss khác — vd
    /// [[11,50],[0,100],[1,5000]] = 50 điểm sự kiện + 100 vàng + 5000 ngọc cho người kết liễu. Điểm sự kiện là
    /// PlayerData.EventPoint, xếp hạng ở bảng "Top sự kiện" có sẵn.
    ///
    /// Bật/tắt + khung giờ đọc từ bảng `event_config` (eventKey = "clanevent2026") qua EventConfigManager — Init() tự
    /// thêm 1 dòng (đang TẮT) nếu chưa có để admin thấy ngay trên trang Event.
    /// </summary>
    public class ClanEvent2026 : EventBase
    {
        public static readonly ClanEvent2026 Instance = new ClanEvent2026();

        public const string EVENT_KEY = "clanevent2026";

        /// <summary>petTemplateId của Boss Hộ Vệ Bang / quái Hộ Vệ — dùng để dựng dòng `boss` tương ứng.</summary>
        public const int PET_TEMPLATE_BOSS = 3044;
        public const int PET_TEMPLATE_GUARD = 3043;

        /// <summary>typeBoss của boss sự kiện bang (khác 5 = boss lãnh địa) và của quái hộ vệ.</summary>
        public const sbyte TYPE_EVENT_BOSS = 7;
        public const sbyte TYPE_EVENT_GUARD = 8;

        /// <summary>Số quái hộ vệ tối đa đứng quanh boss mỗi đợt.</summary>
        public const int GUARD_COUNT = 4;

        /// <summary>Map không xuất hiện boss sự kiện (ải, đấu trường, chợ, khu bang, boss lãnh địa...).</summary>
        static readonly HashSet<int> EXCLUDED_MAPS = new() { 12, 19, 20, 22, 30, 36 };

        /// <summary>Boss -> danh sách hộ vệ của nó (để khoá boss tới khi hạ hết hộ vệ).</summary>
        readonly Dictionary<Boss, List<Boss>> guardsOfBoss = new();
        readonly object guardsLock = new();

        int lastSpawnKey = -1;

        protected ClanEvent2026()
        {
            this.Name = "Sự kiện bang hội 2026";
        }

        // ===== Vòng bảo hộ vàng quanh Boss Hộ Vệ Bang =====
        // Boss còn sống: vòng vàng bao quanh boss (chỉ để hiển thị). Hạ boss xong vòng "kích hoạt" ở lại CIRCLE_ACTIVE_WINDOW:
        // ai đứng liên tục trong vòng CIRCLE_STAY sẽ nhận buff +BUFF_EXP_PERCENT% exp và +BUFF_STAT_PERCENT% chỉ số pet
        // trong BUFF_DURATION. Buff chỉ lưu trong RAM (restart server là mất).

        /// <summary>Bán kính vòng (px) — client vẽ đúng bán kính này, server dùng để kiểm tra đứng trong vòng.</summary>
        public const int CIRCLE_RADIUS = 70;
        public static readonly TimeSpan CIRCLE_ACTIVE_WINDOW = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan CIRCLE_STAY = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan BUFF_DURATION = TimeSpan.FromMinutes(30);
        public const int BUFF_EXP_PERCENT = 20;
        public const int BUFF_STAT_PERCENT = 10;
        /// <summary>Icon + đếm ngược buff hiện trong client (assets/items/4000784.png).</summary>
        const string BUFF_ICON = "items/4000784.png";

        class ProtectCircle
        {
            public int Id;
            public GopetPlace Place = null!;
            public int X, Y;
            public bool Active;
            public DateTime Expire;
            /// <summary>clanId -> lúc bang bắt đầu có người đứng trong vòng (reset khi cả bang không còn ai trong vòng).</summary>
            public Dictionary<int, DateTime> ClanSince = new();
            /// <summary>Các bang đã nhận buff từ vòng này (mỗi bang 1 lần).</summary>
            public HashSet<int> RewardedClans = new();
        }

        readonly List<ProtectCircle> circles = new();
        readonly Dictionary<Boss, ProtectCircle> circleOfBoss = new();
        /// <summary>userId -> lúc hết buff.</summary>
        readonly Dictionary<int, DateTime> buffs = new();
        readonly object circleLock = new();
        int nextCircleId = 1;

        static bool IsBuffed(Player player)
        {
            var inst = Instance;
            lock (inst.circleLock)
            {
                return inst.buffs.TryGetValue(player.user.user_id, out var expire) && expire > DateTime.Now;
            }
        }

        /// <summary>% exp cộng thêm khi giết quái (đơn vị % — 20 = +20%).</summary>
        public static float GetExpBuffPercent(Player player) => IsBuffed(player) ? BUFF_EXP_PERCENT : 0f;

        /// <summary>% cộng thêm cho atk/def/hp/mp của pet (đơn vị % — 10 = +10%).</summary>
        public static float GetStatBuffPercent(Player player) => IsBuffed(player) ? BUFF_STAT_PERCENT : 0f;

        Message BuildCircleMessage(ProtectCircle c, bool add)
        {
            Message m = GameController.messagePetService(GopetCMD.ZONE_CIRCLE);
            m.putsbyte(add ? 1 : 0);
            m.putInt(c.Id);
            if (add)
            {
                m.putInt(c.X);
                m.putInt(c.Y);
                m.putInt(CIRCLE_RADIUS);
                m.putbool(c.Active);
            }
            m.cleanup();
            return m;
        }

        /// <summary>Gửi các vòng đang có trong khu cho người vừa vào khu (gọi từ GopetPlace.addPlayer).</summary>
        public void SendCirclesTo(GopetPlace place, Player player)
        {
            try
            {
                lock (circleLock)
                {
                    foreach (var c in circles.Where(c => c.Place == place))
                    {
                        player.session.sendMessage(BuildCircleMessage(c, true));
                    }
                }
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }

        void CreateCircle(Boss boss, GopetPlace place, int x, int y, DateTime expire)
        {
            lock (circleLock)
            {
                var c = new ProtectCircle { Id = nextCircleId++, Place = place, X = x, Y = y, Expire = expire };
                circles.Add(c);
                circleOfBoss[boss] = c;
                place.sendMessage(BuildCircleMessage(c, true));
            }
        }

        /// <summary>Boss bị hạ: vòng chuyển sang trạng thái kích hoạt trong CIRCLE_ACTIVE_WINDOW.</summary>
        void ActivateCircle(Boss boss)
        {
            lock (circleLock)
            {
                if (!circleOfBoss.TryGetValue(boss, out var c)) return;
                circleOfBoss.Remove(boss);
                c.Active = true;
                c.Expire = DateTime.Now.Add(CIRCLE_ACTIVE_WINDOW);
                c.Place.sendMessage(BuildCircleMessage(c, true));
                foreach (var p in c.Place.players)
                {
                    if (p.controller.getClan() != null && InCircle(c, p))
                    {
                        p.Popup($"Vòng bảo hộ đã mở! Bang bạn phải có người đứng trong vòng liên tục {(int)CIRCLE_STAY.TotalMinutes} phút để nhận buff.");
                    }
                }
            }
        }

        static bool InCircle(ProtectCircle c, Player p)
        {
            long dx = p.playerData.x - c.X, dy = p.playerData.y - c.Y;
            return dx * dx + dy * dy <= (long)CIRCLE_RADIUS * CIRCLE_RADIUS;
        }

        void GrantBuff(Player player)
        {
            lock (circleLock)
            {
                buffs[player.user.user_id] = DateTime.Now.Add(BUFF_DURATION);
            }
            RefreshPetStats(player);
            SendBuffIcon(player);
            player.okDialog($"Bang của bạn đã giữ vòng bảo hộ đủ {(int)CIRCLE_STAY.TotalMinutes} phút! Nhận buff +{BUFF_EXP_PERCENT}% exp và +{BUFF_STAT_PERCENT}% chỉ số trong {(int)BUFF_DURATION.TotalMinutes} phút.");
        }

        /// <summary>Gửi icon + thời gian còn lại của buff cho client (0 giây = tắt icon). Gọi cả lúc đăng nhập.</summary>
        public void SendBuffIcon(Player player)
        {
            try
            {
                int remain = 0;
                lock (circleLock)
                {
                    if (buffs.TryGetValue(player.user.user_id, out var expire) && expire > DateTime.Now)
                    {
                        remain = (int)Math.Ceiling((expire - DateTime.Now).TotalSeconds);
                    }
                }
                Message m = GameController.messagePetService(GopetCMD.CLAN_EVENT_BUFF);
                m.putUTF(BUFF_ICON);
                m.putInt(remain);
                m.cleanup();
                player.session.sendMessage(m);
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }

        static void RefreshPetStats(Player player)
        {
            try
            {
                player.getPet()?.applyInfo(player);
                player.controller.sendMyPetInfo();
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }

        /// <summary>Chạy mỗi giây: dọn vòng hết hạn, tính thời gian đứng trong vòng, phát/thu hồi buff.</summary>
        void TickCircles()
        {
            DateTime now = DateTime.Now;
            var expiredBuffs = new List<int>();
            lock (circleLock)
            {
                foreach (var c in circles.ToList())
                {
                    if (now >= c.Expire)
                    {
                        circles.Remove(c);
                        foreach (var bk in circleOfBoss.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) circleOfBoss.Remove(bk);
                        c.Place.sendMessage(BuildCircleMessage(c, false));
                        continue;
                    }
                    if (!c.Active) continue;
                    // Mỗi bang tính giờ riêng: chạy khi còn ÍT NHẤT 1 thành viên của bang đứng trong vòng, reset khi cả bang
                    // không còn ai trong vòng. Đủ CIRCLE_STAY thì các thành viên đang đứng trong vòng nhận buff (mỗi bang 1 lần).
                    var insideByClan = new Dictionary<int, List<Player>>();
                    foreach (var p in c.Place.players)
                    {
                        var member = p.controller.getClan();
                        if (member?.clan == null || !InCircle(c, p)) continue; // chỉ thành viên bang mới được tính
                        int clanId = member.clan.clanId;
                        if (c.RewardedClans.Contains(clanId)) continue;
                        if (!insideByClan.TryGetValue(clanId, out var list)) insideByClan[clanId] = list = new List<Player>();
                        list.Add(p);
                    }
                    foreach (int clanId in c.ClanSince.Keys.Where(id => !insideByClan.ContainsKey(id)).ToList())
                    {
                        c.ClanSince.Remove(clanId); // cả bang rời vòng → tính lại từ đầu
                    }
                    bool captured = false;
                    foreach (var (clanId, members) in insideByClan)
                    {
                        if (captured) break; // vòng đã bị 1 bang chiếm trong tick này
                        if (!c.ClanSince.TryGetValue(clanId, out var since))
                        {
                            c.ClanSince[clanId] = now;
                            foreach (var p in members)
                            {
                                p.Popup($"Bang bạn vào vòng bảo hộ — giữ vòng liên tục {(int)CIRCLE_STAY.TotalMinutes} phút để nhận buff.");
                            }
                        }
                        else if (now - since >= CIRCLE_STAY)
                        {
                            c.RewardedClans.Add(clanId);
                            c.ClanSince.Remove(clanId);
                            // Chỉ cần 1 thành viên giữ vòng đủ giờ: TẤT CẢ thành viên bang đang online đều nhận buff
                            var clan = members[0].controller.getClan()!.clan;
                            foreach (var cm in clan.members.ToList())
                            {
                                var online = PlayerManager.get(cm.user_id);
                                if (online != null) GrantBuff(online);
                            }
                            captured = true;
                        }
                    }
                    if (captured)
                    {
                        // Vòng bị chiếm thành công → biến mất luôn, bang khác không còn cơ hội nhận buff từ vòng này
                        circles.Remove(c);
                        c.Place.sendMessage(BuildCircleMessage(c, false));
                    }
                }
                foreach (var kv in buffs.Where(kv => kv.Value <= now).ToList())
                {
                    buffs.Remove(kv.Key);
                    expiredBuffs.Add(kv.Key);
                }
            }
            foreach (int uid in expiredBuffs)
            {
                var p = PlayerManager.get(uid);
                if (p == null) continue;
                RefreshPetStats(p);
                SendBuffIcon(p); // buff đã xoá → remain = 0 → client tắt icon
                p.Popup("Buff vòng bảo hộ đã hết.");
            }
        }

        // Vẫn chạy Update khi sự kiện tắt nếu còn vòng/buff cần dọn (đợt triệu hồi test của admin) — xem Update().
        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY) || circles.Count > 0 || buffs.Count > 0;

        public override bool NeedRemove => false;

        public override void Init()
        {
            try
            {
                using var conn = MYSQLManager.create();
                conn.Execute(
                    @"CREATE TABLE IF NOT EXISTS `event_clan_score` (
                        `user_id` INT NOT NULL,
                        `clan_id` INT NOT NULL DEFAULT 0,
                        `clan_name` VARCHAR(100) NOT NULL DEFAULT '',
                        `name` VARCHAR(100) NOT NULL DEFAULT '',
                        `Points` BIGINT NOT NULL DEFAULT 0,
                        `BossKills` INT NOT NULL DEFAULT 0,
                        `GuardKills` INT NOT NULL DEFAULT 0,
                        `UpdatedAt` DATETIME NULL,
                        PRIMARY KEY (`user_id`),
                        KEY `idx_clan` (`clan_id`)
                      ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `event_config` WHERE eventKey = @key", new { key = EVENT_KEY });
                if (existing == 0)
                {
                    conn.Execute("INSERT INTO `event_config` (eventKey, name, isEnabled, startTime, endTime) VALUES (@key, @name, 0, NULL, NULL)",
                        new { key = EVENT_KEY, name = this.Name });
                    EventConfigManager.Reload();
                }
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }

            BXHManager.listTop.Add(TopClanEvent2026.Instance);
            // BẮT BUỘC có tên option cho TỪNG ngôn ngữ, thiếu sẽ crash KeyNotFoundException khi mở menu NPC.
            foreach (var lang in GopetManager.Language)
            {
                lang.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_CLAN_EVENT_2026] = "Top bang sự kiện bang hội";
            }
            // Gắn option vào mọi NPC đang có option "Top level bang hội" (không cần sửa bảng npc).
            foreach (var npc in GopetManager.npcTemplate.Values)
            {
                if (npc.optionId.Contains(MenuController.OP_TOP_LVL_CLAN) && !npc.optionId.Contains(MenuController.OP_XEM_TOP_CLAN_EVENT_2026))
                {
                    npc.optionId = npc.optionId.Concat(new int[] { MenuController.OP_XEM_TOP_CLAN_EVENT_2026 }).ToArray();
                }
            }
        }

        /// <summary>
        /// Ghi điểm khi người chơi kết liễu Boss Hộ Vệ Bang / hộ vệ (gọi từ PetBattle). Điểm = tổng các mục [11,x] trong
        /// cột gift của dòng boss (đúng số điểm sự kiện người chơi nhận). Chỉ tính khi sự kiện đang bật và người chơi có bang.
        /// </summary>
        public void OnKill(Player killer, Boss boss)
        {
            try
            {
                sbyte type = boss.Template.typeBoss;
                if (type == TYPE_EVENT_BOSS) ActivateCircle(boss);
                if ((type != TYPE_EVENT_BOSS && type != TYPE_EVENT_GUARD) || !EventConfigManager.IsActive(EVENT_KEY)) return;
                ClanMember? member = killer.controller.getClan();
                if (member?.clan == null) return;
                int points = (boss.Template.gift ?? Array.Empty<int[]>()).Where(g => g.Length >= 2 && g[0] == GopetManager.GIFT_EVENT_POINT).Sum(g => g[1]);
                int userId = killer.user.user_id;
                string name = killer.playerData.name;
                int clanId = member.clan.clanId;
                string clanName = member.clan.name;
                bool isBoss = type == TYPE_EVENT_BOSS;
                // Ghi DB ở luồng nền (PetBattle.win chạy trên luồng map); cộng dồn bằng Points = Points + @points
                Task.Run(() =>
                {
                    try
                    {
                        using var conn = MYSQLManager.create();
                        conn.Execute(
                            @"INSERT INTO `event_clan_score` (user_id, clan_id, clan_name, name, Points, BossKills, GuardKills, UpdatedAt)
                              VALUES (@userId, @clanId, @clanName, @name, @points, @b, @g, NOW())
                              ON DUPLICATE KEY UPDATE clan_id = @clanId, clan_name = @clanName, name = @name, Points = Points + @points,
                                  BossKills = BossKills + @b, GuardKills = GuardKills + @g, UpdatedAt = NOW()",
                            new { userId, clanId, clanName, name, points, b = isBoss ? 1 : 0, g = isBoss ? 0 : 1 });
                    }
                    catch (Exception e)
                    {
                        e.printStackTrace();
                    }
                });
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }

        /// <summary>Bảng xếp hạng BANG theo tổng điểm sự kiện bang hội (cộng điểm của mọi thành viên).</summary>
        public class TopClanEvent2026 : Top
        {
            public static readonly TopClanEvent2026 Instance = new TopClanEvent2026();

            long lastRefreshMs = 0;

            protected TopClanEvent2026() : base("clan.event.2026.top")
            {
                this.name = "TOP Bang Sự kiện bang hội";
                this.desc = "Bang có tổng điểm sự kiện cao nhất";
            }

            public override TopData getMyInfo(Player player)
            {
                // BXHManager nạp 5 phút/lần và lần đầu chạy trước khi bảng đăng ký → làm mới nếu cũ quá 30 giây
                if (Utilities.CurrentTimeMillis - lastRefreshMs > 30000)
                {
                    Update();
                }
                TopData topData = new TopData();
                topData.id = player.playerData.user_id;
                topData.name = player.playerData.name;
                topData.imgPath = player.playerData.avatarPath;
                topData.title = topData.name;
                ClanMember? member = player.controller.getClan();
                if (member?.clan == null)
                {
                    topData.desc = "Bạn chưa có bang hội.";
                    return topData;
                }
                var mine = datas.FirstOrDefault(d => d.id == member.clan.clanId);
                topData.desc = mine != null ? "Bang của bạn: " + mine.desc : "Bang của bạn chưa có điểm.";
                return topData;
            }

            public override void Update()
            {
                try
                {
                    lastRefreshMs = Utilities.CurrentTimeMillis;
                    lastDatas.Clear();
                    lastDatas.AddRange(datas);
                    datas.Clear();
                    using var conn = MYSQLManager.create();
                    var rows = conn.Query(
                        @"SELECT s.clan_id, COALESCE(MAX(c.name), MAX(s.clan_name)) AS clan_name, SUM(s.Points) AS Points,
                                 SUM(s.BossKills) AS BossKills, SUM(s.GuardKills) AS GuardKills, MAX(s.UpdatedAt) AS UpdatedAt
                          FROM `event_clan_score` s LEFT JOIN `clan` c ON c.clanId = s.clan_id
                          GROUP BY s.clan_id HAVING SUM(s.Points) > 0
                          ORDER BY Points DESC, UpdatedAt ASC LIMIT 10");
                    int index = 1;
                    foreach (dynamic data in rows)
                    {
                        TopData topData = new TopData();
                        topData.id = data.clan_id;
                        topData.name = "Bang " + data.clan_name;
                        topData.imgPath = "npcs/gopet.png";
                        topData.title = topData.name;
                        // SUM() trả decimal; FormatNumber chỉ nhận long nên phải ép kiểu (dynamic sẽ ném RuntimeBinderException)
                        long points = (long)Convert.ToDecimal(data.Points);
                        long bossKills = (long)Convert.ToDecimal(data.BossKills);
                        long guardKills = (long)Convert.ToDecimal(data.GuardKills);
                        topData.desc = $"Hạng {index}: {Utilities.FormatNumber(points)} điểm ({bossKills} boss, {guardKills} hộ vệ)";
                        datas.Add(topData);
                        index++;
                    }
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
            }
        }

        public override void Update()
        {
            TickCircles();
            if (!EventConfigManager.IsActive(EVENT_KEY))
            {
                return; // sự kiện tắt: chỉ dọn vòng/buff còn sót, không triệu hồi theo giờ
            }
            DateTime now = DateTime.Now;
            int key = now.DayOfYear * 100 + now.Hour;
            if (lastSpawnKey == -1)
            {
                // Lần chạy đầu (vừa khởi động hoặc vừa bật sự kiện): đợi đầu giờ kế tiếp mới xuất hiện
                lastSpawnKey = key;
                return;
            }
            if (key == lastSpawnKey)
            {
                return;
            }
            lastSpawnKey = key;
            SpawnWave(null, null, out _);
        }

        BossTemplate? FindTemplate(sbyte type, int petTemplateId) =>
            GopetManager.boss.Values.Where(b => b.typeBoss == type && b.petTemplateId == petTemplateId).OrderBy(b => b.bossId).FirstOrDefault();

        /// <summary>
        /// Triệu hồi 1 đợt NGAY (admin test qua API, chạy được cả khi sự kiện đang tắt). mapId/zone để trống = ngẫu nhiên.
        /// Trả về thông báo kết quả để hiện cho admin.
        /// </summary>
        public string SummonNow(int? mapId, int? zone)
        {
            return SpawnWave(mapId, zone, out string message) ? message : message;
        }

        bool SpawnWave(int? forceMapId, int? forceZone, out string message)
        {
            BossTemplate? bossTemplate = FindTemplate(TYPE_EVENT_BOSS, PET_TEMPLATE_BOSS);
            BossTemplate? guardTemplate = FindTemplate(TYPE_EVENT_GUARD, PET_TEMPLATE_GUARD);
            if (bossTemplate == null)
            {
                message = "Chưa có dòng boss typeBoss=7 (petTemplateId 3044) trong bảng boss";
                GopetManager.ServerMonitor.LogWarning("Sự kiện bang hội: " + message);
                return false;
            }

            var maps = MapManager.mapArr.Where(m => !EXCLUDED_MAPS.Contains(m.mapID) && GopetManager.mobLocation.ContainsKey(m.mapID)).ToArray();
            if (forceMapId.HasValue)
            {
                maps = MapManager.mapArr.Where(m => m.mapID == forceMapId.Value).ToArray();
            }
            if (maps.Length == 0)
            {
                message = "Không có map phù hợp";
                return false;
            }

            DateTime deadline = DateTime.Now.AddSeconds(5);
            while (DateTime.Now < deadline)
            {
                var map = Utilities.RandomArray(maps);
                if (map.places.Count == 0) continue;
                var place = forceZone.HasValue
                    ? (GopetPlace?)map.places.FirstOrDefault(pl => pl.zoneID == forceZone.Value)
                    : (GopetPlace)Utilities.RandomArray(map.places);
                if (place == null) break;
                var candidates = place.mobs.Where(x => !(x is Boss) && !x.HasBattle).ToList();
                if (candidates.Count == 0) continue;

                // Boss lấy chỗ của 1 quái thường, hộ vệ lấy chỗ của các quái thường gần nhất xung quanh
                Mob.Mob center = Utilities.RandomArray(candidates);
                var around = guardTemplate == null
                    ? new List<Mob.Mob>()
                    : candidates.Where(c => c != center)
                        .OrderBy(c => Math.Abs(c.getMobLocation().getX() - center.getMobLocation().getX()) + Math.Abs(c.getMobLocation().getY() - center.getMobLocation().getY()))
                        .Take(GUARD_COUNT).ToList();

                DateTime timeout = DateTime.Now.AddMilliseconds(GopetManager.TIME_BOSS_DISPOINTED);
                var newMobs = new JArrayList<Mob.Mob>();

                place.mobs.remove(center);
                place.sendRemoveMob(center.getMobId());
                Boss boss = new Boss(bossTemplate.bossId, center.getMobLocation());
                boss.isTimeOut = true;
                boss.TimeOut = timeout;
                place.addNewMob(boss);
                newMobs.add(boss);

                var guards = new List<Boss>();
                foreach (var g in around)
                {
                    place.mobs.remove(g);
                    place.sendRemoveMob(g.getMobId());
                    Boss guard = new Boss(guardTemplate!.bossId, g.getMobLocation());
                    guard.isTimeOut = true;
                    guard.TimeOut = timeout;
                    place.addNewMob(guard);
                    newMobs.add(guard);
                    guards.Add(guard);
                }
                place.sendMob(newMobs);
                CreateCircle(boss, place, center.getMobLocation().getX(), center.getMobLocation().getY(), timeout);
                lock (guardsLock)
                {
                    foreach (var dead in guardsOfBoss.Keys.Where(b => b.TimeOut < DateTime.Now).ToList())
                    {
                        guardsOfBoss.Remove(dead); // dọn các đợt cũ đã hết hạn
                    }
                    guardsOfBoss[boss] = guards;
                }
                PlayerManager.showBannerZ($"{bossTemplate.name} cùng {guards.Count} hộ vệ vừa xuất hiện tại {place.map.mapTemplate.name} khu {place.zoneID}. Hạ hết hộ vệ rồi hạ boss để nhận thưởng sự kiện!");
                message = $"Đã triệu hồi tại {place.map.mapTemplate.name} (map {place.map.mapID}) khu {place.zoneID} với {guards.Count} hộ vệ";
                return true;
            }
            message = "Không tìm được khu nào còn quái thường để dựng boss (thử map/khu khác)";
            return false;
        }

        /// <summary>
        /// true = boss sự kiện còn hộ vệ sống → chưa được đánh boss. Boss khác (không thuộc sự kiện) luôn false.
        /// Hộ vệ "còn sống" = còn trong khu và máu > 0.
        /// </summary>
        public bool IsBossLocked(GopetPlace place, Mob.Mob mob, out int guardsAlive)
        {
            guardsAlive = 0;
            if (mob is not Boss boss || boss.Template.typeBoss != TYPE_EVENT_BOSS)
            {
                return false;
            }
            lock (guardsLock)
            {
                if (guardsOfBoss.TryGetValue(boss, out var guards))
                {
                    guardsAlive = guards.Count(g => g.hp > 0 && place.mobs.Contains(g));
                }
            }
            return guardsAlive > 0;
        }
    }
}
