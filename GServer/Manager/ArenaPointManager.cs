using Dapper;
using Gopet.Util;

public class ArenaPointManager
{
    /// <summary>
    /// Cộng/trừ điểm đấu trường cho một user_id, hoạt động cả khi người chơi đang offline.
    /// Không đụng tới các field khác của PlayerData nên không cần load/save toàn bộ dữ liệu người chơi.
    /// </summary>
    public static void adjustPoint(int userId, int delta)
    {
        try
        {
            Player online = PlayerManager.get(userId);
            if (online != null)
            {
                // Đang có 0 điểm mà bị trừ thêm thì bỏ qua, không trừ nữa.
                if (delta < 0 && online.playerData.ArenaPoint <= 0)
                {
                    return;
                }
                online.playerData.ArenaPoint = Math.Max(0, online.playerData.ArenaPoint + delta);
                return;
            }
            using (var conn = MYSQLManager.create())
            {
                if (delta < 0)
                {
                    conn.Execute("UPDATE `player` SET ArenaPoint = GREATEST(0, ArenaPoint + @delta) WHERE user_id = @id AND ArenaPoint > 0", new { delta, id = userId });
                }
                else
                {
                    conn.Execute("UPDATE `player` SET ArenaPoint = ArenaPoint + @delta WHERE user_id = @id", new { delta, id = userId });
                }
            }
        }
        catch (Exception e)
        {
            e.printStackTrace();
        }
    }
}
