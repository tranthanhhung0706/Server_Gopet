using Gopet.Data.GopetClan;
using Gopet.Util;

namespace Gopet.Data.Map
{
    public class ClanMap : GopetMap
    {
        public ClanMap(int mapId_, bool canUpdate, MapTemplate mapTemplate) : base(mapId_, canUpdate, mapTemplate)
        {
        }

        public override void addPlace(Place place)
        {
            throw new UnsupportedOperationException("Place dua vao clan");
        }


        public override void addRandom(Player player)
        {
            // Đi qua cổng vào map 30 (vd từ map Boss lãnh địa 36) → đưa vào đúng khu của bang mình, giống option
            // "Vào lãnh địa bang" ở NPC. Không có bang thì từ chối như trước.
            var clanMember = player.controller.getClan();
            if (clanMember != null)
            {
                clanMember.getClan().getClanPlace().add(player);
                return;
            }
            player.redDialog(player.Language.MapRequireYouHaveClan);
        }


        public override void update()
        {
            try
            {
                foreach (var clan in ClanManager.clans)
                {
                    // try riêng từng bang: 1 bang lỗi dữ liệu không được chặn update của các bang còn lại
                    try
                    {
                        clan.update();
                    }
                    catch (Exception e)
                    {
                        e.printStackTrace();
                    }
                }
            }
            catch (Exception e)
            {
                e.printStackTrace();
            }
        }


        public override void createZoneDefault()
        {

        }
    }
}