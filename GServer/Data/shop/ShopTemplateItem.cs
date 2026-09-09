
using Gopet.Data.GopetItem;
using Gopet.Util;
using Newtonsoft.Json;

public class ShopTemplateItem
{

    public sbyte shopId;
    public int itemTemTempleId;
    public int count;
    public sbyte[] moneyType;
    public int[] price;
    /// <summary>
    /// Loại tiền tệ THỨ 2 bắt buộc phải có ĐỦ (cộng thêm, không phải lựa chọn khác) — khác với
    /// moneyType/price là danh sách CHỌN 1 trong N. Ghép theo index với moneyType/price: nếu
    /// moneyType2[i]/price2[i] tồn tại và price2[i] > 0 thì lựa chọn thanh toán thứ i cần đủ CẢ
    /// price[i] moneyType[i] LẪN price2[i] moneyType2[i] mới mua được (vd hộp quà Boss2026: 5 Hoa
    /// Ngọc + 100.000 Ngọc). NULL/rỗng/price2[i]=0 = lựa chọn i chỉ cần 1 loại tiền như trước giờ
    /// (không đổi hành vi cũ). Xem MenuController.checkMoneyShopItem/deductMoneyShopItem.
    /// </summary>
    /// <remarks>
    /// [JsonProperty(NullValueHandling = Ignore)] BẮT BUỘC phải có: object này bị serialize thẳng
    /// vào cột player.shopArena (VARCHAR 4000, xem ShopArena.cs) mỗi khi player reset shop đấu
    /// trường — JsonAdapter dùng NullValueHandling.Include GLOBAL nên nếu không override riêng ở
    /// đây, mọi ShopTemplateItem null cả 2 field mới này (hầu hết mọi item hiện có, vì tính năng
    /// chỉ dùng cho hộp quà Boss2026) sẽ tốn thêm ~30 byte/item × N item trong shopArena, có thể
    /// làm vượt 4000 ký tự → MySQL cắt cụt JSON → crash JsonSerializationException lúc login.
    /// </remarks>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public sbyte[]? moneyType2;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int[]? price2;
    public bool isSpceial = false;
    public String nameSpeceial, descriptionSpeceial;
    public bool needRemove = false;
    public int spceialType = -1;
    public bool CloseScreenAfterClick = false;
    public int clanLvl;
    public int perCount = 0;
    public const int TYPE_RESET_SHOP_ARENA = 1;
    public bool hasId = false;
    public int menuId;
    public int petId;
    public bool isSellItem = true;
    public bool isLock = false;
    public TimeSpan? TimeNeedReset { get; set; } = null;
    public int NeedFund = 0;
    public void setShopId(sbyte shopId)
    {
        this.shopId = shopId;
    }

    public void setItemTempalteId(int itemTempalteId)
    {
        this.itemTemTempleId = itemTempalteId;
    }

    public void setCount(int count)
    {
        this.count = count;
    }

    public void setMoneyType(sbyte[] moneyType)
    {
        this.moneyType = moneyType;
    }

    public void setPrice(int[] price)
    {
        this.price = price;
    }




    public void setNameSpeceial(String nameSpeceial)
    {
        this.nameSpeceial = nameSpeceial;
    }

    public void setDescriptionSpeceial(String descriptionSpeceial)
    {
        this.descriptionSpeceial = descriptionSpeceial;
    }

    public void setNeedRemove(bool needRemove)
    {
        this.needRemove = needRemove;
    }

    public void setSpceialType(int spceialType)
    {
        this.spceialType = spceialType;
    }

    public void setCloseScreenAfterClick(bool CloseScreenAfterClick)
    {
        this.CloseScreenAfterClick = CloseScreenAfterClick;
    }

    public void setClanLvl(int clanLvl)
    {
        this.clanLvl = clanLvl;
    }

    public void setPerCount(int perCount)
    {
        this.perCount = perCount;
    }

    public void setHasId(bool hasId)
    {
        this.hasId = hasId;
    }

    public void setMenuId(int menuId)
    {
        this.menuId = menuId;
    }

    public void setPetId(int petId)
    {
        this.petId = petId;
    }



    public int getShopId()
    {
        return this.shopId;
    }

    public int getItemTempalteId()
    {
        return this.itemTemTempleId;
    }

    public int getCount()
    {
        return this.count;
    }

    public sbyte[] getMoneyType()
    {
        return this.moneyType;
    }

    public int[] getPrice()
    {
        return this.price;
    }

    public sbyte[]? getMoneyType2()
    {
        return this.moneyType2;
    }

    public int[]? getPrice2()
    {
        return this.price2;
    }

    public String getNameSpeceial()
    {
        return this.nameSpeceial;
    }

    public String getDescriptionSpeceial()
    {
        return this.descriptionSpeceial;
    }

    public bool isNeedRemove()
    {
        return this.needRemove;
    }

    public int getSpceialType()
    {
        return this.spceialType;
    }

    public bool isCloseScreenAfterClick()
    {
        return this.CloseScreenAfterClick;
    }

    public int getClanLvl()
    {
        return this.clanLvl;
    }

    public int getPerCount()
    {
        return this.perCount;
    }

    public bool isHasId()
    {
        return this.hasId;
    }

    public int getMenuId()
    {
        return this.menuId;
    }

    public int getPetId()
    {
        return this.petId;
    }




