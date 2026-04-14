using QRCoder;
using karaoke_home.Player;
using karaoke_home.Queue;
using karaoke_home.NAS;
using karaoke_home.Web;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace karaoke_home
{
    public partial class MainForm : Form
    {
        private MediaPlayer _player;
        private QueueManager _queue;
        private NASScanner _scanner;
        private HttpServer _server;
        // BGM system
        private List<Song> _bgmList = new List<Song>();
        private int _bgmIndex = 0;
        private bool _isPlayingBGM = false;

        public MainForm()
        {
            InitializeComponent();
            SetupUI();
            LoadBGM();
            // Khởi tạo các thành phần
            _queue = new QueueManager();
            _scanner = new NASScanner();

            // Truyền panelCenter vào cho Player tự quản lý giao diện
            _player = new MediaPlayer(panelCenter);

            // Đăng ký sự kiện: Khi bài hát kết thúc thì làm gì? -> Chuyển bài!
            _player.SongFinished += Player_SongFinished;

            // TEST NHANH: Quét và bỏ thử 2 bài đầu tiên vào Queue rồi ép nó phát luôn
            TestHarness();

            _server = new karaoke_home.Web.HttpServer(_player, _queue, _scanner);
            _server.Start("8888");
            PlayNextSongInQueue();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            NAS.NASScanner scanner = new NAS.NASScanner();
            List<NAS.Song> myLibrary = scanner.ScanSongs();

            // In ra thử số lượng bài hát tìm được để test
            MessageBox.Show($"Đã tìm thấy {myLibrary.Count} bài hát trong NAS!");
        }

        private void SetupUI()
        {
            // Gọi hàm tạo QR Code ở đây
            // Vì Nginx chứa Web UI sẽ nằm trên Ubuntu, ta trỏ thẳng URL về IP của Ubuntu
            string webUrl = "http://192.168.0.230/";
            GenerateQRCode(webUrl);
        }

        private void LoadBGM()
        {
            string bgmPath = @"C:\Users\tkt\Downloads\BGM";

            if (!Directory.Exists(bgmPath)) return;

            var files = Directory.GetFiles(bgmPath, "*.mp4");

            foreach (var file in files)
            {
                _bgmList.Add(new Song(
                    Path.GetFileNameWithoutExtension(file),
                    "BGM",
                    file
                ));
            }
        }

        // Hàm xử lý tạo hình ảnh QR Code
        private void GenerateQRCode(string url)
        {
            try
            {
                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                // Khởi tạo dữ liệu QR (Mức Q - sửa lỗi 25% giúp điện thoại quét nhạy hơn)
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

                // Tạo ảnh Bitmap từ dữ liệu QR
                QRCode qrCode = new QRCode(qrCodeData);
                Bitmap qrCodeImage = qrCode.GetGraphic(5); // Số 5 là độ nét (Pixel/module)

                // Đổ ảnh vào PictureBox trên giao diện (giả sử em đặt tên là picQRCode)
                if (picQRCode != null)
                {
                    picQRCode.Image = qrCodeImage;
                    picQRCode.SizeMode = PictureBoxSizeMode.Zoom; // Ép ảnh vừa vặn với khung
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lỗi QR] Không thể tạo mã QR: {ex.Message}");
            }
        }

        private void Player_SongFinished(object sender, EventArgs e)
        {
            // Hàm này tự kích hoạt khi bài trước hát xong
            //PlayNextSongInQueue();
            if (_isPlayingBGM)
            {
                PlayNextBGM(); // 🔁 loop BGM
            }
            else
            {
                PlayNextSongInQueue();
            }
        }
        private void PlayNextSongInQueue()
        {
            var nextSong = _queue.GetNextSong(); // Rút bài đầu tiên ra khỏi hàng chờ
            if (nextSong != null)
            {
                _isPlayingBGM = false; //
                _player.PlaySong(nextSong);

                // Cập nhật dòng chữ trên UI
                var upNext = _queue.PeekNextSong();
                string upNextTitle = upNext != null ? upNext.Title : "(Trống)";
                UpdateUIInfo(nextSong.Title, upNextTitle);
            }
            else
            {
                //UpdateUIInfo("Chưa có bài hát", "(Trống)");
                PlayNextBGM(); // fallback sang BGM
            }
        }

        private void PlayNextBGM()
        {
            if (_bgmList.Count == 0) return;

            _isPlayingBGM = true;

            var song = _bgmList[_bgmIndex];
            _player.PlaySong(song);

            UpdateUIInfo($"🎧 NHẠC NỀN: {song.Title.ToUpper()}", "😒BẠN ĐÃ ORDER BÀI GÌ ĐÂU🤳QUÉT QR GÓC MÀN HÌNH RỒI CHỌN BÀI ĐI🎙");

            _bgmIndex = (_bgmIndex + 1) % _bgmList.Count;
        }

        public void UpdateUIInfo(string nowPlaying, string nextSong)
        {
            // Cần dùng Invoke nếu hàm này được gọi từ Thread khác (như HttpListener)
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateUIInfo(nowPlaying, nextSong)));
                return;
            }
            lblNowPlaying.Text = $"Đang phát: {nowPlaying}";
            lblNextSong.Text = $"Tiếp theo: {nextSong}";
        }

        private async void TestHarness()
        {
            var songs = _scanner.ScanSongs();

            // ❗ Đảm bảo có ít nhất 1 bài
            if (songs.Count == 0) return;

            // 👉 Giai đoạn 1: KHÔNG add gì → để hệ thống tự chạy BGM
            PlayNextSongInQueue();

            // 👉 Giai đoạn 2: giả lập user order sau 5 giây
            await Task.Delay(5000);

            Console.WriteLine("🔥 [TEST] User đã order bài!");

            _queue.AddSong(songs[20]);
            _queue.AddSong(songs[19]);

            // ❗ QUAN TRỌNG: nếu đang BGM thì chuyển ngay
            if (_isPlayingBGM)
            {
                PlayNextSongInQueue();
            }
        }

    }
}
