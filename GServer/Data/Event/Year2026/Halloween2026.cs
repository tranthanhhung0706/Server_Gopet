using Dapper;
using Gopet.Data.Collections;
using Gopet.Data.GopetItem;
using Gopet.Data.Map;
using Gopet.Manager;
using Gopet.Util;
using MySqlConnector;
using System;
using System.Linq;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Sự kiện Halloween 2026 — cơ chế y hệt Trung Thu 2026, chỉ khác vật phẩm.
    ///
    /// Gom 4 nguyên liệu farm được từ quái (Bí ngô, Kẹo, Xương, Mắt quái vật) + Ngọc (thêm Vàng cho bản VIP) để CHẾ TẠO
    /// Hộp quà halloween (thường/VIP) — xem CraftGiftBox(). Mở hộp theo ItemTemplate.giftData của chính item hộp
    /// (cột `giftData` bảng `item`, sửa qua trang admin Item). Mỗi lần mở cộng bộ đếm riêng cho từng loại hộp để xếp hạng
    /// và nhận quà mốc (bảng `halloween_milestone`).
    ///
    /// CÁCH CONFIG (giống Trung Thu):
    /// - Bật/tắt + khung giờ: bảng `event_config` (eventKey = "halloween2026") — trang Event của buff_gopet, áp dụng ngay.
    /// - Công thức chế tạo: bảng `halloween_recipe` — trang "Công thức Halloween" của buff_gopet (cần "Nạp lại danh mục").
    /// - Quà mốc: bảng `halloween_milestone` — trang "Quà mốc Halloween" của buff_gopet (áp dụng ngay).
    /// - Loot hộp: cột giftData của item 240037/240038 — trang Item.
    /// - Nguyên liệu rơi từ quái: bảng drop (trang Drop Item) với itemId 240033-240036.
    ///
    /// Lần đầu chạy server tự tạo bảng/cột/vật phẩm/NPC còn thiếu (EnsureSchema) nên không cần chạy SQL tay; sự kiện được
    /// tạo sẵn ở trạng thái TẮT trong event_config.
    /// </summary>
    public class Halloween2026 : EventBase
    {
        public static readonly Halloween2026 Instance = new Halloween2026();

        public const string EVENT_KEY = "halloween2026";

        /// <summary>NPC sự kiện Halloween — nơi chế tạo hộp quà (dòng npc được EnsureSchema tự thêm).</summary>
        public const int NPC_HALLOWEEN_2026 = -44;

        /// <summary>ID nguyên liệu "Bí ngô".</summary>
        public const int ID_PUMPKIN = 240033;
        /// <summary>ID nguyên liệu "Kẹo".</summary>
        public const int ID_CANDY = 240034;
        /// <summary>ID nguyên liệu "Xương".</summary>
        public const int ID_BONE = 240035;
        /// <summary>ID nguyên liệu "Mắt quái vật".</summary>
        public const int ID_MONSTER_EYE = 240036;
        /// <summary>ID "Hộp quà halloween thường".</summary>
        public const int ID_GIFT_BOX_NORMAL = 240037;
        /// <summary>ID "Hộp quà halloween VIP".</summary>
        public const int ID_GIFT_BOX_VIP = 240038;

        protected Halloween2026()
        {
            this.Name = "Sự kiện Halloween 2026";
        }

        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY);

        public override bool NeedRemove => false;

        public override int[] ItemsOfEvent => new int[] { ID_PUMPKIN, ID_CANDY, ID_BONE, ID_MONSTER_EYE, ID_GIFT_BOX_NORMAL, ID_GIFT_BOX_VIP };

        /// <summary>Danh sách (itemId nguyên liệu, số lượng cần) theo công thức hiện tại của 1 loại hộp.</summary>
        (int itemId, int need)[] GetMaterials(HalloweenRecipe recipe) => new (int, int)[]
        {
            (ID_PUMPKIN, recipe.PumpkinCount),
            (ID_CANDY, recipe.CandyCount),
            (ID_BONE, recipe.BoneCount),
            (ID_MONSTER_EYE, recipe.MonsterEyeCount),
        };

        static string GetMaterialName(int itemId) => itemId switch
        {
            ID_PUMPKIN => "Bí ngô",
            ID_CANDY => "Kẹo",
            ID_BONE => "Xương",
            ID_MONSTER_EYE => "Mắt quái vật",
            _ => "?",
        };

        /// <summary>Mô tả công thức (nguyên liệu + Ngọc/Vàng) để hiện lên popup xác nhận trước khi chế tạo.</summary>
        public string GetRecipeText(bool isVip)
        {
            int boxItemId = isVip ? ID_GIFT_BOX_VIP : ID_GIFT_BOX_NORMAL;
            if (!GopetManager.halloweenRecipe.ContainsKey(boxItemId))
            {
                return "Chưa cấu hình công thức, vui lòng báo admin";
            }
            HalloweenRecipe recipe = GopetManager.halloweenRecipe.get(boxItemId);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var (itemId, need) in GetMaterials(recipe))
            {
                if (need > 0)
                {
                    parts.Add($"{need} {GetMaterialName(itemId)}");
                }
            }
            if (recipe.CoinCost > 0)
            {
                parts.Add($"{Utilities.FormatNumber(recipe.CoinCost)} Ngọc");
            }
            if (recipe.GoldCost > 0)
            {
                parts.Add($"{Utilities.FormatNumber(recipe.GoldCost)} Vàng");
            }
            return String.Join(", ", parts);
        }

        /// <summary>Chế tạo hộp quà halloween — trừ nguyên liệu + tiền theo công thức admin cấu hình, cộng 1 hộp vào túi đồ.</summary>
        public void CraftGiftBox(Player player, bool isVip)
        {
            if (!Condition)
            {
                player.redDialog(player.Language.EventHadFinished);
                return;
            }
            int boxItemId = isVip ? ID_GIFT_BOX_VIP : ID_GIFT_BOX_NORMAL;
            if (!GopetManager.halloweenRecipe.ContainsKey(boxItemId))
            {
                player.redDialog("Công thức chế tạo chưa được cấu hình, vui lòng báo admin");
                return;
            }
            HalloweenRecipe recipe = GopetManager.halloweenRecipe.get(boxItemId);
            if (!MenuController.checkMoney(GopetManager.MONEY_TYPE_COIN, recipe.CoinCost, player))
            {
                MenuController.NotEngouhMoney(GopetManager.MONEY_TYPE_COIN, recipe.CoinCost, player);
                return;
            }
            if (recipe.GoldCost > 0 && !MenuController.checkMoney(GopetManager.MONEY_TYPE_GOLD, recipe.GoldCost, player))
            {
                MenuController.NotEngouhMoney(GopetManager.MONEY_TYPE_GOLD, recipe.GoldCost, player);
                return;
            }
            var materials = GetMaterials(recipe);
            var playerItems = new System.Collections.Generic.Dictionary<int, Item>();
            foreach (var (itemId, need) in materials)
            {
                if (need <= 0)
                {
                    continue;
                }
                Item item = player.controller.selectItemsbytemp(itemId, GopetManager.NORMAL_INVENTORY);
                if (item == null || !GameController.checkCount(item, need))
                {
                    player.redDialog(player.Language.NotEnoughMaterial);
                    return;
                }
                playerItems[itemId] = item;
            }
            MenuController.addMoney(GopetManager.MONEY_TYPE_COIN, -recipe.CoinCost, player);
            if (recipe.GoldCost > 0)
            {
                MenuController.addMoney(GopetManager.MONEY_TYPE_GOLD, -recipe.GoldCost, player);
            }
            foreach (var (itemId, need) in materials)
            {
                if (need > 0)
                {
                    player.controller.subCountItem(playerItems[itemId], need, GopetManager.NORMAL_INVENTORY);
                }
            }
            player.addItemToInventory(new Item(boxItemId, 1));
            player.okDialog(player.Language.HalloweenMakeBoxOK);
        }

        /// <summary>Nguyên liệu không dùng trực tiếp được, chỉ hộp quà mới mở được.</summary>
        public override void UseItem(int itemId, Player player)
        {
            switch (itemId)
            {
                case ID_GIFT_BOX_NORMAL:
                case ID_GIFT_BOX_VIP:
                    OpenOneGiftBox(itemId, player);
                    break;
                case ID_PUMPKIN:
                case ID_CANDY:
                case ID_BONE:
                case ID_MONSTER_EYE:
                    player.redDialog(player.Language.ThisEventItemIsMaterial);
                    break;
            }
        }

        void OpenOneGiftBox(int itemId, Player player)
        {
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
            IncreaseUseCounter(itemId, player, 1);
            JArrayList<Popup> popups = player.controller.onReiceiveGift(GopetManager.itemTemplate.get(itemId).giftData);
            JArrayList<String> textInfo = new();
            foreach (Popup popup in popups)
            {
                textInfo.add(popup.getText());
            }
            player.okDialog(string.Format(player.Language.GetGiftCodeOK, String.Join(",", textInfo)));
        }

        /// <summary>Mở NHIỀU hộp cùng lúc (client cho nhập số lượng ở màn Rương đồ).</summary>
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
            IncreaseUseCounter(itemId, player, count);
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

        void IncreaseUseCounter(int itemId, Player player, int count)
        {
            if (itemId == ID_GIFT_BOX_NORMAL)
            {
                player.playerData.NumUseHalloweenBoxNormal2026 += count;
            }
            else if (itemId == ID_GIFT_BOX_VIP)
            {
                player.playerData.NumUseHalloweenBoxVip2026 += count;
            }
        }

        /// <summary>
        /// Tự tạo những gì sự kiện cần mà còn thiếu trong DB (an toàn khi chạy lại): 2 bảng cấu hình, 2 cột đếm của bảng player,
        /// 6 vật phẩm (4 nguyên liệu + 2 hộp), NPC sự kiện, công thức mặc định. Gọi từ GopetManager TRƯỚC khi nạp bảng item/npc.
        /// Hộp quà nhân bản loot (giftData) từ hộp Trung Thu tương ứng nếu có — admin chỉnh lại qua trang Item.
        /// </summary>
        public static void EnsureSchema(MySqlConnection conn)
        {
            void Try(string what, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    GopetManager.ServerMonitor.LogError($"Halloween2026.EnsureSchema [{what}]: {e.Message}");
                }
            }

            Try("halloween_recipe", () => conn.Execute(
                @"CREATE TABLE IF NOT EXISTS `halloween_recipe` (
                    `recipeId` INT NOT NULL AUTO_INCREMENT,
                    `boxItemId` INT NOT NULL,
                    `name` VARCHAR(200) NOT NULL,
                    `pumpkinCount` INT NOT NULL DEFAULT 0,
                    `candyCount` INT NOT NULL DEFAULT 0,
                    `boneCount` INT NOT NULL DEFAULT 0,
                    `monsterEyeCount` INT NOT NULL DEFAULT 0,
                    `coinCost` BIGINT NOT NULL DEFAULT 0,
                    `goldCost` BIGINT NOT NULL DEFAULT 0,
                    PRIMARY KEY (`recipeId`),
                    UNIQUE KEY `boxItemId` (`boxItemId`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"));
            Try("halloween_milestone", () => conn.Execute(
                @"CREATE TABLE IF NOT EXISTS `halloween_milestone` (
                    `id` INT NOT NULL AUTO_INCREMENT,
                    `boxType` TINYINT NOT NULL COMMENT '0 = thường, 1 = VIP',
                    `name` VARCHAR(200) NOT NULL,
                    `threshold` INT NOT NULL,
                    `giftData` TEXT NOT NULL,
                    `usersOfUseThis` TEXT NOT NULL DEFAULT '[]',
                    PRIMARY KEY (`id`),
                    UNIQUE KEY `boxType_threshold` (`boxType`,`threshold`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"));
            Try("player.NumUseHalloweenBoxNormal2026", () => conn.Execute("ALTER TABLE `player` ADD COLUMN IF NOT EXISTS `NumUseHalloweenBoxNormal2026` INT NOT NULL DEFAULT 0"));
            Try("player.NumUseHalloweenBoxVip2026", () => conn.Execute("ALTER TABLE `player` ADD COLUMN IF NOT EXISTS `NumUseHalloweenBoxVip2026` INT NOT NULL DEFAULT 0"));

            // 4 nguyên liệu (type 24 = vật phẩm sự kiện, giống nguyên liệu Trung Thu)
            var materials = new (int id, string name)[]
            {
                (ID_PUMPKIN, "Bí ngô"), (ID_CANDY, "Kẹo"), (ID_BONE, "Xương"), (ID_MONSTER_EYE, "Mắt quái vật"),
            };
            foreach (var (id, name) in materials)
            {
                Try($"item {id}", () => conn.Execute(
                    @"INSERT IGNORE INTO `item` (itemId, name, description, type, iconPath, frameImgPath, isStackable)
                      VALUES (@id, @name, 'Nguyên liệu sự kiện Halloween 2026. Dùng để chế tạo Hộp quà halloween.', 24, @icon, @icon, 1)",
                    new { id, name, icon = $"items/{id}.png" }));
            }
            // 2 hộp quà: nhân bản loot từ hộp Trung Thu tương ứng (nếu có) rồi bổ sung dòng trống nếu chưa tạo được
            var boxes = new (int id, int cloneFrom, string name, string description)[]
            {
                (ID_GIFT_BOX_NORMAL, TrungThu2026.ID_GIFT_BOX_NORMAL, "Hộp quà thường (Halloween 2026)", "Chế tạo từ Bí ngô, Kẹo, Xương, Mắt quái vật + Ngọc. Mở ra nhận ngẫu nhiên 1 vật phẩm."),
                (ID_GIFT_BOX_VIP, TrungThu2026.ID_GIFT_BOX_VIP, "Hộp quà VIP (Halloween 2026)", "Chế tạo từ Bí ngô, Kẹo, Xương, Mắt quái vật (gấp đôi) + Ngọc + Vàng. Mở ra nhận ngẫu nhiên 1 vật phẩm."),
            };
            foreach (var (id, cloneFrom, name, description) in boxes)
            {
                Try($"item {id} (clone)", () => conn.Execute(
                    @"INSERT IGNORE INTO `item` (itemId, name, description, type, iconPath, frameImgPath, isStackable, giftData)
                      SELECT @id, @name, @description, type, @icon, @icon, isStackable, giftData FROM `item` WHERE itemId = @cloneFrom",
                    new { id, name, description, icon = $"items/{id}.png", cloneFrom }));
                Try($"item {id}", () => conn.Execute(
                    @"INSERT IGNORE INTO `item` (itemId, name, description, type, iconPath, frameImgPath, isStackable)
                      VALUES (@id, @name, @description, 24, @icon, @icon, 1)",
                    new { id, name, description, icon = $"items/{id}.png" }));
            }

            // Công thức mặc định (admin sửa lại qua trang Công thức Halloween)
            Try("recipe normal", () => conn.Execute(
                @"INSERT IGNORE INTO `halloween_recipe` (boxItemId, name, pumpkinCount, candyCount, boneCount, monsterEyeCount, coinCost, goldCost)
                  VALUES (@id, 'Hộp quà halloween thường', 2, 2, 1, 1, 50000, 0)", new { id = ID_GIFT_BOX_NORMAL }));
            Try("recipe vip", () => conn.Execute(
                @"INSERT IGNORE INTO `halloween_recipe` (boxItemId, name, pumpkinCount, candyCount, boneCount, monsterEyeCount, coinCost, goldCost)
                  VALUES (@id, 'Hộp quà halloween VIP', 4, 4, 2, 2, 100000, 10000)", new { id = ID_GIFT_BOX_VIP }));

            // NPC sự kiện: option 115..121 (xem MenuController.OP_*_HALLOWEEN_2026)
            Try("npc", () => conn.Execute(
                @"INSERT IGNORE INTO `npc` (npcId, name, chat, optionName, optionId, x, y, imgPath, type, bounds)
                  VALUES (@id, 'Phù Thủy Halloween', @chat, @optionName, '[115,116,117,118,119,120,121]', 262, 268, 'npcs/axgru.png', 1, '[-25,-25,50,50]')",
                new
                {
                    id = NPC_HALLOWEEN_2026,
                    chat = "[\"Ghé qua chế tạo hộp quà halloween nhé!\"]",
                    optionName = "[\"Chế tạo hộp quà halloween thường\",\"Chế tạo hộp quà halloween VIP\",\"Top dùng hộp halloween thường\",\"Top dùng hộp halloween VIP\",\"Hướng dẫn sự kiện\",\"Nhận quà mốc hộp thường\",\"Nhận quà mốc hộp VIP\"]",
                }));
        }

        /// <summary>
        /// Đăng ký tên hiển thị 7 option NPC + 2 bảng xếp hạng, thêm NPC vào map Thành Phố Linh Thú, tạo dòng event_config (TẮT)
        /// nếu chưa có — chạy ĐÚNG 1 LẦN lúc khởi động, KHÔNG phụ thuộc Condition (giống TrungThu2026).
        /// </summary>
        public override void Init()
        {
            try
            {
                using var conn = MYSQLManager.create();
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

            BXHManager.listTop.Add(TopUseGiftBoxNormal2026.Instance);
            BXHManager.listTop.Add(TopUseGiftBoxVip2026.Instance);
            // BẮT BUỘC có tên option cho TỪNG ngôn ngữ, thiếu sẽ crash KeyNotFoundException khi mở menu NPC.
            foreach (var item1 in GopetManager.Language)
            {
                item1.Value.NpcOptionLanguage[MenuController.OP_CRAFT_BOX_NORMAL_HALLOWEEN_2026] = item1.Value.CraftBoxNormalHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_CRAFT_BOX_VIP_HALLOWEEN_2026] = item1.Value.CraftBoxVipHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_BOX_NORMAL_HALLOWEEN_2026] = item1.Value.TopBoxNormalHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_BOX_VIP_HALLOWEEN_2026] = item1.Value.TopBoxVipHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_GUIDE_HALLOWEEN_2026] = item1.Value.GuideHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_NHAN_QUA_MOC_BOX_NORMAL_HALLOWEEN_2026] = item1.Value.NhanQuaMocBoxNormalHalloween2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_NHAN_QUA_MOC_BOX_VIP_HALLOWEEN_2026] = item1.Value.NhanQuaMocBoxVipHalloween2026Option;
            }
            MapTemplate mapTemplate = GopetManager.mapTemplate[MapTemplate.THÀNH_PHỐ_LINH_THÚ];
            if (!mapTemplate.npc.Contains(NPC_HALLOWEEN_2026) && GopetManager.npcTemplate.ContainsKey(NPC_HALLOWEEN_2026))
            {
                mapTemplate.npc = mapTemplate.npc.Concat(new int[] { NPC_HALLOWEEN_2026 }).ToArray();
            }
        }

        /// <summary>Bảng xếp hạng số lần dùng Hộp quà halloween THƯỜNG.</summary>
        public class TopUseGiftBoxNormal2026 : Top
        {
            public static readonly TopUseGiftBoxNormal2026 Instance = new TopUseGiftBoxNormal2026();

            protected TopUseGiftBoxNormal2026() : base("halloween.2026.box.normal.top")
            {
                this.name = "TOP Dùng Hộp Quà Halloween Thường 2026";
            }

            public override string HrImagePath => $"items/{ID_GIFT_BOX_NORMAL}.png";

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
                topData.desc = $"Hạng chưa có. Bạn đã dùng {Utilities.FormatNumber(player.playerData.NumUseHalloweenBoxNormal2026)} hộp quà thường.";
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
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, NumUseHalloweenBoxNormal2026 FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`NumUseHalloweenBoxNormal2026` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. Đã dùng {Utilities.FormatNumber(data.NumUseHalloweenBoxNormal2026)} hộp quà thường.";
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

        /// <summary>Bảng xếp hạng số lần dùng Hộp quà halloween VIP.</summary>
        public class TopUseGiftBoxVip2026 : Top
        {
            public static readonly TopUseGiftBoxVip2026 Instance = new TopUseGiftBoxVip2026();

            protected TopUseGiftBoxVip2026() : base("halloween.2026.box.vip.top")
            {
                this.name = "TOP Dùng Hộp Quà Halloween VIP 2026";
            }

            public override string HrImagePath => $"items/{ID_GIFT_BOX_VIP}.png";

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
                topData.desc = $"Hạng chưa có. Bạn đã dùng {Utilities.FormatNumber(player.playerData.NumUseHalloweenBoxVip2026)} hộp quà VIP.";
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
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, NumUseHalloweenBoxVip2026 FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`NumUseHalloweenBoxVip2026` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. Đã dùng {Utilities.FormatNumber(data.NumUseHalloweenBoxVip2026)} hộp quà VIP.";
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

    /// <summary>1 dòng công thức chế tạo hộp quà Halloween 2026 (bảng `halloween_recipe`), khoá theo BoxItemId.</summary>
    public class HalloweenRecipe
    {
        public int RecipeId { get; set; }
        public int BoxItemId { get; set; }
        public string Name { get; set; } = "";
        public int PumpkinCount { get; set; }
        public int CandyCount { get; set; }
        public int BoneCount { get; set; }
        public int MonsterEyeCount { get; set; }
        public long CoinCost { get; set; }
        public long GoldCost { get; set; }
    }
}
