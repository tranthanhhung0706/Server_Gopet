
using Dapper;
using Gopet.Data.Collections;
using Gopet.Util;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MySqlConnector;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Threading;

public class HistoryManager
{

    public static HistoryManager Instance = new HistoryManager();
    private ConcurrentQueue<History> historys = new();
    /// <summary>Số dòng lịch sử tối đa giữ chờ trong RAM khi DB không ghi được (vượt thì bỏ dòng cũ nhất).</summary>
    private const int MAX_PENDING_HISTORY = 50000;
    private AutoResetEvent AutoResetEvent = new AutoResetEvent(false);
    public Thread HistoryThread;
    public HistoryManager()
    {
        this.HistoryThread = new Thread(run);
        this.HistoryThread.Name = "History thread";
        this.HistoryThread.IsBackground = true;
    }

    public void start()
    {
        this.HistoryThread.Start();
    }

    public void add(History history)
    {
        historys.Enqueue(history);
        AutoResetEvent.Set();
    }

    public static void addHistory(History history)
    {
        Instance.add(history);
    }


    public void run()
    {
        try
        {
            while (true)
            {
                /*
                using (var client = new MongoClient("mongodb://localhost:27017"))
                {
                    var database = client.GetDatabase("logDB"); 
                    var collection = database.GetCollection<BsonDocument>("logDB");
                    while (this.historys.TryDequeue(out History history))
                    {
                        var json = JsonConvert.SerializeObject(history.Get());
                        var document = BsonSerializer.Deserialize<BsonDocument>(json);
                        collection.InsertOne(document);
                    }
                }
                */

                // try/catch NẰM TRONG vòng lặp: trước đây 1 lần ghi lỗi (vd ổ đĩa đầy → "table 'history' is full")
                // làm thoát hẳn vòng lặp → luồng ghi lịch sử chết vĩnh viễn, hàng đợi phình mãi trong RAM.
                try
                {
                    using (var conn = MYSQLManager.createLogConnection())
                    {
                        using (var gameMySqlConnection = MYSQLManager.create())
                        {
                            while (this.historys.TryDequeue(out var history))
                            {
                                conn.Execute("INSERT INTO `history`(`targetId`, `log`, `obj`, `charname`) VALUES (@targetId,@log,@obj,@charname)", new
                                {
                                    targetId = history.user_id,
                                    log = history.log,
                                    obj = JsonConvert.SerializeObject(history.obj),
                                    charname = history.charName(gameMySqlConnection)
                                });
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    e.printStackTrace();
                    // Không ghi được (DB/ổ đĩa lỗi): bỏ bớt hàng đợi nếu quá dài để không ngốn hết RAM, rồi thử lại sau
                    while (this.historys.Count > MAX_PENDING_HISTORY && this.historys.TryDequeue(out _)) { }
                    Thread.Sleep(10000);
                }
                AutoResetEvent.WaitOne(2000);
            }
        }
        catch (Exception e)
        {
            e.printStackTrace();
        }
    }
}
