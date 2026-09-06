
using Gopet.Data.Collections;
using Gopet.Data.GopetItem;
using Gopet.Data.item;
using Gopet.Util;
using Newtonsoft.Json;

public class PetTatto : IBinaryObject<PetTatto>
{

    public DateTime Time { get; set; } = DateTime.Now;

    public int tattooTemplateId;
    public int tattoId;
    public int lvl = 0;

    public PetTatto(int tattooTemplateId)
    {
        this.tattooTemplateId = tattooTemplateId;
    }


    public static int getInfo(int info, int lvl)
    {
        return info + (int)Utilities.GetValueFromPercent(info, lvl * 10f);
    }

    public int getHp()
    {
        return getInfo(Template.hp, lvl);
    }


    public int getMp()
    {
        return getInfo(Template.mp, lvl);
    }


    public int getAtk()
    {
        return getInfo(Template.atk, lvl);
    }


    public int getDef()
    {
        return getInfo(Template.def, lvl);
    }


    public PetTattoTemplate Template
    {
        get
        {
            return GopetManager.tattos.get(tattooTemplateId);
        }
    }

    [JsonIgnore]
    public PetTatto Instance => this;

    public String getName(Player player)
    {
        JArrayList<String> infoStrings = new JArrayList<String>();

        if (lvl > 0)
        {
            infoStrings.add($" "+ player.Language.level + $" {lvl}");
        }

        if (getAtk() > 0)
        {
            infoStrings.add(getAtk() + " (atk) ");
        }
        if (getDef() > 0)
        {
            infoStrings.add(getDef() + " (def) ");
        }
        if (getHp() > 0)
        {
            infoStrings.add(getHp() + " (hp) ");
        }
        if (getMp() > 0)
        {
            infoStrings.add(getMp() + " (mp) ");
        }

        return Template.getName(player) + "  " + String.Join(" ", infoStrings);
    }

    /// <summary>
    /// Đọc hiệu ứng combat đặc biệt của xăm (phản đòn/hút máu/định thân...), cùng định dạng và cách
    /// đọc với Item.ExtractBattleOptions() dùng cho cánh. Xăm không có "roll" chỉ số riêng như Item
    /// nên đọc thẳng từ Template thay vì optionValue instance.
    /// </summary>
    public ItemBattleOptionBuff[] ExtractBattleOptions()
    {
        int[] itemOption = Template.itemOption;
        int[] itemOptionValue = Template.itemOptionValue;
        if (itemOption == null || itemOptionValue == null || !itemOption.Contains(ItemInfo.OptionType.OPTION_BATTLE))
        {
            return new ItemBattleOptionBuff[0];
        }
        JArrayList<ItemBattleOptionBuff> buffs = new();
        for (int i = 0; i < itemOption.Length; i++)
        {
            if (itemOption[i] == ItemInfo.OptionType.OPTION_BATTLE)
            {
                buffs.add(new ItemBattleOptionBuff(
                    itemOptionValue[i],
                    itemOptionValue[i + 2],
                    itemOptionValue[i + 3] == 1,
                    itemOptionValue[i + 1] > 100 ? int.MaxValue - 10 : itemOptionValue[i + 1]));
                i += 3;
            }
        }
        return buffs.ToArray();
    }

    public int GetId()
    {
        return this.tattoId;
    }

    public void SetId(int id)
    {
        this.tattoId = id;
    }
}
