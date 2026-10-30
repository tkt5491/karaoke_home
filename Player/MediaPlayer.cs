// ============================================================
//  MediaPlayer.cs  (Tuần 6)
//  Bọc LibVLCSharp thành interface đơn giản cho MainForm.
//
//  Thay đổi so với bản hè:
//    - Nhận ĐƯỜNG DẪN/URL thay vì NAS.Song → player không cần biết
//      bài đến từ đâu (file cục bộ, Samba \\server\NAS, stream http).
//    - Hỗ trợ 2 luồng tách riêng của yt-dlp (hình + tiếng) bằng AddSlave.
//    - Dispose Media sau mỗi lần Play → không rò rỉ bộ nhớ native.
//    - Thêm PlaybackError (link hết hạn, file hỏng) để không đứng màn hình đen.
//    - Tắt nhận phím/chuột của VLC → phím tắt đến được MainForm.
//
//  Quy tắc thread (giữ nguyên từ bản hè):
//    Callback của VLC chạy trên thread riêng của VLC.
//    - Không gọi lại API VLC (Play/Stop) ngay trong callback → deadlock.
//    - Raise event ra ngoài bằng ThreadPool; MainForm tự đưa về UI thread.
// ============================================================

using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace karaoke_home.Player
{
    public class MediaPlayer : IDisposable
    {
        private readonly LibVLC                         _libVLC;
        private readonly LibVLCSharp.Shared.MediaPlayer _vlcPlayer;
        private readonly VideoView                      _videoView;

        private volatile string? _currentTitle;
        private bool _disposed;

        // ── Events (đều raise từ ThreadPool, KHÔNG phải UI thread) ──────────
        public event EventHandler?         SongFinished;   // hết bài (EndReached)
        public event EventHandler<string>? SongStarted;    // tham số: tiêu đề
        public event EventHandler<string>? PlaybackError;  // tham số: tiêu đề bài lỗi

        // ── Trạng thái ──────────────────────────────────────────────────────
        public string? CurrentTitle => _currentTitle;
        public bool    IsPlaying    => _vlcPlayer.IsPlaying;
        public long    PositionMs   => _vlcPlayer.Time;
        public long    DurationMs   => _vlcPlayer.Length;

        public int Volume
        {
            get => _vlcPlayer.Volume;
            set => _vlcPlayer.Volume = Math.Clamp(value, 0, 100);
        }

        // ════════════════════════════════════════════════════════════════════
        public MediaPlayer(Control parentPanel)
        {
            _libVLC    = new LibVLC();   // cần Core.Initialize() ở Program.cs trước dòng này
            _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC)
            {
                // Mặc định cửa sổ VLC "nuốt" phím và chuột → phím tắt của Form không chạy
                EnableKeyInput   = false,
                EnableMouseInput = false
            };

            _videoView = new VideoView { MediaPlayer = _vlcPlayer, Dock = DockStyle.Fill };
            AttachToPanel(parentPanel);
            RegisterVlcEvents();
        }

        // ════════════════════════════════════════════════════════════════════
        // PLAY — một hàm cho mọi nguồn
        // ════════════════════════════════════════════════════════════════════

        /// <param name="source">File cục bộ, đường dẫn UNC (\\server\NAS\...), hoặc URL http(s).</param>
        /// <param name="audioUrl">Luồng tiếng riêng (yt-dlp tách hình/tiếng). Null nếu file đã có tiếng.</param>
        /// <param name="title">Tên hiển thị. Null → tự lấy từ tên file.</param>
        /// <returns>false nếu không bắt đầu phát được.</returns>
        public bool Play(string source, string? audioUrl = null, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                Console.WriteLine("[MediaPlayer] Play: nguồn rỗng.");
                return false;
            }

            var isNetwork = Uri.TryCreate(source, UriKind.Absolute, out var uri)
                            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            // Chỉ kiểm tra tồn tại với file/UNC; URL thì để VLC tự báo lỗi qua EncounteredError
            if (!isNetwork && !File.Exists(source))
            {
                Console.WriteLine($"[MediaPlayer] Không thấy file (sai đường dẫn, hoặc Windows chưa đăng nhập Samba?): {source}");
                return false;
            }

            var displayTitle = title ?? (isNetwork ? "Stream" : Path.GetFileNameWithoutExtension(source));

            // `using`: LibVLC đếm tham chiếu ở tầng native — Play() làm player giữ bản riêng,
            // nên dispose bản của mình ngay sau đó là an toàn và tránh rò rỉ mỗi bài một ít.
            using var media = isNetwork
                ? new Media(_libVLC, uri!)
                : new Media(_libVLC, source, FromType.FromPath);

            if (!string.IsNullOrWhiteSpace(audioUrl))
                media.AddSlave(MediaSlaveType.Audio, 4, audioUrl);   // 4 = ưu tiên cao nhất

            if (isNetwork)
                media.AddOption(":network-caching=1500");             // đệm 1.5 giây cho stream

            _currentTitle = displayTitle;
            var ok = _vlcPlayer.Play(media);

            Console.WriteLine($"[MediaPlayer] ▶ {displayTitle} ({(isNetwork ? "stream" : "file")}" +
                              $"{(audioUrl is null ? "" : " + luồng tiếng riêng")})");

            if (ok) ThreadPool.QueueUserWorkItem(_ => SongStarted?.Invoke(this, displayTitle));
            return ok;
        }

        /// <summary>VLC.Pause() là toggle: đang chạy → dừng, đang dừng → chạy.</summary>
        public void PauseOrResume() => _vlcPlayer.Pause();

        /// <summary>
        /// Dừng hẳn. ⚠ Stop() KHÔNG kích hoạt EndReached → không dùng để "next bài".
        /// </summary>
        public void Stop()
        {
            _vlcPlayer.Stop();
            _currentTitle = null;
        }

        // ── Tua: dùng IsSeekable thay vì IsPlaying → tua được cả khi đang pause ──
        public void SeekForward(int seconds = 5)
        {
            if (!_vlcPlayer.IsSeekable) return;
            _vlcPlayer.Time = Math.Min(_vlcPlayer.Time + seconds * 1000L, _vlcPlayer.Length);
        }

        public void SeekBackward(int seconds = 5)
        {
            if (!_vlcPlayer.IsSeekable) return;
            _vlcPlayer.Time = Math.Max(_vlcPlayer.Time - seconds * 1000L, 0);
        }

        public void VolumeUp(int step = 5)   => Volume += step;
        public void VolumeDown(int step = 5) => Volume -= step;

        // ════════════════════════════════════════════════════════════════════
        private void AttachToPanel(Control parentPanel)
        {
            void Attach() => parentPanel.Controls.Add(_videoView);
            if (parentPanel.InvokeRequired) parentPanel.Invoke((Action)Attach);
            else Attach();
        }

        private void RegisterVlcEvents()
        {
            // Cả 2 callback chạy trên thread của VLC → chỉ ghi nhận rồi đẩy ra ThreadPool
            _vlcPlayer.EndReached += (_, _) =>
            {
                Console.WriteLine($"[MediaPlayer] 🏁 Hết bài: {_currentTitle}");
                _currentTitle = null;
                ThreadPool.QueueUserWorkItem(_ => SongFinished?.Invoke(this, EventArgs.Empty));
            };

            _vlcPlayer.EncounteredError += (_, _) =>
            {
                var title = _currentTitle ?? "(không rõ)";
                Console.WriteLine($"[MediaPlayer] ❌ VLC báo lỗi khi phát: {title}");
                _currentTitle = null;
                ThreadPool.QueueUserWorkItem(_ => PlaybackError?.Invoke(this, title));
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // Thứ tự dispose: stop → player → view → engine (cuối cùng)
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _vlcPlayer.Stop(); }    catch { }
            try { _vlcPlayer.Dispose(); } catch { }
            try { _videoView.Dispose(); } catch { }
            try { _libVLC.Dispose(); }    catch { }
        }
    }
}
