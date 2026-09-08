using Dapper;
using Gopet.Util;
using System;
using System.Linq;

namespace Gopet.Data.top
{
    /// <summary>
    /// Xếp hạng theo tổng tiền thật đã nạp (cột `tongnap`, DB web gopettae_gopet_web) — khác
    /// TopSpendGold (xếp theo `spendGold`, DB game, tiền vàng tiêu trong game). Vì tongnap chỉ có
    /// ở DB web còn tên/avatar nhân vật chỉ có ở DB game, phải query 2 DB riêng rồi ghép lại.
    /// </summary>
    public class TopTongNap : Top
    {
        public static readonly TopTongNap Instance = new TopTongNap();

        public TopTongNap() : base("top.tongnap")
        {
            this.name = "Top tổng nạp";
            this.desc = "Chỉ những người chơi nạp thẻ nhiều nhất";
        }

        public override TopData getMyInfo(Player player)
        {
            var findTop = datas.Where(p => p.id == player.playerData.user_id);
            if (findTop.Any())
            {
                return findTop.First();
            }

            long tongNap;
            using (var webConn = MYSQLManager.createWebMySqlConnection())
            {
                tongNap = webConn.QueryFirstOrDefault<long?>(
                    "SELECT tongnap FROM `user` WHERE user_id = @user_id", new { user_id = player.playerData.user_id }) ?? 0;
            }

            TopData topData = new TopData();
            topData.id = player.playerData.user_id;
            topData.name = player.playerData.name;
            topData.imgPath = player.playerData.avatarPath;
            topData.title = topData.name;
            topData.desc = $"Hạng chưa có : Bạn đã nạp {Utilities.FormatNumber(tongNap)}";
            return topData;
        }

        public override void Update()
        {
            try
            {
                lastDatas.Clear();
                lastDatas.AddRange(datas);
                datas.Clear();
                try
                {
                    using (var webConn = MYSQLManager.createWebMySqlConnection())
                    using (var conn = MYSQLManager.create())
                    {
                        var topDataDynamic = webConn.Query(
                            "SELECT user_id, tongnap FROM `user` WHERE tongnap > 0 ORDER BY tongnap DESC LIMIT 20");
                        int index = 1;
                        foreach (dynamic data in topDataDynamic)
                        {
                            dynamic playerRow = conn.QueryFirstOrDefault(
                                "SELECT name, avatarPath FROM `player` WHERE user_id = @user_id", new { user_id = (int)data.user_id });
                            if (playerRow == null)
                            {
                                continue;
                            }

                            TopData topData = new TopData();
                            topData.id = data.user_id;
                            topData.name = playerRow.name;
                            topData.imgPath = playerRow.avatarPath;
                            topData.title = topData.name;
                            topData.desc = Utilities.Format("Hạng %s : đã nạp %s", index, Utilities.FormatNumber(data.tongnap));
                            datas.Add(topData);
                            index++;
                        }
                    }
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                }
                updateSQLBXH();
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }
    }
}