    public ItemTemplate getItemTemplate()
    {
        return GopetManager.itemTemplate.get(itemTemTempleId);
    }

    public PetTemplate getPetTemplate()
    {
        return GopetManager.PETTEMPLATE_HASH_MAP.get(petId);
    }

    public String getIconPath()
    {
        if (isSellItem)
        {
            return getItemTemplate().getIconPath();
        }

        return getPetTemplate().icon;
    }

    public String getDesc(Player player)
    {
        if (isSpceial)
        {
            return descriptionSpeceial;
        }

        if (!isSellItem)
        {
            PetTemplate petTemplate = getPetTemplate();
            if (petTemplate == null)
            {
                throw new NullReferenceException("pet null");
            }
            return Utilities.Format($"Hệ: {GopetManager.GetElementDisplay(petTemplate.element, petTemplate.nclass, player)}. Mô tả:   " + " + %s (str) , + %s (agi) , + %s (int) , + %s (hp) , + %s (mp)", petTemplate.str, petTemplate.agi, petTemplate._int, petTemplate.getHp(), petTemplate.getMp());
        }

        ItemTemplate itemTemplate = getItemTemplate();

        if (itemTemplate.getType() == GopetManager.PET_EQUIP_ARMOUR || itemTemplate.getType() == GopetManager.PET_EQUIP_GLOVE || itemTemplate.getType() == GopetManager.PET_EQUIP_HAT || itemTemplate.getType() == GopetManager.PET_EQUIP_SHOE || itemTemplate.getType() == GopetManager.PET_EQUIP_WEAPON)
        {
            return itemTemplate.getDescription(player) + Utilities.Format("( %s ,  %s,  %s ,  %s )", itemTemplate.getAtk(), itemTemplate.getDef(), itemTemplate.getHp(), itemTemplate.getMp());
        }

        if (itemTemplate.getType() == GopetManager.SKIN_ITEM)
        {
            return Utilities.Format("+%s  +%s +%s  +%s ", itemTemplate.getAtk(), itemTemplate.getDef(), itemTemplate.getHp(), itemTemplate.getMp());
        }
        return itemTemplate.getDescription(player);
    }

    public String getName(Player player)
    {
        if (isSpceial)
        {
            return nameSpeceial;
        }

        if (!isSellItem)
        {
            return getPetTemplate().getName(player);
        }
        ItemTemplate itemTemplate = getItemTemplate();

        if (itemTemplate.getType() == GopetManager.PET_EQUIP_ARMOUR || itemTemplate.getType() == GopetManager.PET_EQUIP_GLOVE || itemTemplate.getType() == GopetManager.PET_EQUIP_HAT || itemTemplate.getType() == GopetManager.PET_EQUIP_SHOE || itemTemplate.getType() == GopetManager.PET_EQUIP_WEAPON)
        {
            return itemTemplate.getName(player) + Utilities.Format("(" + player.Language.Request + "   %s (str) ,  %s (agi) ,  %s (int))", itemTemplate.getRequireStr(), itemTemplate.getRequireAgi(), itemTemplate.getRequireInt());
        }

        if (count > 1 && shopId != MenuController.SHOP_CLAN)
        {
            return itemTemplate.getName(player) + "  x" + count;
        }
        else if (count > 1 && shopId == MenuController.SHOP_CLAN)
        {
            return itemTemplate.getName(player) + " còn x" + (count - perCount);
        }
        return itemTemplate.getName(player);
    }

    public void execute(Player player)
    {
        switch (spceialType)
        {
            case TYPE_RESET_SHOP_ARENA:
                {
                    if (player.playerData.shopArena != null)
                    {
                        if (player.playerData.shopArena.getNumReset() - 1 >= GopetManager.MAX_RESET_SHOP_ARENA)
                        {
                            player.redDialog("Reset đạt số lần tối đa trong hôm nay");
                            return;
                        }
                        player.playerData.shopArena.nextArena();
                        MenuController.sendMenu(MenuController.SHOP_ARENA, player);
                        player.okDialog("Reset thành công");
                    }
                }
                break;
            default:
                GopetManager.ServerMonitor.LogError($"Không có type đặc biệt là: {spceialType}");
                break;
        }

    }

    public void setSpceial(bool v)
    {
        this.isSpceial = v;
    }


    public ShopTemplateItem Clone()
    {
        ShopTemplateItem item = new ShopTemplateItem();
        item.shopId = shopId;
        item.itemTemTempleId = itemTemTempleId;
        item.count = count;
        item.moneyType = moneyType.ToArray();
        item.price = price.ToArray();
        item.moneyType2 = moneyType2?.ToArray();
        item.price2 = price2?.ToArray();
        item.isSpceial = isSpceial;
        item.nameSpeceial = nameSpeceial;
        item.descriptionSpeceial = descriptionSpeceial;
        item.needRemove = needRemove;
        item.spceialType = spceialType;
        item.CloseScreenAfterClick = CloseScreenAfterClick;
        item.clanLvl = clanLvl;
        item.perCount = perCount;
        item.hasId = hasId;
        item.menuId = menuId;
        item.petId = petId;
        item.isSellItem = isSellItem;
        item.isLock = isLock;
        item.TimeNeedReset = TimeNeedReset;
        item.NeedFund = NeedFund;
        return item;
    }
}
