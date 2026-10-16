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