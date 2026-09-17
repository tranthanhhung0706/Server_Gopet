using Gopet.Data.Collections;
using Gopet.Data.Map;
using Gopet.Data.Mob;
using Gopet.Manager;
using Gopet.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Vòng lặp hồi sinh Boss Trung Thu (bảng `boss`, typeBoss = 2) — sự kiện Trung Thu 2026.
    ///
    /// KHÔNG hardcode bossId nào — mỗi lần Update() quét TOÀN BỘ GopetManager.boss tìm các dòng có
    /// typeBoss = 2, mỗi dòng tự chạy đợt hồi riêng theo đúng bossId đó (admin có thể tạo nhiều Boss
    /// Trung Thu khác nhau qua trang Boss, mỗi con có map/skill riêng, đều được xử lý tự động).
    ///
    /// 2 CÁCH LÊN LỊCH hồi, tuỳ admin chọn cấu hình cột nào (KHÔNG hardcode số/giờ/khoảng thời gian
    /// nào trong code nữa):
    /// - SummonIntervalMinutes > 0: hồi lặp lại đều đặn mỗi ngần ấy phút suốt cả ngày (vd = 30 thì
    ///   30 phút hồi 1 lần, y hệt yêu cầu ban đầu nhưng số phút do admin tự đặt) — bỏ qua HourSummon.
    /// - SummonIntervalMinutes = 0 (mặc định): dùng CHÍNH cột HourSummon của dòng boss đó (giống
    ///   cách typeBoss=4/DailyBossEvent dùng HourSummon — vd [8,20] = chỉ hồi đúng lúc 8h và 20h).
    /// Số lượng hồi mỗi lần luôn đọc từ cột SummonCount, rải ngẫu nhiên trên các map cấu hình ở cột
    /// BossMapSummon của dòng đó (BossMapSummon KHÔNG ghép cặp theo chỉ số với HourSummon như
    /// DailyBossEvent — ở đây là "tới lúc hồi thì rải SummonCount con trên TẤT CẢ map trong
    /// BossMapSummon"). Cách chọn map/chỗ trống để spawn giống hệt GameBirthdayEvent.SummonBossSchedule:
    /// thay chỗ 1 quái thường (không đang đánh nhau) bằng boss.
    ///
    /// Boss loại này không cho EXP — thật ra KHÔNG CẦN xử lý riêng gì cả, vì PetBattle.win() vốn đã
    /// không bao giờ cộng EXP khi hạ boss (mọi typeBoss), chỉ trao Gift theo cột `gift` như thường.
    ///
    /// Tung skill ngẫu nhiên thay vì chỉ đánh thường: xem Boss.cs (constructor gán Pet.skill từ
    /// BossTemplate.SkillIds) + PetBattle.mobAttack() (dùng lại pickUsableSkill()/mobUseSkill() sẵn
    /// có cho Arena) — không cần code gì thêm ở đây, chỉ cần admin điền cột SkillIds (vd [101,105,
    /// 107,111] = song kích/sấm sét/hạ độc/hút máu, các skillId này đã có sẵn trong bảng `skill`).
    /// </summary>
    public class BossTrungThu2026 : EventBase
    {
        public static readonly BossTrungThu2026 Instance = new BossTrungThu2026();

        /// <summary>Giá trị typeBoss riêng cho Boss Trung Thu — trước đây chưa dùng cho việc gì.</summary>
        public const sbyte TYPE_BOSS_TRUNG_THU = 2;

        /// <summary>Lần hồi gần nhất theo từng bossId — dùng để tính khoảng cách (chế độ
        /// SummonIntervalMinutes) hoặc chặn hồi lặp lại nhiều lần trong cùng 1 giờ (chế độ HourSummon).</summary>
        readonly Dictionary<int, DateTime> lastSummonTime = new();

        protected BossTrungThu2026()
        {
            this.Name = "Boss Trung Thu 2026";
        }

        /// <summary>Dùng chung công tắc bật/tắt với TrungThu2026 (eventKey "trungthu2026") — 1 sự kiện, 1 chỗ bật/tắt.</summary>
        public override bool Condition => EventConfigManager.IsActive(TrungThu2026.EVENT_KEY);

        public override bool NeedRemove => false;

        public override void Update()
        {
            foreach (BossTemplate bossTemplate in GopetManager.boss.Values.Where(b => b.typeBoss == TYPE_BOSS_TRUNG_THU))
            {
                if (bossTemplate.BossMapSummon == null || bossTemplate.BossMapSummon.Length == 0)
                {
                    continue;
                }
                if (bossTemplate.SummonCount <= 0)
                {
                    continue;
                }
                DateTime last = lastSummonTime.TryGetValue(bossTemplate.bossId, out DateTime t) ? t : DateTime.MinValue;
                bool shouldSummon;
                if (bossTemplate.SummonIntervalMinutes > 0)
                {
                    // Chế độ lặp đều đặn (vd mỗi 30 phút) — bỏ qua HourSummon hoàn toàn.
                    shouldSummon = DateTime.Now - last >= TimeSpan.FromMinutes(bossTemplate.SummonIntervalMinutes);
                }
                else
                {
                    // Chế độ giờ cố định trong ngày — chỉ hồi đúng những giờ liệt kê trong HourSummon,
                    // và chỉ 1 lần cho mỗi giờ đó (tránh Update() chạy mỗi giây lại hồi lặp lại).
                    bool isSummonHour = bossTemplate.HourSummon != null && bossTemplate.HourSummon.Contains(DateTime.Now.Hour);
                    bool alreadySummonedThisHour = last.Date == DateTime.Now.Date && last.Hour == DateTime.Now.Hour;
                    shouldSummon = isSummonHour && !alreadySummonedThisHour;
                }
                if (!shouldSummon)
                {
                    continue;
                }
                lastSummonTime[bossTemplate.bossId] = DateTime.Now;
                SummonWave(bossTemplate);
            }
        }

        void SummonWave(BossTemplate bossTemplate)
        {
            var targetMaps = MapManager.mapArr.Where(m => bossTemplate.BossMapSummon.Contains(m.mapID)).ToArray();
            if (targetMaps.Length == 0)
            {
                return;
            }
            int summoned = 0;
            Dictionary<string, int> summonedByMap = new();
            DateTime deadline = DateTime.Now.AddSeconds(5);
            while (summoned < bossTemplate.SummonCount && DateTime.Now < deadline)
            {
                var map = Utilities.RandomArray(targetMaps);
                var place = (GopetPlace)Utilities.RandomArray(map.places);
                var mobs = place.mobs.Where(x => !(x is Boss) && !x.HasBattle);
                if (!mobs.Any())
                {
                    continue;
                }
                Mob.Mob mob = Utilities.RandomArray(mobs);
                place.mobs.remove(mob);
                // Phải báo client xoá hình quái cũ (sendRemoveMob) rồi báo có mob mới (sendMob) —
                // thiếu 2 dòng này thì client đang đứng sẵn trong map vẫn giữ hình quái cũ trên màn
                // hình (server đã xoá khỏi mobs nhưng chưa ai bảo client xoá theo), tạo cảm giác
                // "boss tách làm 2 con" khi con boss mới xuất hiện chồng lên đúng vị trí đó.
                place.sendRemoveMob(mob.getMobId());
                Boss boss = new Boss(bossTemplate.bossId, mob.getMobLocation());
                boss.isTimeOut = true;
                boss.TimeOut = DateTime.Now.AddMilliseconds(GopetManager.TIME_BOSS_DISPOINTED);
                place.addNewMob(boss);
                place.sendMob(new JArrayList<Mob.Mob>(new Mob.Mob[] { boss }));
                summoned++;
                string location = $"{place.map.mapTemplate.name} khu {place.zoneID}";
                summonedByMap[location] = summonedByMap.TryGetValue(location, out int count) ? count + 1 : 1;
            }
            if (summoned > 0)
            {
                string mapList = string.Join(", ", summonedByMap.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key));
                PlayerManager.showBannerZ(string.Format("{0} {1} vừa xuất hiện tại: {2}. Nhanh tay săn nào!", summoned, bossTemplate.name, mapList));
            }
        }
    }
}
