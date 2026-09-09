using Dapper;
using Gopet.Manager;
using Gopet.Util;
using System;
using System.Linq;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Sự kiện săn boss 2026.
    ///
    /// Cơ chế: boss THƯỜNG (không phải boss riêng cho sự kiện) được admin cấu hình thêm 1 phần tử
    /// [GopetManager.GIFT_FLOWER_COIN_BOSS, số lượng] vào cột `gift` của bảng `boss` (qua trang
    /// admin Boss) — vd boss cấp 10 thêm [16,10] vào gift để ai hạ được (đòn kết liễu, theo đúng
    /// luật boss chung) nhận 10 Hoa Ngọc. GameController.onReiceiveGift chỉ thật sự cộng điểm khi
    /// EventConfigManager.IsActive("boss2026") — đóng sự kiện qua trang admin thì boss vẫn có thể
    /// bị giết nhưng KHÔNG cộng Hoa Ngọc nữa.
    ///
    /// Hoa Ngọc có 2 field tách biệt (xem PlayerData.cs):
    /// - FlowerCoin: số dư TIÊU ĐƯỢC, dùng chung MONEY_TYPE_FLOWER_COIN — giảm khi mua đồ ở
    ///   SHOP_BOSS_2026 (qua checkMoney/addMoney sẵn có, không cần code riêng cho việc mua).
    /// - NumBossFlowerCoin2026: TỔNG điểm cả đời, chỉ tăng (mỗi lần giết boss), dùng để xếp hạng
    ///   qua TopBossFlowerCoin2026 — KHÔNG bị trừ khi mua đồ.
    ///
    /// Bật/tắt + khung giờ sự kiện đọc từ bảng `event_config` (eventKey = "boss2026") qua
    /// EventConfigManager — sửa qua trang admin Event, áp dụng ngay không cần build lại/restart.
    /// NPC riêng "Thợ Săn Boss" (NpcTemplate.THỢ_SĂN_BOSS_2026, map Thành Phố Linh Thú) có sẵn
    /// optionId/optionName baked trong bảng npc, luôn hiển thị — Init() chỉ cần đăng ký tên hiển thị
    /// (NpcOptionLanguage) và bảng xếp hạng, không cần chờ Condition.
    /// </summary>
    public class Boss2026 : EventBase
    {
        public static readonly Boss2026 Instance = new Boss2026();

        public const string EVENT_KEY = "boss2026";

        protected Boss2026()
        {
            this.Name = "Sự kiện săn boss 2026";
        }

        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY);

        public override bool NeedRemove => false;

        /// <summary>
        /// Đăng ký tên hiển thị 2 option của NPC "Thợ Săn Boss" + bảng xếp hạng — chạy ĐÚNG 1 LẦN
        /// lúc GServer khởi động, KHÔNG phụ thuộc Condition (vì Condition giờ đổi được bất cứ lúc
        /// nào qua trang admin, nếu gate ở đây thì bật sự kiện sau này sẽ không có tên option/bảng
        /// xếp hạng do Init() đã chạy xong từ trước với Condition=false).
        /// </summary>
        public override void Init()
        {
            BXHManager.listTop.Add(TopBossFlowerCoin2026.Instance);
            BXHManager.listTop.Add(TopFlowerCoinBalance2026.Instance);
            // BẮT BUỘC: NpcTemplate.getOptionName() tra tên option qua NpcOptionLanguage theo
            // optionId cho TỪNG ngôn ngữ (không đọc trực tiếp mảng optionName của bảng npc) — thiếu
            // bước này sẽ crash KeyNotFoundException ngay khi client mở menu NPC.
            foreach (var item1 in GopetManager.Language)
            {
                item1.Value.NpcOptionLanguage[MenuController.OP_SHOW_SHOP_BOSS_2026] = item1.Value.ShopBoss2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_BOSS_2026] = item1.Value.TopBoss2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_FLOWER_COIN_2026] = item1.Value.TopFlowerCoinBoss2026Option;
            }
        }

        /// <summary>
        /// Bảng xếp hạng theo TỔNG Hoa Ngọc kiếm được (NumBossFlowerCoin2026) — không phải số dư
        /// hiện có (FlowerCoin), nên mua đồ tiêu Hoa Ngọc không làm tụt hạng.
        /// </summary>
        public class TopBossFlowerCoin2026 : Top
        {
            public static readonly TopBossFlowerCoin2026 Instance = new TopBossFlowerCoin2026();

            protected TopBossFlowerCoin2026() : base("boss.2026.flowercoin.top")
            {
                this.name = "TOP Săn Boss - Hoa Ngọc 2026";
            }

            public override string HrImagePath => "items/240018.png";

            public override TopData getMyInfo(Player player)
            {
                var findTop = datas.Where(p => p.id == player.playerData.user_id);
                if (findTop.Any())
                {
                    return findTop.First();
                }
                TopData topData = new TopData();
                topData.id = player.playerData.user_id;
                topData.name = player.playerData.name;
                topData.imgPath = player.playerData.avatarPath;
                topData.title = topData.name;
                topData.desc = $"Hạng chưa có. Bạn đang có {Utilities.FormatNumber(player.playerData.NumBossFlowerCoin2026)} Hoa Ngọc.";
                return topData;
            }

            public override void Update()
            {
                try
                {
                    lastDatas.Clear();
                    lastDatas.AddRange(datas);
                    datas.Clear();
                    using (var conn = MYSQLManager.create())
                    {
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, NumBossFlowerCoin2026 FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`NumBossFlowerCoin2026` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. {Utilities.FormatNumber(data.NumBossFlowerCoin2026)} Hoa Ngọc.";
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

        /// <summary>
        /// Bảng xếp hạng theo số dư Hoa Ngọc ĐANG CÓ (FlowerCoin) — giảm khi tiêu mua đồ ở
        /// SHOP_BOSS_2026, khác TopBossFlowerCoin2026 (tổng điểm cả đời, không giảm).
        /// </summary>
        public class TopFlowerCoinBalance2026 : Top
        {
            public static readonly TopFlowerCoinBalance2026 Instance = new TopFlowerCoinBalance2026();

            protected TopFlowerCoinBalance2026() : base("boss.2026.flowercoin.balance.top")
            {
                this.name = "TOP Số Dư Hoa Ngọc 2026";
            }

            public override string HrImagePath => "items/240018.png";

            public override TopData getMyInfo(Player player)
            {
                var findTop = datas.Where(p => p.id == player.playerData.user_id);
                if (findTop.Any())
                {
                    return findTop.First();
                }
                TopData topData = new TopData();
                topData.id = player.playerData.user_id;
                topData.name = player.playerData.name;
                topData.imgPath = player.playerData.avatarPath;
                topData.title = topData.name;
                topData.desc = $"Hạng chưa có. Bạn đang có {Utilities.FormatNumber(Math.Max(0, player.playerData.FlowerCoin))} Hoa Ngọc.";
                return topData;
            }

            public override void Update()
            {
                try
                {
                    lastDatas.Clear();
                    lastDatas.AddRange(datas);
                    datas.Clear();
                    using (var conn = MYSQLManager.create())
                    {
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, FlowerCoin FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`FlowerCoin` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. {Utilities.FormatNumber(Math.Max(0, (int)data.FlowerCoin))} Hoa Ngọc.";
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
