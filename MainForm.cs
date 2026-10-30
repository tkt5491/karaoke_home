// ============================================================
//  MainForm.cs  (Tuần 6 — Thin-Client)
//  Chỉ còn: phát video full màn hình Tivi, video chờ (BGM), QR, phím tắt test.
//  Đã bỏ: HttpServer, QueueManager, SessionManager, AdminManager, NASScanner
//         → "não" chuyển lên Backend; Tuần 7 nối SignalR vào đây.
//
//  Quy tắc thread: MỌI logic điều khiển chạy trên UI thread.
//  Event từ MediaPlayer đến từ ThreadPool → đưa về bằng RunOnUi (BeginInvoke).
//  Tuần 7, handler SignalR cũng đi đúng đường này.
//
//  Phím tắt (để nghiệm thu khi chưa có Web App):
//    F1 file cục bộ | F2 file NAS | F3 stream yt-dlp | B bài chờ tiếp theo
//    Space pause    | ← → tua 5s  | ↑ ↓ âm lượng     | F11 bật/tắt full màn hình
// ============================================================

using QRCoder;
using karaoke_home.Player;

namespace karaoke_home
{
    public partial class MainForm : Form
    {
        private readonly TvSettings  _settings;
        private readonly MediaPlayer _player;

        private readonly List<string> _bgmFiles = new();
        private int  _bgmIndex;
        private bool _isPlayingBGM;
        private bool _isFullScreen;

        // ════════════════════════════════════════════════════════════════════
        public MainForm()
        {
            InitializeComponent();

            _settings = TvSettings.Load();
            _player   = new MediaPlayer(panelCenter);

            _player.SongStarted   += (_, title) => RunOnUi(() => OnSongStarted(title));
            _player.SongFinished  += (_, _)     => RunOnUi(OnSongFinished);
            _player.PlaybackError += (_, title) => RunOnUi(() => OnPlaybackError(title));

            LoadBGM();
        }

        // Load chạy khi Form đã có handle → lúc này BeginInvoke mới dùng được
        // (gọi BeginInvoke trong constructor sẽ ném InvalidOperationException)
        private void MainForm_Load(object sender, EventArgs e)
        {
            GenerateQRCode(_settings.WebAppUrl);
            SetFullScreen(true);
            PlayNextBGM();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isFullScreen) Cursor.Show();
            _player.Dispose();
            base.OnFormClosing(e);
        }

        // ════════════════════════════════════════════════════════════════════
        // XỬ LÝ SỰ KIỆN PLAYER (đã ở UI thread)
        // ════════════════════════════════════════════════════════════════════

        private void OnSongStarted(string title)
        {
            if (_isPlayingBGM)
                UpdateUIInfo($"🎧 NHẠC CHỜ: {title}", "Quét QR góc màn hình để chọn bài 🎙");
            else
                UpdateUIInfo(title, "(Tuần 7: lấy từ hàng đợi trên server)");
        }

        private void OnSongFinished()
        {
            Console.WriteLine($"[MainForm] {DateTime.Now:HH:mm:ss} hết bài → quay về nhạc chờ");
            PlayNextBGM();   // Tuần 6 chưa có hàng đợi: hết bài thì về video chờ
        }

        private async void OnPlaybackError(string title)
        {
            UpdateUIInfo($"⚠️ Không phát được: {title}", "Link yt-dlp hết hạn? Kiểm tra tvsettings.json — 3 giây nữa về nhạc chờ");
            await Task.Delay(3000);     // await trên UI thread: không chặn giao diện, chạy tiếp vẫn ở UI thread
            if (!IsDisposed) PlayNextBGM();
        }

        // ════════════════════════════════════════════════════════════════════
        // PHÁT
        // ════════════════════════════════════════════════════════════════════

        private void PlayTest(string name, string source, string? audio = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                UpdateUIInfo($"Chưa cấu hình nguồn {name}", "Điền vào tvsettings.json cạnh file .exe");
                return;
            }

            // URL stream không có "tên file" — cắt bằng Path sẽ ra nguyên chuỗi query (kèm IP public)
            var isUrl = source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var title = isUrl ? $"[{name}] stream" : $"[{name}] {Path.GetFileNameWithoutExtension(source)}";

