using System.Collections.Generic;

namespace Gopet.APIs
{
    /// <summary>
    /// Response chuẩn cho endpoint danh sách (khác GopetRepository&lt;T&gt; cũ — có thêm "message").
    /// Khớp với interface BaseResponse&lt;T&gt; phía Next.js.
    /// </summary>
    public record BaseResponse<T>(int Status, string Message, T Data);

    /// <summary>Khớp với interface PaginatedData&lt;T&gt; phía Next.js.</summary>
    public record PaginatedData<T>(List<T> Data, int Total, int Page, int Limit);
}
