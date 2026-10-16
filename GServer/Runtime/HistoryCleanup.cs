using Dapper;

namespace Gopet.Runtime
{
    /// <summary>
    /// Tự xoá lịch sử thao tác (bảng `history`, DB gp_log) cũ hơn <see cref="KEEP_DAYS"/> ngày.
    /// Bảng này từng phình tới ~17,5 GB làm đầy ổ đĩa → MariaDB "Disk is full" → server sập.
    ///
    /// Chạy lúc khởi động rồi mỗi <see cref="INTERVAL_HOURS"/> giờ, trên luồng nền riêng (không chặn RuntimeServer,
    /// vì AutoSave chạy chung luồng đó). Xoá theo lô <see cref="BATCH_SIZE"/> dòng để không khoá bảng lâu khi
    /// HistoryManager đang ghi. DELETE không trả dung lượng file .ibd cho ổ đĩa, nhưng InnoDB dùng lại chỗ trống
    /// cho dòng mới nên bảng sẽ không phình thêm sau khi đã thu nhỏ 1 lần bằng tay.
    /// </summary>
    public class HistoryCleanup : IRuntime
    {
        public const int KEEP_DAYS = 10;
        private const int INTERVAL_HOURS = 6;
        private const int BATCH_SIZE = 5000;

        private DateTime nextRun = DateTime.Now;
        private int running = 0;

        public void Update()
        {
            if (DateTime.Now < nextRun || Interlocked.CompareExchange(ref running, 1, 0) != 0)
            {
                return;
            }
            nextRun = DateTime.Now.AddHours(INTERVAL_HOURS);
            Task.Run(() =>
            {
                try
                {
                    DateTime cutoff = DateTime.Now.AddDays(-KEEP_DAYS);
                    long total = 0;
                    using var conn = MYSQLManager.createLogConnection();
                    while (true)
                    {
                        int deleted = conn.Execute("DELETE FROM `history` WHERE timeDB < @cutoff LIMIT " + BATCH_SIZE, new { cutoff }, commandTimeout: 300);
                        total += deleted;
                        if (deleted < BATCH_SIZE)
                        {
                            break;
                        }
                        Thread.Sleep(200); // nhường cho HistoryManager ghi xen giữa các lô
                    }
                    GopetManager.ServerMonitor.LogInfo($"Dọn lịch sử: xoá {total} dòng cũ hơn {KEEP_DAYS} ngày");
                }
                catch (Exception e)
                {
                    GopetManager.ServerMonitor.LogError("Dọn lịch sử lỗi: " + e.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref running, 0);
                }
            });
        }
    }
}
