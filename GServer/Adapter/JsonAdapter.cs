using Dapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gopet.Adapter
{
    public class JsonAdapter<T> : SqlMapper.TypeHandler<T>
    {

        public static JsonSerializerSettings SerializerSettings { get; } = new JsonSerializerSettings()
        {
            DefaultValueHandling = DefaultValueHandling.Include,
            NullValueHandling = NullValueHandling.Include
        };

        public override T? Parse(object value)
        {
            if (value is string text)
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(text, SerializerSettings);
                }
                catch (Exception ex)
                {
                    // 1 dòng dữ liệu JSON hỏng (vd cột bị cắt cụt do vượt giới hạn độ dài VARCHAR,
                    // hoặc admin gõ tay sai cú pháp) trước đây làm crash TOÀN BỘ server ngay khi có
                    // player đăng nhập/thao tác đụng tới dòng đó (Unhandled exception — mọi người
                    // chơi khác cũng bị văng theo). Giờ chỉ log lỗi + trả về default(T) (null) cho
                    // đúng dòng đó, phần còn lại của server vẫn chạy bình thường.
                    GopetManager.ServerMonitor.LogError($"JsonAdapter<{typeof(T).Name}>.Parse lỗi, trả về default. Giá trị lỗi: {text}\n{ex}");
                    return default(T);
                }
            }
            else
            {
                return default(T);
            }
        }

        public override void SetValue(IDbDataParameter parameter, T? value)
        {
            if (value != null)
            {
                parameter.Value = JsonConvert.SerializeObject(value, SerializerSettings);
            }
        }
    }
}
