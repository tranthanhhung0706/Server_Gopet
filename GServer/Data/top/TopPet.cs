
using Dapper;
using Gopet.Data.User;
using Gopet.Util;
using MySqlConnector;

public class TopPet : Top
{

    public static TopPet Instance = new TopPet();

    public TopPet() : base("top_pet")
    {

        base.name = "TOP Pet";
        base.desc = "Chỉ những thú cưng mạnh mẽ";
    }


    public override void Update()
    {
        try
        {
            lastDatas.Clear();
            lastDatas.AddRange(datas);
            datas.Clear();
            // Trước đây gom bằng SQL JSON_VALUE(pets, '$[*].x') — MariaDB không hỗ trợ wildcard '[*]' trong JSON_VALUE
            // nên lỗi mỗi lần cập nhật, bảng top không bao giờ nạp được. Giờ đọc JSON của từng người chơi và tính trong C#.
            var entries = new List<(int ownerId, string ownerName, int petTemplateId, int star, int lvl, long exp, int maxHp, int maxMp, int def, int atk)>();
            using (var conn = MYSQLManager.create())
            {
                var rows = conn.Query("SELECT user_id, name, pets, petSelected FROM `player` WHERE (`pets` IS NOT NULL OR `petSelected` IS NOT NULL) AND isAdmin = 0");
                foreach (dynamic row in rows)
                {
                    foreach (string? json in new string?[] { (string?)row.pets, (string?)row.petSelected })
                    {
                        if (string.IsNullOrWhiteSpace(json) || json == "null") continue;
                        try
                        {
                            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                            IEnumerable<Newtonsoft.Json.Linq.JToken> pets = token is Newtonsoft.Json.Linq.JArray arr ? arr.Children() : new[] { token };
                            foreach (var pet in pets)
                            {
                                if (pet == null || pet.Type != Newtonsoft.Json.Linq.JTokenType.Object || pet["petIdTemplate"] == null || pet["exp"] == null) continue;
                                entries.Add(((int)row.user_id, (string)row.name, (int)pet["petIdTemplate"]!, (int?)pet["star"] ?? 0, (int?)pet["lvl"] ?? 0,
                                    (long?)pet["exp"] ?? 0, (int?)pet["maxHp"] ?? 0, (int?)pet["maxMp"] ?? 0, (int?)pet["def"] ?? 0, (int?)pet["atk"] ?? 0));
                            }
                        }
                        catch (Exception)
                        {
                            // dữ liệu pet của 1 người hỏng thì bỏ qua người đó, không làm hỏng cả bảng top
                        }
                    }
                }
            }
            int index = 1;
            foreach (var data in entries.OrderByDescending(e => e.lvl).ThenByDescending(e => e.exp).Take(30))
            {
                PetTemplate petTemplate = GopetManager.PETTEMPLATE_HASH_MAP.get(data.petTemplateId);
                if (petTemplate == null) continue;
                TopData topData = new TopData();
                topData.id = data.ownerId;
                topData.name = data.ownerName;
                topData.imgPath = petTemplate.icon;
                topData.title = getNameWithStar(data.star, petTemplate) + " của " + topData.name;
                topData.desc = Utilities.Format("Hạng %s : Cấp %s  hiện có %s kinh nghiệm (hp) %s (mp) %s (def) %s (atk) %s",
                        index, data.lvl, Utilities.FormatNumber(data.exp), data.maxHp, data.maxMp, data.def, data.atk);
                datas.Add(topData);
                index++;
            }
            updateSQLBXH();
        }
        catch (Exception e)
        {
            e.printStackTrace();
            
        }
    }

    public String getNameWithStar(int star, PetTemplate petTemplate)
    {
        String name = petTemplate.name + " ";
        for (int i = 0; i < star; i++)
        {
            name += "(sao)";
        }

        for (int i = 0; i < 5 - star; i++)
        {
            name += "(saoden)";
        }
        return name;
    }
}
