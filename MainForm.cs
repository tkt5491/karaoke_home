using karaoke_home.Player;
using karaoke_home.Queue;
using karaoke_home.NAS;
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
        public MainForm()
        {
            InitializeComponent();
            //SetupUI();
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
        }
        private void MainForm_Load(object sender, EventArgs e)
        {
            NAS.NASScanner scanner = new NAS.NASScanner();
            List<NAS.Song> myLibrary = scanner.ScanSongs();

            // In ra thử số lượng bài hát tìm được để test
            MessageBox.Show($"Đã tìm thấy {myLibrary.Count} bài hát trong NAS!");
        }
        /*private void SetupUI()
        {
            // Cài đặt Form Fullscreen (Giống quán Karaoke thật)
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;

            // Text mặc định ban đầu
            lblNowPlaying.Text = "Đang phát: Chưa có bài hát";
            lblNextSong.Text = "Tiếp theo: (Trống)";
        }
        private void SetupVLC()
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

        private void btnExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
