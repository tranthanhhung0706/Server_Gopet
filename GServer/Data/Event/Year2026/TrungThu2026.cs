using Dapper;
using Gopet.Data.Collections;
using Gopet.Data.GopetItem;
using Gopet.Data.Map;
using Gopet.Manager;
using Gopet.Util;
using System;
using System.Linq;

namespace Gopet.Data.Event.Year2026
{
    /// <summary>
    /// Sự kiện Trung Thu 2026.
    ///
    /// Cơ chế: gom 5 nguyên liệu (Bột mì, Trứng, Đậu xanh, Hạt sen, Bánh trung thu) + Ngọc (và
    /// thêm Vàng cho bản VIP) để CHẾ TẠO ra Hộp quà trung thu (thường/VIP) — xem CraftGiftBox().
    /// Mở hộp quà dùng lại ĐÚNG cơ chế của Boss2026 (UseItem/UseItemCount đọc
    /// ItemTemplate.giftData của chính item hộp đó, cột `giftData` bảng `item`, admin cấu hình qua
    /// trang Item — KHÔNG hardcode loot trong code).
    ///
    /// Công thức (xem CraftGiftBox):
    /// - Hộp THƯỜNG: x2 Bột mì, x1 Trứng, x1 Đậu xanh, x1 Hạt sen, x1 Bánh trung thu + 50.000 Ngọc.
    /// - Hộp VIP: gấp đôi công thức hộp thường (x4/x2/x2/x2/x2) + 100.000 Ngọc + thêm 10.000 Vàng.
    ///
    /// Bật/tắt + khung giờ sự kiện đọc từ bảng `event_config` (eventKey = "trungthu2026") qua
    /// EventConfigManager — sửa qua trang admin Event, áp dụng ngay không cần build lại/restart.
    ///
    /// NPC riêng cho sự kiện (NPC_TRUNG_THU_2026, map Thành Phố Linh Thú) được thêm vào map ngay
    /// trong Init() (giống GameBirthdayEvent) — CẦN admin insert 1 dòng vào bảng `npc` với npcId =
    /// NPC_TRUNG_THU_2026 và optionId chứa 4 optionId của sự kiện này (xem hướng dẫn kèm theo khi
    /// bàn giao) rồi restart GServer 1 lần để nạp NpcTemplate.
    /// </summary>
    public class TrungThu2026 : EventBase
    {
        public static readonly TrungThu2026 Instance = new TrungThu2026();

        public const string EVENT_KEY = "trungthu2026";

        /// <summary>NPC "Chú Cuội" — nơi chế tạo hộp quà trung thu. Cần admin tạo dòng npc tương ứng.</summary>
        public const int NPC_TRUNG_THU_2026 = -43;

        /// <summary>ID nguyên liệu "Bột mì".</summary>
        public const int ID_FLOUR = 240026;
        /// <summary>ID nguyên liệu "Trứng".</summary>
        public const int ID_EGG = 240027;
        /// <summary>ID nguyên liệu "Đậu xanh".</summary>
        public const int ID_MUNG_BEAN = 240028;
        /// <summary>ID nguyên liệu "Hạt sen".</summary>
        public const int ID_LOTUS_SEED = 240029;
        /// <summary>ID nguyên liệu "Bánh trung thu".</summary>
        public const int ID_MOONCAKE = 240030;
        /// <summary>ID "Hộp quà trung thu thường" — chế tạo từ 5 nguyên liệu + 50.000 Ngọc.</summary>
        public const int ID_GIFT_BOX_NORMAL = 240031;
        /// <summary>ID "Hộp quà trung thu VIP" — chế tạo từ x2 nguyên liệu + 100.000 Ngọc + 10.000 Vàng.</summary>
        public const int ID_GIFT_BOX_VIP = 240032;

        protected TrungThu2026()
        {
            this.Name = "Sự kiện Trung Thu 2026";
        }

        public override bool Condition => EventConfigManager.IsActive(EVENT_KEY);

        public override bool NeedRemove => false;

        public override int[] ItemsOfEvent => new int[] { ID_FLOUR, ID_EGG, ID_MUNG_BEAN, ID_LOTUS_SEED, ID_MOONCAKE, ID_GIFT_BOX_NORMAL, ID_GIFT_BOX_VIP };

