using karaoke_home.NAS;
using karaoke_home.Player;
using karaoke_home.Queue;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace karaoke_home.Web
{
    public class HttpServer
    {
        private HttpListener _listener;
        private Thread _serverThread;
        private MediaPlayer _player;
        private QueueManager _queue;
        private NASScanner _scanner;

        // Nhận tham chiếu Player và Queue từ MainForm truyền vào để điều khiển
        public HttpServer(MediaPlayer player, QueueManager queue, NASScanner scanner)
        {
            _player = player;
            _queue = queue;
            _scanner = scanner;
            _listener = new HttpListener();
        }

        public void Start(string port = "8888")
        {
            try
            {
                // Lắng nghe trên mọi IP của máy Windows ở cổng 8888
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Start();
                Console.WriteLine($"[Web Server] Đã khởi chạy tại cổng {port}");

                // Chạy vòng lặp lắng nghe trên một luồng ngầm (Background Thread)
                _serverThread = new Thread(Listen);
                _serverThread.IsBackground = true;
                _serverThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lỗi Server] Không thể khởi động: {ex.Message}");
                // Lưu ý: Cần chạy Visual Studio bằng quyền Administrator để HttpListener cho phép mở cổng mạng LAN.
            }
        }

        private void Listen()
        {
            while (_listener.IsListening)
            {
                try
                {
                    // Chờ đến khi có một thiết bị (điện thoại) gửi Request tới
                    HttpListenerContext context = _listener.GetContext();
                    ProcessRequest(context);
                }
                catch (HttpListenerException) { /* Bỏ qua lỗi khi tắt Form */ }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            string url = request.RawUrl;
            string method = request.HttpMethod;

            Console.WriteLine($"[Web Server] Đang xử lý: {method} {url}");

            // BẬT ĐÈN XANH CHO CORS (Rất quan trọng)
            response.Headers.Add("Access-Control-Allow-Origin", "*"); // Cho phép mọi Web gọi vào
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

            // Xử lý request OPTIONS (Preflight request của trình duyệt)
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }
            try
            {
                // ========================================================
                // 2. ROUTING DỮ LIỆU (API TRẢ VỀ JSON CHO JAVASCRIPT)
                // ========================================================
                if (url.StartsWith("/api/queue") && method == "GET")
                {
                    // Lấy danh sách đang chờ từ QueueManager
                    var currentQueue = _queue.GetCurrentQueue();

                    // Biến đổi List<Song> thành chuỗi JSON để Web hiểu được
                    string json = JsonSerializer.Serialize(currentQueue);
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);

                    response.ContentType = "application/json; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                // ========================================================
                // API: Lấy toàn bộ danh sách bài hát từ NAS
                // ========================================================
                else if (url.StartsWith("/api/songs") && method == "GET")
                {
                    var allSongs = _scanner.ScanSongs();
                    string json = JsonSerializer.Serialize(allSongs);
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);

                    response.ContentType = "application/json; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }

                // ========================================================
                // API: Thêm bài hát vào hàng chờ (Order / Ưu tiên)
                // ========================================================
                else if (url.StartsWith("/api/queue/add") && method == "POST")
                {
                    // Đọc đường dẫn file từ URL
                    string filePath = request.QueryString["path"];
                    bool isPriority = url.Contains("add_priority"); // Kiểm tra xem có phải lệnh ưu tiên không

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var allSongs = _scanner.ScanSongs();
                        var song = allSongs.FirstOrDefault(s => s.FilePath == filePath);

                        if (song != null)
                        {
                            if (isPriority) _queue.PrioritySong(song);
                            else _queue.AddSong(song);
                        }
                    }
                    response.StatusCode = 200;
                }
                
                // ========================================================
                // 3. ROUTING ĐIỀU KHIỂN (NHẬN LỆNH POST TỪ ĐIỆN THOẠI)
                // ========================================================
                else if (url.StartsWith("/api/player/pause") && method == "POST")
                {
                    _player.PauseOrResume(); // Ra lệnh cho VLC tạm dừng/tiếp tục
                    response.StatusCode = 200; // Trả về 200 OK báo hiệu thành công
                }
                else if (url.StartsWith("/api/player/voldown") && method == "POST")
                {
                    _player.VolumeDown();
                    response.StatusCode = 200;
                }
                else if (url.StartsWith("/api/player/volup") && method == "POST")
                {
                    _player.VolumeUp();
                    response.StatusCode = 200;
                }
                // (Sau này bạn có thể bổ sung thêm if/else cho Next, Seek, Priority...)
                else if (url.StartsWith("/api/queue/priority") && method == "POST")
                {
                    int index = int.Parse(request.QueryString["index"]);
                    var queue = _queue.GetCurrentQueue();

                    if (index >= 0 && index < queue.Count)
                    {
                        var song = queue[index];
                        _queue.RemoveSong(index);
                        _queue.PrioritySong(song);
                    }

                    response.StatusCode = 200;
                }
                else if (url.StartsWith("/api/queue/remove") && method == "POST")
                {
                    int index = int.Parse(request.QueryString["index"]);
                    _queue.RemoveSong(index);
                    response.StatusCode = 200;
                }
                else if (url.StartsWith("/api/player/next") && method == "POST")
                {
                    _player.Stop();
                    response.StatusCode = 200;
                }

                else
                {
                    // Nếu gửi một đường link tào lao không có trong cấu hình
                    response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lỗi API] {ex.Message}");
                response.StatusCode = 500; // Lỗi server nội bộ
            }
            finally
            {
                response.Close(); // Luôn luôn phải đóng response để giải phóng kết nối
            }
        }

        public void Stop()
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }

    }
}