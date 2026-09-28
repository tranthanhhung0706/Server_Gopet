using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gopet.Data.User
{
    public record Animation(sbyte numFrame, string frameImgPath, int vX, int vY, bool isDrawEnd, bool mirrorWithChar, sbyte type)
    {
        public const sbyte TYPE_ARCHIVENMENT = 0;
        /// <summary>
        /// Trang sức treo sát bên phải thân nhân vật (client cũ không có case này nên tự bỏ qua)
        /// </summary>
        public const sbyte TYPE_ACCESSORY = 1;
    }
}