            _isPlayingBGM = false;
            if (!_player.Play(source, string.IsNullOrWhiteSpace(audio) ? null : audio, title))
                UpdateUIInfo($"⚠️ Không mở được nguồn {name}", isUrl ? "(link stream)" : source);
        }

        private void LoadBGM()
        {
            if (!Directory.Exists(_settings.BgmFolder))
            {
                Console.WriteLine($"[MainForm] Không thấy thư mục BGM: {_settings.BgmFolder}");
                return;
            }

            _bgmFiles.AddRange(Directory.EnumerateFiles(_settings.BgmFolder)
                .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f));

            Console.WriteLine($"[MainForm] {_bgmFiles.Count} video chờ.");
        }

        private void PlayNextBGM()
        {
            if (_bgmFiles.Count == 0)
            {
                _player.Stop();
                UpdateUIInfo("Chưa có video chờ", $"Kiểm tra BgmFolder: {_settings.BgmFolder}");
                return;
            }

            _isPlayingBGM = true;
            var file = _bgmFiles[_bgmIndex];
            _bgmIndex = (_bgmIndex + 1) % _bgmFiles.Count;
            _player.Play(file);
        }

        // ════════════════════════════════════════════════════════════════════
        // PHÍM TẮT — dùng ProcessCmdKey thay vì KeyDown:
        // KeyDown không nhận được phím mũi tên (Windows dùng chúng để chuyển focus).
        // ════════════════════════════════════════════════════════════════════
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:    PlayTest("Local", _settings.LocalTestFile); return true;
                case Keys.F2:    PlayTest("NAS",   _settings.NasTestPath);   return true;
                case Keys.F3:    PlayTest("YouTube", _settings.YtVideoUrl, _settings.YtAudioUrl); return true;
                case Keys.B:     PlayNextBGM();            return true;
                case Keys.Space: _player.PauseOrResume();  return true;
                case Keys.Right: _player.SeekForward();    return true;
                case Keys.Left:  _player.SeekBackward();   return true;
                case Keys.Up:    _player.VolumeUp();       return true;
                case Keys.Down:  _player.VolumeDown();     return true;
                case Keys.F11:   SetFullScreen(!_isFullScreen); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ════════════════════════════════════════════════════════════════════
        // FULL MÀN HÌNH — ưu tiên màn hình phụ (Tivi), không có thì dùng màn hình chính
        // ════════════════════════════════════════════════════════════════════
        private void SetFullScreen(bool on)
        {
            if (on == _isFullScreen) return;   // Cursor.Hide/Show có đếm số lần → phải cân bằng

            if (on)
            {
                var tv = Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;
                WindowState     = FormWindowState.Normal;   // đang Maximized thì đổi Bounds không có tác dụng
                FormBorderStyle = FormBorderStyle.None;
                StartPosition   = FormStartPosition.Manual;
                Bounds          = tv.Bounds;
                WindowState     = FormWindowState.Maximized;
                Cursor.Hide();
            }
            else
            {
                WindowState     = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.Sizable;
                Cursor.Show();
            }

            _isFullScreen = on;
        }

        // ════════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Đưa một việc về UI thread. BeginInvoke (không chờ) thay vì Invoke (chờ):
        /// thread nền không bị treo nếu UI thread đang bận hoặc đang đóng Form.
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (ObjectDisposedException) { }      // Form vừa đóng giữa chừng — bỏ qua
            catch (InvalidOperationException) { }
        }

        private void UpdateUIInfo(string nowPlaying, string next)
        {
            lblNowPlaying.Text = $"Đang phát: {nowPlaying}";
            lblNextSong.Text   = $"Tiếp theo: {next}";
        }

        private void GenerateQRCode(string url)
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var data      = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                using var qr        = new QRCode(data);
                picQRCode.Image     = qr.GetGraphic(5);
                picQRCode.SizeMode  = PictureBoxSizeMode.Zoom;
                Console.WriteLine($"[MainForm] QR → {url}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainForm] Lỗi tạo QR: {ex.Message}");
            }
        }
    }
}
