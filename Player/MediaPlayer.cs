using System;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using karaoke_home.NAS;

namespace karaoke_home.Player
{
    public class MediaPlayer
    {
        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _vlcPlayer;
        private VideoView _videoView;

        // 🌟 Sự kiện cực kỳ quan trọng: Báo cho Form biết khi bài hát kết thúc
        public event EventHandler SongFinished;

        // Constructor nhận vào panelCenter từ MainForm để tự động nhúng Video vào
        public MediaPlayer(Control parentPanel)
        {
            // Khởi tạo engine
            _libVLC = new LibVLC();
            _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

            // Gắn khung hiển thị
            _videoView = new VideoView
            {
                MediaPlayer = _vlcPlayer,
                Dock = DockStyle.Fill
            };

            // Ép khung video này vào Panel của MainForm một cách an toàn (tránh lỗi Thread)
            if (parentPanel.InvokeRequired)
            {
                parentPanel.Invoke(new Action(() => parentPanel.Controls.Add(_videoView)));
            }
            else
            {
                parentPanel.Controls.Add(_videoView);
            }

            // Lắng nghe sự kiện của VLC khi video chạy đến giây cuối cùng
            _vlcPlayer.EndReached += (sender, args) =>
            {
                // Khi hết bài, phát tín hiệu (Event) ra ngoài để MainForm biết mà Next bài
                // Sử dụng ThreadPool (chạy ngầm) để không làm block lõi VLC
                System.Threading.ThreadPool.QueueUserWorkItem(_ => SongFinished?.Invoke(this, EventArgs.Empty));
            };
        }

        // --- CÁC HÀM ĐIỀU KHIỂN ---

        // 1. Phát bài hát
        public void PlaySong(Song song)
        {
            if (song == null || string.IsNullOrEmpty(song.FilePath)) return;

            var media = new Media(_libVLC, song.FilePath, FromType.FromPath);
            _vlcPlayer.Play(media);
        }

        // 2. Tạm dừng / Tiếp tục (Toggle)
        public void PauseOrResume() => _vlcPlayer.Pause();

        // 3. Dừng hẳn
        public void Stop() => _vlcPlayer.Stop();

        // 4. Tua tới / Tua lùi 5 giây (Theo đúng specs của bạn)
        public void SeekForward() { if (_vlcPlayer.IsPlaying) _vlcPlayer.Time += 5000; }
        public void SeekBackward() { if (_vlcPlayer.IsPlaying) _vlcPlayer.Time -= 5000; }

        // 5. Tăng / Giảm âm lượng (Mỗi nấc 5/100)
        public void VolumeUp() { if (_vlcPlayer.Volume <= 95) _vlcPlayer.Volume += 5; }
        public void VolumeDown() { if (_vlcPlayer.Volume >= 5) _vlcPlayer.Volume -= 5; }
    }
}