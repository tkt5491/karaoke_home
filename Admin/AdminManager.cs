// ============================================================
//  AdminManager.cs
//  "Quyền quản trị" — xác thực admin và thực thi các lệnh
//  có quyền hạn cao hơn user thông thường.
//
//  Luồng hoạt động (theo wireframe):
//    [Web UI] → Nhấn nút Admin
//        → POST /{token}/admin/login  { username, password }
//        → AdminManager.TryLogin()
//        → Thành công → trả adminToken (khác session token)
//        → [Admin Panel] hiện ra 3 nút:
//            POST /{token}/admin/restart-pc       → Khởi động lại máy Windows
//            POST /{token}/admin/restart-session  → Reset phiên hát (queue + player)
//            POST /{token}/admin/shutdown-app     → Tắt ứng dụng karaoke
//
//  Bảo mật 2 lớp:
//    Lớp 1 — Session token trong URL (SessionManager): chặn người lạ
//    Lớp 2 — Admin password: chặn user thường leo quyền
//
//  Admin token:
//    Sau khi login thành công, server trả về một adminToken ngắn hạn.
//    Mọi lệnh admin phải kèm adminToken trong header "X-Admin-Token".
//    Token hết hạn sau AdminTokenTtlMinutes phút không hoạt động.
//    → Không để điện thoại ai đó đang mở trình duyệt tự nhiên có quyền admin.
// ============================================================

namespace karaoke_home.Admin
{
    public class AdminManager
    {
        // ════════════════════════════════════════════════════════════════════
        // CẤU HÌNH — Thay đổi tại đây, không hard-code rải rác
        // ════════════════════════════════════════════════════════════════════

        // Thông tin đăng nhập admin
        // TODO: Sau này có thể đọc từ config file hoặc environment variable
        private const string AdminUsername = "admin";
        private const string AdminPassword = "admin";

        // Admin token hết hạn sau X phút không dùng (idle timeout)
        private const int AdminTokenTtlMinutes = 10;

        // ════════════════════════════════════════════════════════════════════
        // FIELDS — Trạng thái admin session
        // ════════════════════════════════════════════════════════════════════

        private string? _adminToken = null;    // Token hiện tại, null = chưa ai đăng nhập
        private DateTime _tokenExpiresAt = DateTime.MinValue;
        private readonly object _lock = new();

        // Callback để AdminManager yêu cầu MainForm thực hiện hành động
        // (MainForm là owner của Form nên phải Invoke về UI thread)
        public event Action? OnRestartSessionRequested;
        public event Action? OnShutdownAppRequested;

        // ════════════════════════════════════════════════════════════════════
        // AUTHENTICATION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Kiểm tra username/password.
        /// Thành công → tạo admin token mới, trả về token.
        /// Thất bại → trả null.
        /// </summary>
        public string? TryLogin(string username, string password)
        {
            // So sánh case-sensitive để bảo mật tốt hơn
            if (username != AdminUsername || password != AdminPassword)
            {
                Console.WriteLine($"[Admin] ❌ Đăng nhập thất bại — username: '{username}'");
                return null;
            }

            lock (_lock)
            {
                // Tạo admin token mới mỗi lần login (invalidate token cũ)
                _adminToken = Guid.NewGuid().ToString("N")[..12]; // 12 ký tự hex
                _tokenExpiresAt = DateTime.UtcNow.AddMinutes(AdminTokenTtlMinutes);
            }

            Console.WriteLine($"[Admin] ✅ Đăng nhập thành công — token: {_adminToken}, hết hạn sau {AdminTokenTtlMinutes} phút");
            return _adminToken;
        }

        /// <summary>
        /// Kiểm tra admin token từ request header "X-Admin-Token".
        /// Tự động gia hạn timeout mỗi lần dùng thành công (idle timeout).
        /// </summary>
        public bool IsValidAdminToken(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            lock (_lock)
            {
                if (_adminToken == null || token != _adminToken) return false;
                if (DateTime.UtcNow > _tokenExpiresAt)
                {
                    // Token hết hạn → xóa, bắt login lại
                    Console.WriteLine("[Admin] ⏰ Admin token đã hết hạn.");
                    _adminToken = null;
                    return false;
                }

                // Gia hạn idle timeout mỗi lần dùng
                _tokenExpiresAt = DateTime.UtcNow.AddMinutes(AdminTokenTtlMinutes);
                return true;
            }
        }

        /// <summary>
        /// Đăng xuất admin — xóa token hiện tại.
        /// </summary>
        public void Logout()
        {
            lock (_lock)
            {
                _adminToken = null;
                Console.WriteLine("[Admin] 🔓 Đã đăng xuất admin.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ADMIN ACTIONS — Các lệnh quyền cao
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Nút 1] Restart PC — khởi động lại máy Windows ngay lập tức.
        ///
        /// Dùng khi: app bị treo hoàn toàn, VLC crash, máy lag nặng.
        /// Thực thi: shutdown /r /t 5 — Windows restart sau 5 giây
        /// (5 giây để HTTP response kịp trả về cho điện thoại trước khi máy tắt)
        /// </summary>
        public void RestartPC()
        {
            Console.WriteLine("[Admin] 🔄 RESTART PC được yêu cầu — khởi động lại sau 5 giây...");

            // Chạy lệnh Windows shutdown trên background thread
            // để không block HTTP response
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 5 /c \"Karaoke Admin: Restart PC\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Admin] Lỗi RestartPC: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// [Nút 2] Restart Phiên hát — dừng bài đang phát, xóa queue, bắt đầu lại từ đầu.
        ///
        /// Dùng khi: bài bị đứng giữa chừng, queue rối, muốn reset không cần tắt app.
        /// Thực thi: raise event → MainForm xử lý (dừng player + clear queue + play BGM)
        /// </summary>
        public void RestartSession()
        {
            Console.WriteLine("[Admin] 🔁 RESTART SESSION được yêu cầu — reset queue và player...");

            // Raise event trên ThreadPool — MainForm sẽ Invoke về UI thread
            ThreadPool.QueueUserWorkItem(_ => OnRestartSessionRequested?.Invoke());
        }

        /// <summary>
        /// [Nút 3] Chỉ tắt ứng dụng karaoke — không restart máy.
        ///
        /// Dùng khi: muốn tắt app sạch sẽ từ xa mà không vào trực tiếp máy Windows.
        /// Thực thi: raise event → MainForm gọi Application.Exit() trên UI thread.
        /// </summary>
        public void ShutdownApp()
        {
            Console.WriteLine("[Admin] ⏹ SHUTDOWN APP được yêu cầu — đóng ứng dụng...");

            ThreadPool.QueueUserWorkItem(_ => OnShutdownAppRequested?.Invoke());
        }
    }
}