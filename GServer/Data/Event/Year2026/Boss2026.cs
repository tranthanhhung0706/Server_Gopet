using Dapper;
using Gopet.Data.Collections;
using Gopet.Data.GopetItem;
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

        /// <summary>ID "Hộp quà thường" — mua ở SHOP_BOSS_2026 bằng 10 Hoa Ngọc hoặc 20.000 Ngọc.</summary>
        public const int ID_GIFT_BOX_NORMAL = 240024;
        /// <summary>ID "Hộp quà VIP" — mua ở SHOP_BOSS_2026 bằng 20 Hoa Ngọc hoặc 2.000 Vàng.</summary>
        public const int ID_GIFT_BOX_VIP = 240025;

        protected Boss2026()
        {
            this.Name = "Sự kiện săn boss 2026";
        }

        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY);

        public override bool NeedRemove => false;

        public override int[] ItemsOfEvent => new int[] { ID_GIFT_BOX_NORMAL, ID_GIFT_BOX_VIP };

        /// <summary>
        /// Mở hộp quà — trừ 1 hộp, tặng ngẫu nhiên 1 vật phẩm theo ItemTemplate.giftData của CHÍNH
        /// item hộp đó (cột `giftData` bảng `item`, sửa qua trang admin Item) — KHÔNG hardcode
        /// trong code nữa. Giống hệt cấu trúc UseEventItem() của GameBirthdayEvent cho
        /// ID_RANDOM_EVENT_BOX, chỉ khác nguồn dữ liệu loot.
        /// </summary>
        public override void UseItem(int itemId, Player player)
        {
            if (itemId != ID_GIFT_BOX_NORMAL && itemId != ID_GIFT_BOX_VIP)
            {
                return;
            }
            if (!GopetManager.itemTemplate.ContainsKey(itemId) || GopetManager.itemTemplate.get(itemId).giftData.Length == 0)
            {
                player.redDialog("Hộp quà này chưa được cấu hình danh sách vật phẩm, vui lòng báo admin");
                return;
            }
            Item item = player.controller.selectItemsbytemp(itemId, GopetManager.NORMAL_INVENTORY);
            if (item == null || !GameController.checkCount(item, 1))
            {
                return;
            }
            player.controller.subCountItem(item, 1, GopetManager.NORMAL_INVENTORY);
            JArrayList<Popup> popups = player.controller.onReiceiveGift(GopetManager.itemTemplate.get(itemId).giftData);
            JArrayList<String> textInfo = new();
            foreach (Popup popup in popups)
            {
                textInfo.add(popup.getText());
            }
            player.okDialog(string.Format(player.Language.GetGiftCodeOK, String.Join(",", textInfo)));
        }

        /// <summary>
        /// Mở NHIỀU hộp cùng lúc (client cho nhập số lượng ở màn Rương đồ) — trừ đúng
        /// <paramref name="count"/> hộp trong 1 lần, mở lần lượt lấy loot rồi gộp TẤT CẢ vật phẩm
        /// nhận được vào 1 dialog dạng danh sách (mỗi dòng 1 vật phẩm), thay vì client tự gửi
        /// nhiều gói UseItem riêng lẻ (trước đây mỗi gói ra 1 dialog đè lên nhau, chỉ thấy dialog
        /// cuối/1 vật phẩm dù mở nhiều hộp).
        /// </summary>
        public override void UseItemCount(int itemId, Player player, int count)
        {
            if (itemId != ID_GIFT_BOX_NORMAL && itemId != ID_GIFT_BOX_VIP)
            {
                return;
            }
            if (!GopetManager.itemTemplate.ContainsKey(itemId) || GopetManager.itemTemplate.get(itemId).giftData.Length == 0)
            {
                player.redDialog("Hộp quà này chưa được cấu hình danh sách vật phẩm, vui lòng báo admin");
                return;
            }
            Item item = player.controller.selectItemsbytemp(itemId, GopetManager.NORMAL_INVENTORY);
            if (item == null || !GameController.checkCount(item, count))
            {
                player.redDialog("Bạn không có đủ số lượng hộp để mở.");
                return;
            }
            player.controller.subCountItem(item, count, GopetManager.NORMAL_INVENTORY);
            JArrayList<String> textInfo = new();
            for (int i = 0; i < count; i++)
            {
                JArrayList<Popup> popups = player.controller.onReiceiveGift(GopetManager.itemTemplate.get(itemId).giftData);
                foreach (Popup popup in popups)
                {
                    textInfo.add(popup.getText());
                }
            }
            player.okDialog(string.Format(player.Language.GetGiftCodeOK, "\n" + String.Join("\n", textInfo)));
        }

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
                item1.Value.NpcOptionLanguage[MenuController.OP_SHOW_SHOP_GIFT_BOX_2026] = item1.Value.ShopGiftBoxBoss2026Option;
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
