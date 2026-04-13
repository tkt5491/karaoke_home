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
        public MainForm()
        {
            InitializeComponent();
            SetupUI();
            //SetupVLC();

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

        /*private void SetupVLC()
        {
            // Khởi tạo engine VLC
            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);

            // Tạo khung hiển thị Video và nhét nó vào panelCenter
            _videoView = new VideoView
            {
                MediaPlayer = _player,
                Dock = DockStyle.Fill
            };

            panelCenter.Controls.Add(_videoView);
        }*/
        private void Player_SongFinished(object sender, EventArgs e)
        {
            // Hàm này tự kích hoạt khi bài trước hát xong
            PlayNextSongInQueue();
        }
        private void PlayNextSongInQueue()
        {
            var nextSong = _queue.GetNextSong(); // Rút bài đầu tiên ra khỏi hàng chờ
            if (nextSong != null)
            {
                _player.PlaySong(nextSong);

                // Cập nhật dòng chữ trên UI
                var upNext = _queue.PeekNextSong();
                string upNextTitle = upNext != null ? upNext.Title : "(Trống)";
                UpdateUIInfo(nextSong.Title, upNextTitle);
            }
            else
            {
                UpdateUIInfo("Chưa có bài hát", "(Trống)");
            }
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

        private void TestHarness()
        {
            var songs = _scanner.ScanSongs();
            if (songs.Count >= 2)
            {
                _queue.AddSong(songs[0]); // Đưa bài 1 vào queue
                _queue.AddSong(songs[1]); // Đưa bài 2 vào queue
                PlayNextSongInQueue();    // Kích hoạt phát bài đầu tiên
            }
        }



    }
}
