// ============================================================
//  MediaPlayer.cs
//  "Engine phát video trung tâm" — bọc LibVLCSharp lại thành
//  interface đơn giản để MainForm và HttpServer điều khiển.
//
//  Sơ đồ luồng:
//    MainForm / HttpServer
//        → PlaySong() / Stop() / PauseOrResume() / Seek / Volume
//        → LibVLCSharp.Shared.MediaPlayer (VLC engine)
//        → VideoView (nhúng vào panelCenter của WinForms)
//
//  Lưu ý quan trọng về thread:
//    VLC chạy trên thread riêng của nó.
//    Mọi callback từ VLC (EndReached, v.v.) đều đến từ VLC thread,
//    KHÔNG phải UI thread. Vì vậy:
//      - Raise event ra ngoài bằng ThreadPool để không block VLC.
//      - MainForm nhận event xong phải Invoke về UI thread nếu cần update control.
//
//  Thêm tính năng mới:
//    - CurrentSong   : bài đang phát (để HttpServer trả /api/player/status)
//    - IsPlaying     : trạng thái play/pause
//    - Volume        : property đọc/ghi có validate 0–100
//    - IDisposable   : dọn dẹp LibVLC khi app tắt
// ============================================================

using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using karaoke_home.NAS;

namespace karaoke_home.Player
{
    public class MediaPlayer : IDisposable
    {
        // ════════════════════════════════════════════════════════════════════
        // FIELDS — VLC internals
        // ════════════════════════════════════════════════════════════════════

        private readonly LibVLC _libVLC;
        private readonly LibVLCSharp.Shared.MediaPlayer _vlcPlayer;
        private readonly VideoView _videoView;

        // Bài hát hiện tại — dùng volatile để thread khác đọc luôn thấy giá trị mới nhất
        private volatile Song? _currentSong = null;

        // Cờ chống dispose nhiều lần
        private bool _disposed = false;

        // ════════════════════════════════════════════════════════════════════
        // EVENTS — Thông báo ra ngoài cho MainForm
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Phát khi VLC báo video kết thúc (EndReached).
        /// MainForm subscribe vào đây để tự động chuyển bài.
        /// ⚠ Được raise từ ThreadPool — subscriber cần Invoke nếu update UI.
        /// </summary>
        public event EventHandler? SongFinished;

        /// <summary>
        /// Phát khi bắt đầu phát một bài mới.
        /// HttpServer / MainForm dùng để cập nhật "Now Playing".
        /// </summary>
        public event EventHandler<Song>? SongStarted;

        // ════════════════════════════════════════════════════════════════════
        // PROPERTIES — Trạng thái player (đọc từ bên ngoài)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Bài hát đang phát. Null nếu player đang rảnh.</summary>
        public Song? CurrentSong => _currentSong;

        /// <summary>True nếu VLC đang phát (không phải pause, không phải stop).</summary>
        public bool IsPlaying => _vlcPlayer.IsPlaying;

        /// <summary>
        /// Âm lượng hiện tại (0–100).
        /// Set tự động clamp về đúng khoảng — không bao giờ vượt giới hạn.
        /// </summary>
        public int Volume
        {
            get => _vlcPlayer.Volume;
            set => _vlcPlayer.Volume = Math.Clamp(value, 0, 100);
        }

        /// <summary>
        /// Vị trí hiện tại (milliseconds).
        /// Trả -1 nếu không có media.
        /// </summary>
        public long PositionMs => _vlcPlayer.Time;

        /// <summary>
        /// Tổng độ dài bài hát (milliseconds).
        /// Trả -1 nếu chưa có media hoặc VLC chưa đọc xong header.
        /// </summary>
        public long DurationMs => _vlcPlayer.Length;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR — Khởi tạo VLC engine và gắn VideoView vào panel
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Khởi tạo MediaPlayer.
        /// <param name="parentPanel">
        ///   Panel của MainForm để nhúng VideoView vào (thường là panelCenter).
        ///   VideoView sẽ Dock.Fill toàn bộ panel này.
        /// </param>
        /// </summary>
        public MediaPlayer(Control parentPanel)
        {
            // 1. Khởi tạo LibVLC engine — chỉ tạo 1 lần trong vòng đời app
            _libVLC = new LibVLC();
            _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

            // 2. Tạo VideoView — khung hiển thị video của VLC trong WinForms
            _videoView = new VideoView
            {
                MediaPlayer = _vlcPlayer,
                Dock = DockStyle.Fill
            };

            // 3. Nhúng VideoView vào panel — phải chạy trên UI thread
            //    InvokeRequired = true khi constructor được gọi từ thread khác
            AttachToPanel(parentPanel);

            // 4. Đăng ký sự kiện VLC
            RegisterVlcEvents();
        }

