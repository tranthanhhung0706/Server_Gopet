using Gopet.Util;

namespace Gopet.IO
{
    /// <summary>
    /// Giới hạn tần suất gói tin của MỘT phiên (mỗi session 1 instance, chỉ read-thread của session đó gọi nên
    /// không cần khoá). Cửa sổ cố định 1 giây:
    ///  - tổng mọi gói (trừ xin ảnh): GLOBAL_PER_SEC
    ///  - từng "loại gói": PER_KIND_PER_SEC; nhóm nhạy cảm (dùng item, nâng cấp, chợ, menu/nhập liệu, PK...): STRICT_PER_SEC
    ///  - xin ảnh (COMMAND_IMAGE / PET_SERVICE.REQUEST_PET_IMG): xô riêng IMAGE_PER_SEC, vì mở rương/bản đồ là
    ///    client xin hàng chục ảnh cùng lúc và không được làm rớt (mất hình).
    /// "Loại gói" = id gói; riêng các gói ô dù (PET_SERVICE, COMMAND_GUIDER, SERVER_MESSAGE) tính theo (id, lệnh con)
    /// vì mã lệnh con chỉ có nghĩa BÊN TRONG gói ô dù và trùng số với id gói khác.
    /// Gói vượt trần bị BỎ. Vượt liên tục KICK_AFTER_WINDOWS giây liền thì đóng kết nối.
    /// </summary>
    public class PacketRateLimiter
    {
        public const int GLOBAL_PER_SEC = 60;
        public const int PER_KIND_PER_SEC = 20;
        public const int STRICT_PER_SEC = 8;
        public const int IMAGE_PER_SEC = 300;
        public const int KICK_AFTER_WINDOWS = 5;

        // Ô dù -> vùng đếm riêng: [0..255] id gói thường, [256..511] PET_SERVICE.sub, [512..767] COMMAND_GUIDER.sub, [768..1023] SERVER_MESSAGE.sub
        private const int SLOTS = 1024;
        private static readonly bool[] Strict = BuildStrict();

        private static int Slot(sbyte id, sbyte sub)
        {
            if (id == GopetCMD.PET_SERVICE) return 256 + (sub & 0xFF);
            if (id == GopetCMD.COMMAND_GUIDER) return 512 + (sub & 0xFF);
            if (id == GopetCMD.SERVER_MESSAGE) return 768 + (sub & 0xFF);
            return id & 0xFF;
        }

        private static bool[] BuildStrict()
        {
            var table = new bool[SLOTS];
            foreach (sbyte sub in new sbyte[]
            {
                GopetCMD.USE_EQUIP_ITEM, GopetCMD.USE_NORMAL_ITEM_COUNT, GopetCMD.ENCHANT_ITEM,
                GopetCMD.UP_TIER_ITEM, GopetCMD.ENCHANT_GEM_ITEM, GopetCMD.UP_TIER_GEM_ITEM,
                GopetCMD.PET_UP_TIER, GopetCMD.SELECT_KIOSK_ITEM, GopetCMD.REMOVE_SELL_ITEM, GopetCMD.PLAYER_PK,
            })
            {
                table[Slot(GopetCMD.PET_SERVICE, sub)] = true;
            }
            foreach (sbyte sub in new sbyte[]
            {
                GopetCMD.SELECT_OPTION, GopetCMD.SELECT_MENU_ELEMENT, GopetCMD.TYPE_DIALOG_INPUT, GopetCMD.GUIDER_TYPE_PAY,
            })
            {
                table[Slot(GopetCMD.COMMAND_GUIDER, sub)] = true;
            }
            table[Slot(GopetCMD.SERVER_MESSAGE, GopetCMD.SEND_YES_NO)] = true;
            return table;
        }

        private long windowStart = Utilities.CurrentTimeMillis;
        private int total;
        private int images;
        private readonly int[] perKind = new int[SLOTS];
        private bool windowViolated;
        private int violatedWindows;
        private int firstViolatedSlot = -1;
        private long lastSoftLog;

        public enum Verdict { Allow, Drop, Kick }

        /// <summary>true nếu vừa vi phạm lần đầu trong cửa sổ hiện tại (để caller ghi log 1 lần / giây).</summary>
        public bool NewViolation { get; private set; }
        /// <summary>Mô tả loại gói vi phạm, để ghi log.</summary>
        public string LastViolated => firstViolatedSlot < 0 ? "?" : (firstViolatedSlot < 256 ? $"cmd={firstViolatedSlot}" : $"cmd={(new[] { GopetCMD.PET_SERVICE, GopetCMD.COMMAND_GUIDER, GopetCMD.SERVER_MESSAGE })[firstViolatedSlot / 256 - 1]}/sub={(sbyte)(firstViolatedSlot & 0xFF)}");
        public int ViolatedWindows => violatedWindows;

        public Verdict Check(sbyte id, sbyte sub)
        {
            NewViolation = false;
            long now = Utilities.CurrentTimeMillis;
            if (now - windowStart >= 1000)
            {
                violatedWindows = windowViolated ? violatedWindows + 1 : 0;
                windowStart = now;
                total = 0;
                images = 0;
                Array.Clear(perKind);
                windowViolated = false;
                firstViolatedSlot = -1;
            }

            bool violated;
            bool severe = true;
            int slot;
            if (id == GopetCMD.COMMAND_IMAGE || (id == GopetCMD.PET_SERVICE && sub == GopetCMD.REQUEST_PET_IMG))
            {
                slot = id & 0xFF;
                violated = ++images > IMAGE_PER_SEC;
            }
            else
            {
                slot = Slot(id, sub);
                total++;
                perKind[slot]++;
                bool strict = Strict[slot];
                bool globalOver = total > GLOBAL_PER_SEC;
                violated = globalOver || perKind[slot] > (strict ? STRICT_PER_SEC : PER_KIND_PER_SEC);
                // Vượt trần riêng của 1 loại gói thường (không nhạy cảm, vd bật/tắt hồi HP pet) chỉ bị BỎ BỚT,
                // không tính vào kick: client mod/tool auto hay gửi dồn loại này mà không gây hại gì.
                severe = globalOver || strict;
            }

            if (!violated) return Verdict.Allow;
            if (!severe)
            {
                // Log tối đa 1 lần / 60s mỗi phiên để khỏi ngập console.
                if (now - lastSoftLog >= 60000)
                {
                    lastSoftLog = now;
                    firstViolatedSlot = slot;
                    NewViolation = true;
                }
                return Verdict.Drop;
            }
            if (!windowViolated)
            {
                windowViolated = true;
                firstViolatedSlot = slot;
                NewViolation = true;
            }
            return violatedWindows + 1 >= KICK_AFTER_WINDOWS ? Verdict.Kick : Verdict.Drop;
        }
    }
}