        /// <summary>Danh sách (itemId nguyên liệu, số lượng cần) theo công thức hiện tại của 1 loại hộp.</summary>
        (int itemId, int need)[] GetMaterials(TrungThuRecipe recipe) => new (int, int)[]
        {
            (ID_FLOUR, recipe.FlourCount),
            (ID_EGG, recipe.EggCount),
            (ID_MUNG_BEAN, recipe.MungBeanCount),
            (ID_LOTUS_SEED, recipe.LotusSeedCount),
            (ID_MOONCAKE, recipe.MoonCakeCount),
        };

        /// <summary>Tên nguyên liệu hiển thị, dùng chung cho cả GetRecipeText lẫn log/thông báo.</summary>
        static string GetMaterialName(int itemId) => itemId switch
        {
            ID_FLOUR => "Bột mì",
            ID_EGG => "Trứng",
            ID_MUNG_BEAN => "Đậu xanh",
            ID_LOTUS_SEED => "Hạt sen",
            ID_MOONCAKE => "Bánh trung thu",
            _ => "?",
        };

        /// <summary>
        /// Mô tả công thức chế tạo (nguyên liệu + Ngọc/Vàng cần) để hiện lên popup xác nhận trước
        /// khi chế tạo — đọc trực tiếp từ GopetManager.trungThuRecipe (admin cấu hình qua buff_gopet).
        /// </summary>
        public string GetRecipeText(bool isVip)
        {
            int boxItemId = isVip ? ID_GIFT_BOX_VIP : ID_GIFT_BOX_NORMAL;
            if (!GopetManager.trungThuRecipe.ContainsKey(boxItemId))
            {
                return "Chưa cấu hình công thức, vui lòng báo admin";
            }
            TrungThuRecipe recipe = GopetManager.trungThuRecipe.get(boxItemId);
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

        /// <summary>
        /// Chế tạo hộp quà trung thu — trừ nguyên liệu + tiền theo công thức admin cấu hình
        /// (GopetManager.trungThuRecipe), cộng 1 hộp vào túi đồ.
        /// </summary>
        /// <param name="isVip">false = hộp thường, true = hộp VIP</param>
        public void CraftGiftBox(Player player, bool isVip)
        {
            if (!Condition)
            {
                player.redDialog(player.Language.EventHadFinished);
                return;
            }
            int boxItemId = isVip ? ID_GIFT_BOX_VIP : ID_GIFT_BOX_NORMAL;
            if (!GopetManager.trungThuRecipe.ContainsKey(boxItemId))
            {
                player.redDialog("Công thức chế tạo chưa được cấu hình, vui lòng báo admin");
                return;
            }
            TrungThuRecipe recipe = GopetManager.trungThuRecipe.get(boxItemId);
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
            player.okDialog(player.Language.MakeCakeOK);
        }

        /// <summary>
        /// Vật phẩm nguyên liệu không dùng trực tiếp được, chỉ hộp quà mới mở được (giống hệt cấu
        /// trúc UseItem của Boss2026, chỉ khác nguồn item + cộng thêm bộ đếm riêng cho từng loại
        /// hộp phục vụ 2 bảng xếp hạng tách biệt).
        /// </summary>
        public override void UseItem(int itemId, Player player)
        {
            switch (itemId)
            {
                case ID_GIFT_BOX_NORMAL:
                case ID_GIFT_BOX_VIP:
                    OpenOneGiftBox(itemId, player);
                    break;
                case ID_FLOUR:
                case ID_EGG:
                case ID_MUNG_BEAN:
                case ID_LOTUS_SEED:
                case ID_MOONCAKE:
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

        /// <summary>
        /// Mở NHIỀU hộp cùng lúc (client cho nhập số lượng ở màn Rương đồ) — giống hệt
        /// UseItemCount của Boss2026.
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
                player.playerData.NumUseMoonCakeBoxNormal2026 += count;
            }
            else if (itemId == ID_GIFT_BOX_VIP)
            {
                player.playerData.NumUseMoonCakeBoxVip2026 += count;
            }
        }

        /// <summary>
        /// Đăng ký tên hiển thị 5 option của NPC sự kiện + 2 bảng xếp hạng, cộng thêm NPC vào map
        /// Thành Phố Linh Thú — chạy ĐÚNG 1 LẦN lúc GServer khởi động, KHÔNG phụ thuộc Condition
        /// (giống Boss2026, để bật sự kiện sau này qua trang admin không bị thiếu tên option/bảng
        /// xếp hạng do Init() đã chạy xong từ trước).
        /// </summary>
        public override void Init()
        {
            BXHManager.listTop.Add(TopUseGiftBoxNormal2026.Instance);
            BXHManager.listTop.Add(TopUseGiftBoxVip2026.Instance);
            foreach (var item1 in GopetManager.Language)
            {
                item1.Value.NpcOptionLanguage[MenuController.OP_CRAFT_BOX_NORMAL_TRUNG_THU_2026] = item1.Value.CraftBoxNormalTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_CRAFT_BOX_VIP_TRUNG_THU_2026] = item1.Value.CraftBoxVipTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_BOX_NORMAL_TRUNG_THU_2026] = item1.Value.TopBoxNormalTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_XEM_TOP_BOX_VIP_TRUNG_THU_2026] = item1.Value.TopBoxVipTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_GUIDE_TRUNG_THU_2026] = item1.Value.GuideTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_NHAN_QUA_MOC_BOX_NORMAL_TRUNG_THU_2026] = item1.Value.NhanQuaMocBoxNormalTrungThu2026Option;
                item1.Value.NpcOptionLanguage[MenuController.OP_NHAN_QUA_MOC_BOX_VIP_TRUNG_THU_2026] = item1.Value.NhanQuaMocBoxVipTrungThu2026Option;
            }
            MapTemplate mapTemplate = GopetManager.mapTemplate[MapTemplate.THÀNH_PHỐ_LINH_THÚ];
            if (!mapTemplate.npc.Contains(NPC_TRUNG_THU_2026))
            {
                mapTemplate.npc = mapTemplate.npc.Concat(new int[] { NPC_TRUNG_THU_2026 }).ToArray();
            }
        }

        /// <summary>Bảng xếp hạng số lần dùng Hộp quà trung thu THƯỜNG.</summary>
        public class TopUseGiftBoxNormal2026 : Top
        {
            public static readonly TopUseGiftBoxNormal2026 Instance = new TopUseGiftBoxNormal2026();

            protected TopUseGiftBoxNormal2026() : base("trungthu.2026.box.normal.top")
            {
                this.name = "TOP Dùng Hộp Bánh Trung Thu Thường 2026";
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
                topData.desc = $"Hạng chưa có. Bạn đã dùng {Utilities.FormatNumber(player.playerData.NumUseMoonCakeBoxNormal2026)} hộp quà thường.";
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
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, NumUseMoonCakeBoxNormal2026 FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`NumUseMoonCakeBoxNormal2026` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. Đã dùng {Utilities.FormatNumber(data.NumUseMoonCakeBoxNormal2026)} hộp quà thường.";
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

        /// <summary>Bảng xếp hạng số lần dùng Hộp quà trung thu VIP.</summary>
        public class TopUseGiftBoxVip2026 : Top
        {
            public static readonly TopUseGiftBoxVip2026 Instance = new TopUseGiftBoxVip2026();

            protected TopUseGiftBoxVip2026() : base("trungthu.2026.box.vip.top")
            {
                this.name = "TOP Dùng Hộp Bánh Trung Thu VIP 2026";
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
                topData.desc = $"Hạng chưa có. Bạn đã dùng {Utilities.FormatNumber(player.playerData.NumUseMoonCakeBoxVip2026)} hộp quà VIP.";
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
                        var topDataDynamic = conn.Query("SELECT user_id, name, avatarPath, NumUseMoonCakeBoxVip2026 FROM `player` WHERE isAdmin = 0 ORDER BY `player`.`NumUseMoonCakeBoxVip2026` DESC LIMIT 50");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = data.name;
                            topData.imgPath = data.avatarPath;
                            topData.title = topData.name;
                            topData.desc = $"Hạng {index}. Đã dùng {Utilities.FormatNumber(data.NumUseMoonCakeBoxVip2026)} hộp quà VIP.";
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