        // ════════════════════════════════════════════════════════════════════
        // PLAYBACK — Phát / Dừng / Tạm dừng
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Phát một bài hát.
        /// Tự động dừng bài đang phát (nếu có) trước khi phát bài mới.
        /// </summary>
        /// <param name="song">Bài hát từ NASScanner. Cần FilePath hợp lệ.</param>
        public void PlaySong(Song song)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.FilePath))
            {
                Console.WriteLine("[MediaPlayer] PlaySong: song null hoặc thiếu FilePath.");
                return;
            }

            if (!File.Exists(song.FilePath))
            {
                Console.WriteLine($"[MediaPlayer] PlaySong: file không tồn tại — {song.FilePath}");
                return;
            }

            Console.WriteLine($"[MediaPlayer] ▶ Đang phát: {song.Artist} - {song.Title}");

            _currentSong = song;

            // Tạo Media object từ đường dẫn file cục bộ
            var media = new Media(_libVLC, song.FilePath, FromType.FromPath);
            _vlcPlayer.Play(media);

            // Thông báo ra ngoài — MainForm cập nhật UI "Now Playing"
            ThreadPool.QueueUserWorkItem(_ => SongStarted?.Invoke(this, song));
        }

        /// <summary>
        /// Toggle tạm dừng / tiếp tục.
        /// VLC.Pause() là toggle — nếu đang chạy thì pause, đang pause thì resume.
        /// </summary>
        public void PauseOrResume()
        {
            _vlcPlayer.Pause();
            Console.WriteLine($"[MediaPlayer] ⏸ PauseOrResume — IsPlaying={_vlcPlayer.IsPlaying}");
        }

        /// <summary>
        /// Dừng hẳn bài đang phát và xóa CurrentSong.
        ///
        /// ⚠ VLC.Stop() KHÔNG trigger EndReached event.
        ///    Vì vậy MainForm KHÔNG được gọi Stop() khi muốn "next bài".
        ///    Thay vào đó dùng event OnNextRequested trong HttpServer.
        ///    Stop() chỉ dùng khi thật sự muốn dừng hẳn (tắt app, clear queue...).
        /// </summary>
        public void Stop()
        {
            _vlcPlayer.Stop();
            _currentSong = null;
            Console.WriteLine("[MediaPlayer] ⏹ Đã dừng.");
        }

        // ════════════════════════════════════════════════════════════════════
        // SEEK — Tua nhanh / Tua lùi
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Tua tới 5 giây. Không làm gì nếu đang không phát.</summary>
        public void SeekForward(int seconds = 5)
        {
            if (!_vlcPlayer.IsPlaying) return;
            _vlcPlayer.Time = Math.Min(_vlcPlayer.Time + seconds * 1000L, _vlcPlayer.Length);
            Console.WriteLine($"[MediaPlayer] ⏩ Tua tới {seconds}s → {_vlcPlayer.Time / 1000}s");
        }

        /// <summary>Tua lùi 5 giây. Không về âm — clamp về 0.</summary>
        public void SeekBackward(int seconds = 5)
        {
            if (!_vlcPlayer.IsPlaying) return;
            _vlcPlayer.Time = Math.Max(_vlcPlayer.Time - seconds * 1000L, 0);
            Console.WriteLine($"[MediaPlayer] ⏪ Tua lùi {seconds}s → {_vlcPlayer.Time / 1000}s");
        }

        // ════════════════════════════════════════════════════════════════════
        // VOLUME — Âm lượng
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tăng âm lượng 1 nấc (+5). Giới hạn tối đa 100.
        /// Dùng property Volume để clamp tự động.
        /// </summary>
        public void VolumeUp(int step = 5)
        {
            Volume += step;
            Console.WriteLine($"[MediaPlayer] 🔊 Volume = {Volume}");
        }

        /// <summary>
        /// Giảm âm lượng 1 nấc (-5). Giới hạn tối thiểu 0.
        /// </summary>
        public void VolumeDown(int step = 5)
        {
            Volume -= step;
            Console.WriteLine($"[MediaPlayer] 🔉 Volume = {Volume}");
        }

        // ════════════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gắn VideoView vào parentPanel trên UI thread.
        /// Nếu constructor được gọi từ thread khác → Invoke về UI thread.
        /// </summary>
        private void AttachToPanel(Control parentPanel)
        {
            void Attach() => parentPanel.Controls.Add(_videoView);

            if (parentPanel.InvokeRequired)
                parentPanel.Invoke((Action)Attach);
            else
                Attach();
        }

        /// <summary>
        /// Đăng ký tất cả VLC event handlers tại một chỗ.
        /// Tách ra hàm riêng để constructor gọn và dễ đọc.
        /// </summary>
        private void RegisterVlcEvents()
        {
            // EndReached: VLC phát xong video đến cuối
            // ⚠ Callback này đến từ VLC internal thread — KHÔNG được update UI trực tiếp
            // ⚠ KHÔNG được gọi lại VLC API đồng bộ từ đây (deadlock)
            //    → Dùng ThreadPool để raise event ra ngoài an toàn
            _vlcPlayer.EndReached += (_, _) =>
            {
                Console.WriteLine($"[MediaPlayer] 🏁 Bài kết thúc: {_currentSong?.Title}");
                _currentSong = null;
                ThreadPool.QueueUserWorkItem(_ => SongFinished?.Invoke(this, EventArgs.Empty));
            };

            // Mở rộng sau: thêm event Buffering, EncounteredError, v.v. tại đây
            // _vlcPlayer.EncounteredError += (_, _) => { ... };
        }

        // ════════════════════════════════════════════════════════════════════
        // DISPOSE — Dọn dẹp LibVLC khi app tắt
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Giải phóng VLC engine và VideoView.
        /// Gọi trong MainForm.OnFormClosing() sau _server.Stop().
        ///
        /// Thứ tự dispose quan trọng:
        ///   1. Stop player trước (flush VLC pipeline)
        ///   2. Dispose MediaPlayer
        ///   3. Dispose VideoView
        ///   4. Dispose LibVLC engine (cuối cùng)
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _vlcPlayer.Stop(); } catch { /* bỏ qua nếu đã stop */ }
            try { _vlcPlayer.Dispose(); } catch { }
            try { _videoView.Dispose(); } catch { }
            try { _libVLC.Dispose(); } catch { }

            Console.WriteLine("[MediaPlayer] Đã Dispose.");
        }
    }
}