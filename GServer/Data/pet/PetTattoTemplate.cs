 
public class PetTattoTemplate {
    public int tattooId;
    public String name, iconPath;
    public int atk, def, hp, mp;
    public float percent;

    /// <summary>
    /// Hiệu ứng combat đặc biệt (phản đòn/hút máu/định thân...) — cùng định dạng ItemInfo dùng cho
    /// cánh (xem Item.ExtractBattleOptions()): mỗi hiệu ứng là 1 nhóm 4 phần tử liên tiếp trong
    /// itemOption bắt đầu bằng ItemInfo.OptionType.OPTION_BATTLE (13), giá trị tương ứng trong
    /// itemOptionValue là [effectTypeId, turn, percentValue, isActiveOnWearer(1/0)]. Không có hiệu
    /// ứng đặc biệt thì để null/rỗng.
    /// </summary>
    public int[] itemOption { get; set; } = new int[0];
    public int[] itemOptionValue { get; set; } = new int[0];

    public void setTattooId(int tattooId)
    {
        this.tattooId = tattooId;
    }


    public void setIconPath(String iconPath)
    {
        this.iconPath = iconPath;
    }

    public void setAtk(int atk)
    {
        this.atk = atk;
    }

    public void setDef(int def)
    {
        this.def = def;
    }

    public void setHp(int hp)
    {
        this.hp = hp;
    }

    public void setMp(int mp)
    {
        this.mp = mp;
    }

    public void setPercent(float percent)
    {
        this.percent = percent;
    }


    public int getTattooId()
    {
        return this.tattooId;
    }

    public string getName(Player player)
    {
        return player.Language.TattoLanguage[this.tattooId];
    }

    public String getIconPath()
    {
        return this.iconPath;
    }

    public int getAtk()
    {
        return this.atk;
    }

    public int getDef()
    {
        return this.def;
    }

    public int getHp()
    {
        return this.hp;
    }

    public int getMp()
    {
        return this.mp;
    }

    public float getPercent()
    {
        return this.percent;
    }

}
