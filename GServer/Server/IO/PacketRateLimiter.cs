using Gopet.Util;

namespace Gopet.IO
{
    /// <summary>
    /// Giới hạn tần suất gói tin của MỘT phiên (mỗi session 1 instance, chỉ đọc-thread của session đó gọi nên
    /// không cần khoá). Cửa sổ cố định 1 giây, 3 tầng trần:
    ///  - tổng mọi gói: GLOBAL_PER_SEC
    ///  - từng loại gói (ms.id): PER_ID_PER_SEC
    ///  - nhóm gói nhạy cảm (dùng item, nâng cấp, chợ, menu/nhập liệu...): STRICT_PER_SEC
    /// Gói vượt trần bị BỎ, không xử lý. Bị vượt liên tục KICK_AFTER_WINDOWS giây liền thì đóng kết nối.
    /// TEA chỉ chống nghe lén; spam từ client thật/tool đã có key vẫn phải chặn ở server.
    /// </summary>
    public class PacketRateLimiter
    {
        public const int GLOBAL_PER_SEC = 60;
        public const int PER_ID_PER_SEC = 20;
        public const int STRICT_PER_SEC = 8;
        public const int KICK_AFTER_WINDOWS = 5;

        // Toàn bộ là id gói cấp cao nhất (case của switch trong GameController.onMessage nên mỗi giá trị duy nhất).
        private static readonly bool[] Strict = BuildStrict();

        private static bool[] BuildStrict()
        {
            var table = new bool[256];
            sbyte[] ids =
            {
                GopetCMD.USE_EQUIP_ITEM, GopetCMD.USE_NORMAL_ITEM_COUNT, GopetCMD.ENCHANT_ITEM,
                GopetCMD.UP_TIER_ITEM, GopetCMD.ENCHANT_GEM_ITEM, GopetCMD.UP_TIER_GEM_ITEM,
                GopetCMD.PET_UP_TIER, GopetCMD.SELECT_KIOSK_ITEM, GopetCMD.REMOVE_SELL_ITEM,
                GopetCMD.TYPE_DIALOG_INPUT, GopetCMD.SEND_YES_NO, GopetCMD.SELECT_OPTION,
                GopetCMD.SELECT_MENU_ELEMENT, GopetCMD.PLAYER_PK, GopetCMD.GUIDER_TYPE_PAY,
                GopetCMD.LETTER_COMMAND_SEND_LETTER,
            };
            foreach (sbyte id in ids) table[id & 0xFF] = true;
            return table;
        }

        private long windowStart = Utilities.CurrentTimeMillis;
        private int total;
        private readonly int[] perId = new int[256];
        private bool windowViolated;
        private int violatedWindows;
        private int firstViolatedId = -1;

        public enum Verdict { Allow, Drop, Kick }

        /// <summary>true nếu vừa vi phạm lần đầu trong cửa sổ hiện tại (để caller ghi log 1 lần / giây).</summary>
        public bool NewViolation { get; private set; }
        public int LastViolatedId => firstViolatedId;
        public int ViolatedWindows => violatedWindows;

        public Verdict Check(sbyte id)
        {
            NewViolation = false;
            long now = Utilities.CurrentTimeMillis;
            if (now - windowStart >= 1000)
            {
                // Hết cửa sổ: vi phạm liên tiếp thì cộng dồn, cửa sổ sạch thì reset.
                violatedWindows = windowViolated ? violatedWindows + 1 : 0;
                windowStart = now;
                total = 0;
                Array.Clear(perId);
                windowViolated = false;
                firstViolatedId = -1;
            }

            int idx = id & 0xFF;
            total++;
            perId[idx]++;

            int idCap = Strict[idx] ? STRICT_PER_SEC : PER_ID_PER_SEC;
            if (total > GLOBAL_PER_SEC || perId[idx] > idCap)
            {
                if (!windowViolated)
                {
                    windowViolated = true;
                    firstViolatedId = idx;
                    NewViolation = true;
                }
                // Cửa sổ hiện tại là lần vi phạm thứ (violatedWindows + 1) liên tiếp.
                return violatedWindows + 1 >= KICK_AFTER_WINDOWS ? Verdict.Kick : Verdict.Drop;
            }
            return Verdict.Allow;
        }
    }
}
