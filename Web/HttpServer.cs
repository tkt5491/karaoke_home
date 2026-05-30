// ============================================================
//  HttpServer.cs
//  "Bộ não giao tiếp với điện thoại" — REST API chạy ngay
//  trong WinForms app, lắng nghe ở cổng 8888.
//
//  Sơ đồ luồng:
//    [Điện thoại] → HTTP Request
//        → Listen() (background thread)
//        → ProcessRequest()
//        → HandleXxx() tương ứng
//        → [Player | Queue | Scanner]
//        → HTTP Response (JSON hoặc status code)
//
//  API hiện có (tất cả đều yêu cầu token trong path):
//    GET  /{token}/api/songs              — Danh sách toàn bộ thư viện (cache)
//    GET  /{token}/api/queue              — Hàng chờ hiện tại
//    GET  /{token}/api/player/status      — Trạng thái player
//    POST /{token}/api/queue/add          — Order bài (?path=...)
//    POST /{token}/api/queue/add_priority — Order bài ưu tiên (?path=...)
//    POST /{token}/api/queue/remove       — Xóa bài khỏi hàng chờ (?index=...)
//    POST /{token}/api/queue/promote      — Nâng bài lên đầu hàng (?index=...)
//    POST /{token}/api/player/next        — Bỏ qua bài đang phát
//    POST /{token}/api/player/pause       — Tạm dừng / Tiếp tục
//    POST /{token}/api/player/volup       — Tăng âm lượng
//    POST /{token}/api/player/voldown     — Giảm âm lượng
//
//  Bảo mật:
//    Request thiếu token hoặc token sai → 403 Forbidden ngay lập tức.
//    Token được tạo bởi SessionManager khi app khởi động.
//
//  Lưu ý triển khai:
//    - Chạy Visual Studio bằng quyền Administrator để
//      HttpListener được phép mở cổng LAN (http://+:8888/).
//    - Gọi Stop() trong FormClosing của MainForm.
// ============================================================

using karaoke_home.Admin;
using karaoke_home.NAS;
using karaoke_home.Player;
using karaoke_home.Queue;
using karaoke_home.Session;
using System.Net;
using System.Text;
using System.Text.Json;

namespace karaoke_home.Web
{
    public class HttpServer
    {
        // ════════════════════════════════════════════════════════════════════
        // FIELDS — Các tham chiếu nội bộ
        // ════════════════════════════════════════════════════════════════════

        private readonly HttpListener _listener;
        private Thread? _serverThread;

        private readonly MediaPlayer _player;
        private readonly QueueManager _queue;
        private readonly NASScanner _scanner;

        // Session: validate token trước mọi request
        private readonly SessionManager _session;

        // Admin: xác thực và thực thi lệnh quyền cao
        private readonly AdminManager _admin;

        /// <summary>
        /// Callback để HttpServer yêu cầu MainForm chuyển bài tiếp theo.
        /// Dùng cho /api/player/next vì chỉ MainForm mới biết logic BGM / queue.
        /// Đăng ký trong MainForm: _server.OnNextRequested += PlayNextSongInQueue;
        /// </summary>
        public event Action? OnNextRequested;

        // JsonSerializer options dùng chung — tạo 1 lần, tái sử dụng
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase // { title, artist, filePath }
        };

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Nhận tham chiếu từ MainForm — không khởi động server ngay,
        /// phải gọi Start() để bắt đầu lắng nghe.
        /// </summary>
        public HttpServer(MediaPlayer player, QueueManager queue, NASScanner scanner, SessionManager session, AdminManager admin)
        {
            _player = player;
            _queue = queue;
            _scanner = scanner;
            _session = session;
            _admin = admin;
            _listener = new HttpListener();
        }

