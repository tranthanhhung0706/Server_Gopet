using Gopet.Data.Collections;
using Gopet.Util;
using Newtonsoft.Json;

namespace Gopet.Data.GopetClan
{
    public class ClanMember
    {
        public string avatarPath;
        public int user_id;
        public string name;
        public long fundDonate = 0;
        public sbyte duty;
        public DateTime timeJoin = DateTime.Now;
        public DateTime timeResetData = DateTime.Now;
        public Dictionary<int, DateTime> shopData = new Dictionary<int, DateTime>();
        public int curQuest = 0;
        [JsonIgnore]
        public Clan clan;

        /// <summary>Số lần mỗi thành viên được đánh boss lãnh địa bang (map 36) trong 1 ngày.</summary>
        public const int CLAN_BOSS_DAILY_LIMIT = 3;
        /// <summary>Số lần đã đánh boss lãnh địa trong ngày <see cref="clanBossFightDay"/> (lưu cùng danh sách thành viên bang).</summary>
        public int clanBossFights = 0;
        public DateTime clanBossFightDay = DateTime.MinValue;

        /// <summary>Số lần đánh boss lãnh địa còn lại hôm nay (qua ngày mới tự đầy lại).</summary>
        public int ClanBossFightsLeft()
        {
            return clanBossFightDay.Date != DateTime.Today ? CLAN_BOSS_DAILY_LIMIT : Math.Max(0, CLAN_BOSS_DAILY_LIMIT - clanBossFights);
        }

        /// <summary>Ghi nhận 1 lần đánh boss lãnh địa (sang ngày mới thì đếm lại từ đầu).</summary>
        public void UseClanBossFight()
        {
            if (clanBossFightDay.Date != DateTime.Today)
            {
                clanBossFightDay = DateTime.Today;
                clanBossFights = 0;
            }
            clanBossFights++;
        }

        public void reset()
        {
            timeResetData = DateTime.Now;
            curQuest = 0;
        }

        public bool needReset()
        {
            DateTime dateTime = timeResetData.AddDays(1);
            return dateTime < DateTime.Now;
        }

        public string getDutyName(Player player)
        {
            switch (duty)
            {
                case Clan.TYPE_NORMAL:
                    return player.Language.ClanDutyMember;
                case Clan.TYPE_LEADER:
                    return player.Language.ClanLeaderMember;
                case Clan.TYPE_DEPUTY_LEADER:
                    return player.Language.ClanDeputyLeaderMember;
                case Clan.TYPE_SENIOR:
                    return player.Language.ClanSeniorMember;
            }
            return $"Không xác định <{duty}>";
        }

        [JsonIgnore]
        public bool IsLeader
        {
            get
            {
                return duty == Clan.TYPE_LEADER || duty == Clan.TYPE_DEPUTY_LEADER;
            }
        }


        public string getAvatar()
        {
            if (avatarPath == null)
            {
                return "npcs/gopet.png";
            }
            return avatarPath;
        }

        internal Clan getClan()
        {
            return clan;
        }
    }
}