// ============================================================
//  SessionManager.cs
//  Tạo và validate session token để bảo vệ API khỏi truy cập
//  trái phép từ các thiết bị không quét QR Code.
//
//  Cơ chế hoạt động:
//    - App khởi động → tạo 1 token ngẫu nhiên 8 ký tự hex
//    - Token nhúng vào QR Code: http://[nginx-ip]/[token]/
//    - Mọi API request phải có token trong URL path
//    - Request thiếu token hoặc token sai → 403 Forbidden
//    - Token reset mỗi lần khởi động app (không persist)
//
//  Ví dụ URL hợp lệ:
//    http://192.168.0.230/a3f9bc12/             ← Web UI
//    http://192.168.0.100:8888/a3f9bc12/api/songs ← API qua Windows
//
//  Độ bảo mật:
//    Token 8 ký tự hex = 16^8 = ~4.3 tỷ tổ hợp.
//    Trong mạng LAN tốc độ cao, brute-force 4 tỷ request
//    vẫn tốn nhiều giờ và sẽ làm treo app trước khi đoán được.
//    Đây là mức bảo vệ hợp lý cho use-case karaoke gia đình.
// ============================================================

namespace karaoke_home.Session
{
    public class SessionManager
    {
        // ════════════════════════════════════════════════════════════════════
        // FIELDS
        // ════════════════════════════════════════════════════════════════════

        // Token hiện tại — readonly sau khi khởi tạo, không đổi trong suốt vòng đời app
        private readonly string _token;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR — Tạo token ngay khi khởi tạo
        // ════════════════════════════════════════════════════════════════════

        public SessionManager()
        {
            _token = GenerateToken();
            Console.WriteLine($"[Session] ✅ Token phiên này: {_token}");
            Console.WriteLine($"[Session]    QR sẽ trỏ đến: http://[nginx-ip]/{_token}/");
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Token của phiên hiện tại.
        /// MainForm dùng để tạo URL cho QR Code.
        /// HttpServer dùng để strip khỏi path trước khi route.
        /// </summary>
        public string Token => _token;

        /// <summary>
        /// Kiểm tra xem URL path có chứa đúng token không.
        ///
        /// Path hợp lệ phải bắt đầu bằng "/{token}" hoặc "/{token}/..."
        /// Ví dụ: "/a3f9bc12/api/songs" → hợp lệ
        ///        "/api/songs"           → không hợp lệ → 403
        ///        "/wrongtoken/api/songs"→ không hợp lệ → 403
        /// </summary>
        /// <param name="path">URL path đã lowercase, đã bỏ query string.</param>
        /// <returns>True nếu token đúng.</returns>
        public bool IsValidRequest(string path)
        {
            // Path phải bắt đầu bằng "/token" hoặc "/token/"
            return path.StartsWith($"/{_token}/") || path == $"/{_token}";
        }

        /// <summary>
        /// Bóc token ra khỏi đầu path, trả về phần API path sạch để routing.
        ///
        /// Ví dụ:
        ///   "/a3f9bc12/api/songs"   → "/api/songs"
        ///   "/a3f9bc12/api/queue"   → "/api/queue"
        ///   "/a3f9bc12"             → ""  (root, dùng cho health check)
        /// </summary>
        /// <param name="path">Path đã validate bằng IsValidRequest().</param>
        public string StripToken(string path)
        {
            string prefix = $"/{_token}";

            if (path.StartsWith(prefix))
            {
                string stripped = path[prefix.Length..]; // Bỏ "/token" ở đầu
                return stripped.Length > 0 ? stripped : "/";
            }

            return path; // Fallback — không nên xảy ra nếu đã gọi IsValidRequest trước
        }

        // ════════════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tạo token ngẫu nhiên 8 ký tự hex dùng Guid làm nguồn entropy.
        /// Guid.NewGuid() dùng RNG mạnh của OS — đủ ngẫu nhiên cho use-case này.
        ///
        /// Ví dụ output: "a3f9bc12", "00e7d1f4", "cafe2048"
        /// </summary>
        private static string GenerateToken()
        {
            // Lấy 4 bytes đầu của GUID → 8 ký tự hex
            // Đủ ngắn để URL dễ đọc, đủ dài để không đoán được
            return Guid.NewGuid().ToString("N")[..8];
        }
    }
}