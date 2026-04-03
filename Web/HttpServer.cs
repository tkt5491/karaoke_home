using System;
using System.Net;
using System.Threading;
using karaoke_home.Player;
using karaoke_home.Queue;

namespace karaoke_home.Web
{
    public class HttpServer
    {
        private HttpListener _listener;
        private Thread _serverThread;
        private MediaPlayer _player;
        private QueueManager _queue;

        // Nhận tham chiếu Player và Queue từ MainForm truyền vào để điều khiển
        public HttpServer(MediaPlayer player, QueueManager queue)
        {
            _player = player;
            _queue = queue;
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

            // In ra Console để debug xem điện thoại đang gọi tới đường dẫn nào
            Console.WriteLine($"[Request] {request.HttpMethod} {request.RawUrl}");

            // TODO: Sẽ viết logic phân luồng (Routing) ở đây
            // Ví dụ: Nếu URL là "/player/pause" -> gọi _player.PauseOrResume()

            // Tạm thời trả về HTTP 200 OK
            response.StatusCode = 200;
            response.Close();
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