using QRCoder;
using karaoke_home.Admin;
using karaoke_home.Player;
using karaoke_home.Queue;
using karaoke_home.NAS;
using karaoke_home.Session;
using karaoke_home.Web;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace karaoke_home
{
    public partial class MainForm : Form
    {
        // ════════════════════════════════════════════════════════════════════
        // FIELDS
        // ════════════════════════════════════════════════════════════════════

        private MediaPlayer _player;
        private QueueManager _queue;
        private NASScanner _scanner;
        private HttpServer _server;
        private SessionManager _session;   // ← MỚI: quản lý session token cho QR
        private AdminManager _admin;      // ← MỚI: quản lý quyền admin

        // BGM system
        private List<Song> _bgmList = new List<Song>();
        private int _bgmIndex = 0;
        private bool _isPlayingBGM = false;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════

        public MainForm()
        {
            InitializeComponent();

            // ── Bước 1: Tạo Session token trước tiên (dùng cho QR và HttpServer)
            _session = new SessionManager();

            // ── Bước 2: Tạo AdminManager và đăng ký các event quyền cao
            _admin = new AdminManager();
            _admin.OnRestartSessionRequested += Admin_RestartSession;
            _admin.OnShutdownAppRequested += Admin_ShutdownApp;

            // ── Bước 3: Khởi tạo các thành phần chính
            _queue = new QueueManager();
            _scanner = new NASScanner();
            _player = new MediaPlayer(panelCenter);

            // ── Bước 4: Đăng ký event chuyển bài khi bài hát kết thúc
            _player.SongFinished += Player_SongFinished;
            _player.SongStarted += (_, song) => {
                var upNext = _queue.PeekNextSong();
                UpdateUIInfo(song.Title, upNext?.Title ?? "(Trống)");
            };

            // ── Bước 5: Load BGM
            LoadBGM();

            // ── Bước 6: Khởi động HttpServer với đầy đủ dependencies
            _server = new HttpServer(_player, _queue, _scanner, _session, _admin);
            _server.OnNextRequested += PlayNextSongInQueue; // Fix bug: next từ web
            _server.Start("8888");

            // ── Bước 7: Tạo QR Code trỏ đến URL có session token
            SetupUI();

            // ── Bước 8: Bắt đầu phát (BGM nếu queue trống)
            PlayNextSongInQueue();
            //TestHarness();
        }

        // ════════════════════════════════════════════════════════════════════
        // FORM EVENTS
        // ════════════════════════════════════════════════════════════════════

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Scan NAS async để không block UI thread khi mở app
            Task.Run(() =>
            {
                var library = _scanner.GetSongs();
                Console.WriteLine($"[MainForm] Thư viện: {library.Count} bài hát.");
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Dọn dẹp đúng thứ tự khi đóng app
            _server.Stop();
            _player.Dispose();
            _scanner.Dispose();
            base.OnFormClosing(e);
        }

        // ════════════════════════════════════════════════════════════════════
        // UI SETUP
        // ════════════════════════════════════════════════════════════════════

        private void SetupUI()
        {
            // URL QR Code gồm IP Nginx + session token
            // Token thay đổi mỗi lần khởi động → người lạ không đoán được
            string nginxIp = "http://10.0.0.11"; // TODO: đọc từ config file
            string webUrl = $"{nginxIp}/{_session.Token}/";

            Console.WriteLine($"[MainForm] QR URL: {webUrl}");
            GenerateQRCode(webUrl);
        }

        private void GenerateQRCode(string url)
        {
            try
            {
                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                QRCode qrCode = new QRCode(qrCodeData);
                Bitmap qrCodeImage = qrCode.GetGraphic(5);

                if (picQRCode != null)
                {
                    picQRCode.Image = qrCodeImage;
                    picQRCode.SizeMode = PictureBoxSizeMode.Zoom;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainForm] Lỗi tạo QR: {ex.Message}");
            }
        }

        private void LoadBGM()
        {
            string bgmPath = @"F:\BGM";
            if (!Directory.Exists(bgmPath)) return;

            foreach (var file in Directory.GetFiles(bgmPath, "*.mp4"))
            {
                _bgmList.Add(new Song(
                    Path.GetFileNameWithoutExtension(file), "BGM", file
                ));
            }

            Console.WriteLine($"[MainForm] Loaded {_bgmList.Count} BGM tracks.");
        }

        // ════════════════════════════════════════════════════════════════════
        // PLAYBACK LOGIC
        // ════════════════════════════════════════════════════════════════════

        private void Player_SongFinished(object? sender, EventArgs e)
        {
            if (_isPlayingBGM)
                PlayNextBGM();
            else
                PlayNextSongInQueue();
        }

        public void PlayNextSongInQueue()
        {
            var nextSong = _queue.GetNextSong();
            if (nextSong != null)
            {
                _isPlayingBGM = false;
                _player.PlaySong(nextSong);

                var upNext = _queue.PeekNextSong();
                UpdateUIInfo(nextSong.Title, upNext?.Title ?? "(Trống)");
            }
            else
            {
                PlayNextBGM();
            }
        }

        private void PlayNextBGM()
        {
            if (_bgmList.Count == 0) return;

            _isPlayingBGM = true;
            var song = _bgmList[_bgmIndex];
            _player.PlaySong(song);

            UpdateUIInfo(
                $"🎧 NHẠC NỀN: {song.Title.ToUpper()}",
                "😒 BẠN ĐÃ ORDER BÀI GÌ ĐÂU 🤳 QUÉT QR GÓC MÀN HÌNH RỒI CHỌN BÀI ĐI 🎙"
            );

            _bgmIndex = (_bgmIndex + 1) % _bgmList.Count;
        }

        // ════════════════════════════════════════════════════════════════════
        // ADMIN EVENT HANDLERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin Nút 2] Reset phiên hát: dừng player, xóa queue, phát BGM.
        /// Được gọi từ AdminManager qua ThreadPool → cần Invoke về UI thread.
        /// </summary>
        private void Admin_RestartSession()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(Admin_RestartSession));
                return;
            }

            Console.WriteLine("[MainForm] 🔁 Admin RestartSession: dừng player, xóa queue...");

            _player.Stop();
            _isPlayingBGM = false;

            // Xóa sạch queue bằng cách lấy hết ra (QueueManager không có Clear API — có thể thêm sau)
            while (_queue.GetNextSong() != null) { }

            UpdateUIInfo("🔄 Phiên hát đã được reset", "(Trống)");
            PlayNextBGM(); // Fallback sang BGM
        }

        /// <summary>
        /// [Admin Nút 3] Tắt ứng dụng karaoke.
        /// Được gọi từ AdminManager qua ThreadPool → cần Invoke về UI thread.
        /// </summary>
        private void Admin_ShutdownApp()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(Admin_ShutdownApp));
                return;
            }

            Console.WriteLine("[MainForm] ⏹ Admin ShutdownApp: đóng ứng dụng...");
            Application.Exit();
        }

        // ════════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cập nhật label "Đang phát" và "Tiếp theo" trên UI.
        /// Thread-safe: tự Invoke về UI thread nếu cần.
        /// </summary>
        public void UpdateUIInfo(string nowPlaying, string nextSong)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateUIInfo(nowPlaying, nextSong)));
                return;
            }

            lblNowPlaying.Text = $"Đang phát: {nowPlaying}";
            lblNextSong.Text = $"Tiếp theo: {nextSong}";
        }

        // ════════════════════════════════════════════════════════════════════
        // TEST HARNESS (Bỏ comment để test nhanh)
        // ════════════════════════════════════════════════════════════════════

        private async void TestHarness()
        {
            var songs = _scanner.GetSongs();
            if (songs.Count == 0) return;

            PlayNextSongInQueue(); // Lúc đầu queue trống → phát BGM

            await Task.Delay(10000);
            Console.WriteLine("🔥 [TEST] User order bài!");

            _queue.AddSong(songs[2]);
            _queue.AddSong(songs[3]);

            if (_isPlayingBGM)
                PlayNextSongInQueue(); // Ngắt BGM, phát bài vừa order
        }
    }
}