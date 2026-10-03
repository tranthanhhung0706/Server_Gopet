using Dapper;
using Gopet.Data.Collections;
using Gopet.Data.Mob;
using Gopet.Data.User;
using Gopet.Manager;
using Gopet.Util;
using System;
using System.Linq;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Sự kiện Vượt Ải 2026 — tính điểm khi hạ quái/boss trong phòng Vượt Ải (ChallengePlace).
    ///
    /// Cơ chế:
    /// - Hạ 1 con quái thường trong ải: +(level quái × POINT_PER_MOB_LEVEL) điểm cho người kết liễu.
    /// - Hạ boss ải: +(level boss × POINT_PER_BOSS_LEVEL) điểm cho MỌI người đã gây sát thương lên
    ///   boss đó (đánh đồng đội nên không để mình người giáp đòn cuối hưởng hết).
    /// - Điểm cộng dồn vào bảng `event_challenge_score` (tự tạo lúc Init, không cần chạy SQL) và hiện
    ///   ở bảng xếp hạng TopChallengeScore2026 (mở từ NPC Đấu Trường, cạnh "Top vượt ải").
    ///
    /// Bật/tắt + khung giờ sự kiện đọc từ bảng `event_config` (eventKey = "challenge2026") qua
    /// EventConfigManager — sửa qua trang admin Event, áp dụng ngay không cần build lại/restart.
    /// Init() tự thêm 1 dòng event_config (đang TẮT) nếu chưa có để admin thấy ngay trên trang Event.
    ///
    /// Hook điểm: PetBattle.win() gọi OnKillMob/OnKillBoss, CHỈ khi place là ChallengePlace.
    /// </summary>
    public class ChallengeEvent2026 : EventBase
    {
        public static readonly ChallengeEvent2026 Instance = new ChallengeEvent2026();

        public const string EVENT_KEY = "challenge2026";

        /// <summary>Điểm cho mỗi cấp của quái thường (điểm = level × hệ số này).</summary>
        public const int POINT_PER_MOB_LEVEL = 1;

        /// <summary>Điểm cho mỗi cấp của boss ải (điểm = level × hệ số này).</summary>
        public const int POINT_PER_BOSS_LEVEL = 10;

        protected ChallengeEvent2026()
        {
            this.Name = "Sự kiện Vượt Ải 2026";
        }

        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY);

        public override bool NeedRemove => false;

        /// <summary>
        /// Cộng điểm hạ quái thường. Trả về số điểm vừa cộng (0 nếu sự kiện đang đóng).
        /// </summary>
        public int OnKillMob(Player player, Gopet.Data.Mob.Mob mob)
        {
            if (!Condition || mob == null || mob is Boss)
            {
                return 0;
            }
            int lvl = mob.getMobLvInfo()?.lvl ?? 0;
            int points = lvl * POINT_PER_MOB_LEVEL;
            return AddScore(player, points, isBoss: false);
        }

        /// <summary>
        /// Cộng điểm hạ boss ải cho mọi người đã gây sát thương. Trả về điểm của <paramref name="killer"/>
        /// (người kết liễu, để hiện cùng popup phần thưởng); người còn lại tự nhận popup riêng.
        /// </summary>
        public int OnKillBoss(Player killer, Boss boss)
        {
            if (!Condition || boss == null)
            {
                return 0;
            }
            int points = (boss.getMobLvInfo()?.lvl ?? 0) * POINT_PER_BOSS_LEVEL;
            Player[] participants;
            try
            {
                participants = boss.Damage.Keys.ToArray();
            }
            catch (Exception)
            {
                participants = new Player[0];
            }
            int killerPoints = 0;
            foreach (Player p in participants.Append(killer).Distinct())
            {
                int gained = AddScore(p, points, isBoss: true);
                if (p == killer)
                {
                    killerPoints = gained;
                }
                else if (gained > 0)
                {
                    p.Popup($"+{Utilities.FormatNumber(gained)} điểm sự kiện (hạ boss)");
                }
            }
            return killerPoints;
        }

        int AddScore(Player player, int points, bool isBoss)
        {
            if (player == null || points <= 0)
            {
                return 0;
            }
            // Ghi DB ở luồng nền: PetBattle.win() chạy trên luồng map, không nên chờ MySQL ở đó.
            // Cộng dồn bằng "Points = Points + @points" nên thứ tự các lần ghi không ảnh hưởng kết quả.
            int userId = player.user.user_id;
            string name = player.playerData.name;
            Task.Run(() =>
            {
                try
                {
                    using var conn = MYSQLManager.create();
                    conn.Execute(
                        @"INSERT INTO `event_challenge_score` (user_id, name, Points, MobKills, BossKills, UpdatedAt)
                          VALUES (@userId, @name, @points, @mobKills, @bossKills, NOW())
                          ON DUPLICATE KEY UPDATE name = @name, Points = Points + @points,
                              MobKills = MobKills + @mobKills, BossKills = BossKills + @bossKills, UpdatedAt = NOW()",
                        new { userId, name, points, mobKills = isBoss ? 0 : 1, bossKills = isBoss ? 1 : 0 });
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
            });
            return points;
        }

        /// <summary>
        /// Tạo bảng điểm + dòng event_config (nếu chưa có), đăng ký bảng xếp hạng + 2 option NPC, gắn
        /// option vào NPC Đấu Trường — chạy ĐÚNG 1 LẦN lúc khởi động, KHÔNG phụ thuộc Condition (giống
        /// Boss2026/TrungThu2026, để bật sự kiện sau này qua trang admin không thiếu bảng xếp hạng).
        /// </summary>
        public override void Init()
        {
            try
            {
                using var conn = MYSQLManager.create();
                conn.Execute(
                    @"CREATE TABLE IF NOT EXISTS `event_challenge_score` (
                        `user_id` INT NOT NULL,
                        `name` VARCHAR(100) NOT NULL DEFAULT '',
                        `Points` BIGINT NOT NULL DEFAULT 0,
                        `MobKills` INT NOT NULL DEFAULT 0,
                        `BossKills` INT NOT NULL DEFAULT 0,
                        `UpdatedAt` DATETIME NULL,
                        PRIMARY KEY (`user_id`),
                        KEY `idx_points` (`Points`)
                      ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                int existing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM `event_config` WHERE eventKey = @key", new { key = EVENT_KEY });
                if (existing == 0)
                {
                    conn.Execute(
                        "INSERT INTO `event_config` (eventKey, name, isEnabled, startTime, endTime) VALUES (@key, @name, 0, NULL, NULL)",
                        new { key = EVENT_KEY, name = this.Name });
                    EventConfigManager.Reload();
                }
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }

            BXHManager.listTop.Add(TopChallengeScore2026.Instance);
            // BẮT BUỘC: NpcTemplate.getOptionName() tra tên option qua NpcOptionLanguage theo optionId
            // cho TỪNG ngôn ngữ — thiếu sẽ crash KeyNotFoundException khi client mở menu NPC.
            foreach (var item1 in GopetManager.Language)
            {
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_CHALLENGE_2026] = item1.Value.TopChallengeScore2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_GUIDE_CHALLENGE_2026] = item1.Value.GuideChallenge2026Option;
            }
            // Gắn 2 option vào mọi NPC đang có option "Vượt ải" hoặc "Top vượt ải" (NPC Đấu Trường) — không cần sửa bảng npc.
            foreach (var npc in GopetManager.npcTemplate.Values)
            {
                if ((npc.optionId.Contains(MenuController.OP_CHALLENGE) || npc.optionId.Contains(MenuController.OP_SHOW_TOP_CHALLENGE)) && !npc.optionId.Contains(MenuController.OP_XEM_TOP_CHALLENGE_2026))
                {
                    npc.optionId = npc.optionId.Concat(new int[] { MenuController.OP_XEM_TOP_CHALLENGE_2026, MenuController.OP_GUIDE_CHALLENGE_2026 }).ToArray();
                }
            }
        }

        /// <summary>Bảng xếp hạng tổng điểm sự kiện Vượt Ải.</summary>
        public class TopChallengeScore2026 : Top
        {
            public static readonly TopChallengeScore2026 Instance = new TopChallengeScore2026();

            protected TopChallengeScore2026() : base("challenge.2026.score.top")
            {
                this.name = "TOP Điểm Vượt ải 2026";
            }

            /// <summary>Thời điểm (ms) lần nạp bảng gần nhất — để getMyInfo tự làm mới khi bảng quá cũ.</summary>
            long lastRefreshMs = 0;

            /// <summary>
            /// BXHManager chỉ nạp các bảng mỗi 5 phút và lần nạp đầu chạy TRƯỚC khi bảng này được đăng ký
            /// (Init chạy ở luồng Event) nên vừa khởi động xong bảng trống tới 5 phút. showTop() luôn gọi
            /// getMyInfo() đầu tiên nên nạp lại ở đây nếu bảng cũ quá 30 giây — người chơi thấy điểm gần như
            /// ngay mà không tốn query mỗi lần bấm (tối đa 1 query/30 giây cho cả server).
            /// </summary>
            void RefreshIfStale()
            {
                if (Utilities.CurrentTimeMillis - lastRefreshMs > 30000)
                {
                    Update();
                }
            }

            public override TopData getMyInfo(Player player)
            {
                RefreshIfStale();
                var findTop = datas.Where(p => p.id == player.playerData.user_id);
                if (findTop.Any())
                {
                    return findTop.First();
                }
                long points = 0;
                try
                {
                    using var conn = MYSQLManager.create();
                    points = conn.ExecuteScalar<long?>("SELECT Points FROM `event_challenge_score` WHERE user_id = @id", new { id = player.playerData.user_id }) ?? 0;
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
                TopData topData = new TopData();
                topData.id = player.playerData.user_id;
                topData.name = player.playerData.name;
                topData.imgPath = player.playerData.avatarPath;
                topData.title = topData.name;
                topData.desc = $"Hạng chưa có. Bạn đang có {Utilities.FormatNumber(points)} điểm.";
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
                    using (var conn = MYSQLManager.create())
                    {
                        var rows = conn.Query(
                            @"SELECT s.user_id, s.name, p.avatarPath, s.Points, s.MobKills, s.BossKills
                              FROM `event_challenge_score` s LEFT JOIN `player` p ON p.user_id = s.user_id
                              WHERE s.Points > 0 AND (p.isAdmin = 0 OR p.isAdmin IS NULL)
                              ORDER BY s.Points DESC, s.UpdatedAt ASC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in rows)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. {Utilities.FormatNumber(data.Points)} điểm ({data.MobKills} quái, {data.BossKills} boss).";
                            datas.Add(topData);
                            index++;
                        }
                    }
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
            }
        }
    }
}