        // ════════════════════════════════════════════════════════════════════
        // LIFECYCLE — Khởi động / Tắt server
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Khởi động HTTP server trên port chỉ định.
        /// Server chạy trên background thread — không block UI.
        /// </summary>
        public void Start(string port = "8888")
        {
            try
            {
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Start();
                Console.WriteLine($"[HttpServer] ✅ Đang lắng nghe tại cổng {port}");

                _serverThread = new Thread(ListenLoop);
                _serverThread.IsBackground = true; // Tự tắt khi app đóng
                _serverThread.Name = "HttpServer-Listen";
                _serverThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpServer] ❌ Không thể khởi động: {ex.Message}");
                Console.WriteLine("[HttpServer]    → Hãy chạy Visual Studio bằng quyền Administrator.");
            }
        }

        /// <summary>
        /// Dừng server. Gọi trong MainForm.OnFormClosing() để tránh port bị giữ.
        /// </summary>
        public void Stop()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
                Console.WriteLine("[HttpServer] Server đã dừng.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // LISTEN LOOP — Vòng lặp nhận request (chạy trên background thread)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Vòng lặp chính: chờ request → xử lý từng request trên thread pool.
        /// Mỗi request được xử lý độc lập để không block request tiếp theo.
        /// </summary>
        private void ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    // GetContext() block thread này cho đến khi có request mới
                    var context = _listener.GetContext();

                    // Xử lý mỗi request trên thread pool riêng
                    // → nhiều điện thoại gọi cùng lúc không bị xếp hàng chờ nhau
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Xảy ra khi Stop() được gọi — bình thường, bỏ qua
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HttpServer] Lỗi listen loop: {ex.Message}");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // REQUEST PROCESSING — Phân loại và điều hướng request
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Điểm vào cho mọi request. Gắn CORS header, xử lý preflight OPTIONS,
        /// sau đó điều hướng đến handler tương ứng theo URL.
        /// </summary>
        private void ProcessRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            // Normalize URL: bỏ query string để so sánh route
            string rawUrl = req.RawUrl ?? "/";
            string path = rawUrl.Split('?')[0].TrimEnd('/').ToLowerInvariant();
            string method = req.HttpMethod.ToUpperInvariant();

            Console.WriteLine($"[HttpServer] {method} {rawUrl}");

            // --- CORS: bắt buộc để trình duyệt/điện thoại được gọi cross-origin ---
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            // Trình duyệt gửi OPTIONS "preflight" trước mọi POST cross-origin
            if (method == "OPTIONS")
            {
                res.StatusCode = 200;
                res.Close();
                return;
            }

            // ── SESSION VALIDATION ────────────────────────────────────────
            // Kiểm tra token trước khi làm bất cứ điều gì khác.
            // Request không có token đúng → 403 ngay, không tiết lộ thông tin gì thêm.
            if (!_session.IsValidRequest(path))
            {
                Console.WriteLine($"[HttpServer] 🚫 403 Forbidden — token không hợp lệ: {path}");
                res.StatusCode = 403;
                WriteJson(res, new { error = "Forbidden. Vui lòng quét QR Code để truy cập." });
                try { res.Close(); } catch { }
                return;
            }

            // Bóc token ra khỏi path để routing bên dưới không cần biết về token
            // "/a3f9bc12/api/songs" → "/api/songs"
            path = _session.StripToken(path);

            try
            {
                // ── Điều hướng theo path ──────────────────────────────────
                bool handled = (path, method) switch
                {
                    // ── Thư viện & Queue ──────────────────────────────────
                    ("/api/songs", "GET") => Handle_GetSongs(req, res),
                    ("/api/queue", "GET") => Handle_GetQueue(req, res),
                    ("/api/player/status", "GET") => Handle_GetStatus(req, res),
                    ("/api/queue/add", "POST") => Handle_QueueAdd(req, res, priority: false),
                    ("/api/queue/add_priority", "POST") => Handle_QueueAdd(req, res, priority: true),
                    ("/api/queue/remove", "POST") => Handle_QueueRemove(req, res),
                    ("/api/queue/promote", "POST") => Handle_QueuePromote(req, res),
                    // ── Player ───────────────────────────────────────────
                    ("/api/player/next", "POST") => Handle_PlayerNext(req, res),
                    ("/api/player/pause", "POST") => Handle_PlayerPause(req, res),
                    ("/api/player/volup", "POST") => Handle_PlayerVolUp(req, res),
                    ("/api/player/voldown", "POST") => Handle_PlayerVolDown(req, res),
                    // ── Admin (yêu cầu thêm X-Admin-Token header) ────────
                    ("/admin/login", "POST") => Handle_AdminLogin(req, res),
                    ("/admin/logout", "POST") => Handle_AdminLogout(req, res),
                    ("/admin/restart-pc", "POST") => Handle_AdminRestartPC(req, res),
                    ("/admin/restart-session", "POST") => Handle_AdminRestartSession(req, res),
                    ("/admin/shutdown-app", "POST") => Handle_AdminShutdownApp(req, res),
                    _ => false
                };

                if (!handled)
                {
                    res.StatusCode = 404;
                    WriteJson(res, new { error = $"Route không tồn tại: {method} {path}" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpServer] ❌ Lỗi xử lý request: {ex.Message}");
                res.StatusCode = 500;
                WriteJson(res, new { error = "Lỗi server nội bộ.", detail = ex.Message });
            }
            finally
            {
                // LUÔN đóng response để giải phóng kết nối, dù có lỗi hay không
                try { res.Close(); } catch { /* bỏ qua */ }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // HANDLERS — GET (Trả dữ liệu về điện thoại)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// GET /api/songs
        /// Trả về toàn bộ thư viện bài hát từ NAS (dùng cache của NASScanner).
        /// Điện thoại gọi 1 lần lúc mở app — không gọi lại mỗi khi order bài.
        /// </summary>
        private bool Handle_GetSongs(HttpListenerRequest req, HttpListenerResponse res)
        {
            var songs = _scanner.GetSongs(); // Dùng cache, không scan lại mỗi request
            WriteJson(res, songs);
            return true;
        }

        /// <summary>
        /// GET /api/queue
        /// Trả về hàng chờ hiện tại để điện thoại hiển thị danh sách đang chờ.
        /// QueueManager.GetCurrentQueue() trả clone — thread-safe.
        /// </summary>
        private bool Handle_GetQueue(HttpListenerRequest req, HttpListenerResponse res)
        {
            var queue = _queue.GetCurrentQueue();
            WriteJson(res, queue);
            return true;
        }

        /// <summary>
        /// GET /api/player/status
        /// Trả về trạng thái hiện tại của player để điện thoại hiển thị
        /// "Now Playing", volume, trạng thái play/pause...
        /// </summary>
        private bool Handle_GetStatus(HttpListenerRequest req, HttpListenerResponse res)
        {
            var status = new
            {
                // Mở rộng sau: thêm CurrentSong, IsPlaying, Position, Duration...
                queueCount = _queue.Count,
                isFull = _queue.IsFull,
                isEmpty = _queue.IsEmpty,
            };
            WriteJson(res, status);
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // HANDLERS — POST Queue (Quản lý hàng chờ)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// POST /api/queue/add?path=...
        /// POST /api/queue/add_priority?path=...
        ///
        /// Tìm bài hát trong cache theo đường dẫn file, thêm vào hàng chờ.
        /// Tham số priority=true → chèn lên đầu (phát ngay sau bài hiện tại).
        ///
        /// Response 200: thêm thành công
        /// Response 400: thiếu param hoặc không tìm thấy bài
        /// Response 429: hàng chờ đã đầy
        /// </summary>
        private bool Handle_QueueAdd(HttpListenerRequest req, HttpListenerResponse res, bool priority)
        {
            string? filePath = req.QueryString["path"];

            if (string.IsNullOrWhiteSpace(filePath))
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = "Thiếu tham số 'path'." });
                return true;
            }

            // Tìm bài trong cache — không scan lại NAS
            var library = _scanner.GetSongs();
            var song = library.FirstOrDefault(s => s.FilePath == filePath);

            if (song == null)
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = $"Không tìm thấy bài hát: {filePath}" });
                return true;
            }

            bool added = priority ? _queue.PrioritySong(song) : _queue.AddSong(song);

            if (!added)
            {
                // QueueManager trả false khi hàng chờ đầy (vượt MaxQueueSize)
                res.StatusCode = 429; // Too Many Requests
                WriteJson(res, new { error = "Hàng chờ đã đầy. Vui lòng thử lại sau." });
                return true;
            }

            res.StatusCode = 200;
            WriteJson(res, new { message = $"Đã thêm: {song.Title}", priority });
            return true;
        }

        /// <summary>
        /// POST /api/queue/remove?index=...
        /// Xóa bài tại vị trí index khỏi hàng chờ.
        /// </summary>
        private bool Handle_QueueRemove(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!TryParseIndex(req, res, out int index)) return true;

            bool removed = _queue.RemoveSong(index);

            if (!removed)
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = $"Index {index} không hợp lệ." });
                return true;
            }

            res.StatusCode = 200;
            WriteJson(res, new { message = $"Đã xóa bài tại vị trí {index}." });
            return true;
        }

        /// <summary>
        /// POST /api/queue/promote?index=...
        /// Nâng bài tại vị trí index lên đầu hàng chờ (phát ngay sau bài hiện tại).
        /// Thay thế pattern cũ: remove rồi insert — nay gom vào 1 lệnh atomic.
        /// </summary>
        private bool Handle_QueuePromote(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!TryParseIndex(req, res, out int index)) return true;

            bool promoted = _queue.PromoteSong(index);

            if (!promoted)
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = $"Không thể nâng ưu tiên: index {index} không hợp lệ hoặc đã ở đầu hàng." });
                return true;
            }

            res.StatusCode = 200;
            WriteJson(res, new { message = $"Đã nâng bài tại vị trí {index} lên đầu hàng." });
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // HANDLERS — POST Player (Điều khiển player)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// POST /api/player/next
        /// Yêu cầu MainForm chuyển sang bài tiếp theo trong queue (hoặc BGM).
        ///
        /// ⚠ KHÔNG gọi _player.Stop() trực tiếp ở đây!
        ///    VLC.Stop() không trigger EndReached event → bài tiếp theo không tự phát.
        ///    Thay vào đó: raise event OnNextRequested để MainForm xử lý đúng logic.
        /// </summary>
        private bool Handle_PlayerNext(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (OnNextRequested == null)
            {
                res.StatusCode = 501; // Not Implemented
                WriteJson(res, new { error = "OnNextRequested chưa được đăng ký từ MainForm." });
                return true;
            }

            // Gọi trên ThreadPool để không block HTTP response
            ThreadPool.QueueUserWorkItem(_ => OnNextRequested?.Invoke());

            res.StatusCode = 200;
            WriteJson(res, new { message = "Đang chuyển bài tiếp theo..." });
            return true;
        }

        /// <summary>
        /// POST /api/player/pause
        /// Toggle tạm dừng / tiếp tục bài đang phát.
        /// </summary>
        private bool Handle_PlayerPause(HttpListenerRequest req, HttpListenerResponse res)
        {
            _player.PauseOrResume();
            res.StatusCode = 200;
            WriteJson(res, new { message = "Đã toggle Pause/Resume." });
            return true;
        }

        /// <summary>
        /// POST /api/player/volup
        /// Tăng âm lượng 1 nấc (+5). Giới hạn tối đa trong MediaPlayer.
        /// </summary>
        private bool Handle_PlayerVolUp(HttpListenerRequest req, HttpListenerResponse res)
        {
            _player.VolumeUp();
            res.StatusCode = 200;
            WriteJson(res, new { message = "Âm lượng tăng." });
            return true;
        }

        /// <summary>
        /// POST /api/player/voldown
        /// Giảm âm lượng 1 nấc (-5). Giới hạn tối thiểu trong MediaPlayer.
        /// </summary>
        private bool Handle_PlayerVolDown(HttpListenerRequest req, HttpListenerResponse res)
        {
            _player.VolumeDown();
            res.StatusCode = 200;
            WriteJson(res, new { message = "Âm lượng giảm." });
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // HANDLERS — Admin (Quyền quản trị)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// POST /admin/login
        /// Body JSON: { "username": "admin", "password": "admin" }
        ///
        /// Xác thực admin credentials. Thành công → trả adminToken.
        /// Web UI lưu adminToken vào memory, đính vào header "X-Admin-Token"
        /// cho mọi lệnh admin tiếp theo.
        ///
        /// Response 200: { "adminToken": "abc123def456" }
        /// Response 401: credentials sai
        /// </summary>
        private bool Handle_AdminLogin(HttpListenerRequest req, HttpListenerResponse res)
        {
            // Đọc body JSON
            string body;
            using (var reader = new System.IO.StreamReader(req.InputStream, System.Text.Encoding.UTF8))
                body = reader.ReadToEnd();

            string username = "", password = "";
            try
            {
                var doc = JsonDocument.Parse(body);
                username = doc.RootElement.GetProperty("username").GetString() ?? "";
                password = doc.RootElement.GetProperty("password").GetString() ?? "";
            }
            catch
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = "Body JSON không hợp lệ. Cần { username, password }." });
                return true;
            }

            string? adminToken = _admin.TryLogin(username, password);

            if (adminToken == null)
            {
                res.StatusCode = 401;
                WriteJson(res, new { error = "Sai tên đăng nhập hoặc mật khẩu." });
                return true;
            }

            res.StatusCode = 200;
            WriteJson(res, new { adminToken, message = "Đăng nhập thành công." });
            return true;
        }

        /// <summary>
        /// POST /admin/logout
        /// Header: X-Admin-Token: ...
        /// Đăng xuất admin, xóa token hiện tại.
        /// </summary>
        private bool Handle_AdminLogout(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!ValidateAdminToken(req, res)) return true;

            _admin.Logout();
            res.StatusCode = 200;
            WriteJson(res, new { message = "Đã đăng xuất admin." });
            return true;
        }

        /// <summary>
        /// POST /admin/restart-pc
        /// Header: X-Admin-Token: ...
        ///
        /// [Nút 1] Khởi động lại toàn bộ máy Windows sau 5 giây.
        /// Dùng khi app bị treo hoàn toàn hoặc máy cần reset cứng.
        /// </summary>
        private bool Handle_AdminRestartPC(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!ValidateAdminToken(req, res)) return true;

            Console.WriteLine("[HttpServer] 🔄 Admin yêu cầu RESTART PC");
            _admin.RestartPC();

            res.StatusCode = 200;
            WriteJson(res, new { message = "Máy tính sẽ khởi động lại sau 5 giây." });
            return true;
        }

        /// <summary>
        /// POST /admin/restart-session
        /// Header: X-Admin-Token: ...
        ///
        /// [Nút 2] Reset phiên hát: dừng bài, xóa queue, phát BGM.
        /// Dùng khi bài bị đứng giữa chừng hoặc queue rối mà không muốn tắt app.
        /// </summary>
        private bool Handle_AdminRestartSession(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!ValidateAdminToken(req, res)) return true;

            Console.WriteLine("[HttpServer] 🔁 Admin yêu cầu RESTART SESSION");
            _admin.RestartSession(); // Raise event → MainForm xử lý

            res.StatusCode = 200;
            WriteJson(res, new { message = "Phiên hát đã được reset. Đang phát BGM..." });
            return true;
        }

        /// <summary>
        /// POST /admin/shutdown-app
        /// Header: X-Admin-Token: ...
        ///
        /// [Nút 3] Tắt ứng dụng karaoke (không tắt máy).
        /// Dùng khi muốn đóng app từ xa không cần vào trực tiếp máy Windows.
        /// </summary>
        private bool Handle_AdminShutdownApp(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!ValidateAdminToken(req, res)) return true;

            Console.WriteLine("[HttpServer] ⏹ Admin yêu cầu SHUTDOWN APP");

            res.StatusCode = 200;
            WriteJson(res, new { message = "Ứng dụng karaoke đang tắt..." });

            // Đóng response trước rồi mới tắt app
            // (nếu tắt app trước thì response không kịp gửi về)
            try { res.Close(); } catch { }
            _admin.ShutdownApp(); // Raise event → MainForm.Application.Exit()
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // ADMIN HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Đọc X-Admin-Token từ header và validate qua AdminManager.
        /// Ghi lỗi 401 vào response nếu token thiếu hoặc hết hạn.
        /// Trả true nếu hợp lệ, false nếu không (caller return true ngay).
        /// </summary>
        private bool ValidateAdminToken(HttpListenerRequest req, HttpListenerResponse res)
        {
            string? token = req.Headers["X-Admin-Token"];

            if (!_admin.IsValidAdminToken(token))
            {
                Console.WriteLine("[HttpServer] 🚫 Admin action bị từ chối — token không hợp lệ hoặc hết hạn.");
                res.StatusCode = 401;
                WriteJson(res, new { error = "Unauthorized. Vui lòng đăng nhập admin trước." });
                return false;
            }

            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS — Các hàm tiện ích dùng chung trong class
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Serialize object thành JSON và ghi vào HTTP response.
        /// Dùng UTF-8, content-type application/json.
        /// </summary>
        private static void WriteJson(HttpListenerResponse res, object data)
        {
            string json = JsonSerializer.Serialize(data, _jsonOptions);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = buffer.Length;

            try
            {
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpServer] WriteJson thất bại: {ex.Message}");
            }
        }

        /// <summary>
        /// Đọc và validate tham số "index" từ QueryString.
        /// Nếu thiếu hoặc không phải số nguyên → ghi lỗi 400 vào response và trả false.
        /// </summary>
        private static bool TryParseIndex(
            HttpListenerRequest req,
            HttpListenerResponse res,
            out int index)
        {
            string? raw = req.QueryString["index"];

            if (!int.TryParse(raw, out index))
            {
                res.StatusCode = 400;
                WriteJson(res, new { error = $"Tham số 'index' không hợp lệ: '{raw}'. Phải là số nguyên >= 0." });
                return false;
            }

            return true;
        }
    }
}