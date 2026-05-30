namespace karaoke_home.NAS
{
    // 1. Định nghĩa cấu trúc dữ liệu của một Bài hát
    public class Song
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string FilePath { get; set; }

        // Constructor để khởi tạo bài hát nhanh gọn
        public Song(string title, string artist, string filePath)
        {
            Title = title;
            Artist = artist;
            FilePath = filePath;
        }
    }
    // 2. Lớp phụ trách quét NAS
    public class NASScanner
    {
        // ─── Cấu hình ────────────────────────────────────────────────────────
        private readonly string _rootPath;
        private static readonly string[] _supportedExtensions = { "*.mp4", "*.mkv", "*.avi" };

        // ─── Cache ───────────────────────────────────────────────────────────
        private List<Song>? _cachedSongs = null;
        private bool _cacheIsDirty = true;   // true = cần scan lại
        private readonly object _cacheLock = new();

        // ─── FileSystemWatcher ───────────────────────────────────────────────
        private FileSystemWatcher? _watcher = null;
        private bool _watcherActive = false;

        // ─── Debounce: tránh invalidate liên tục khi copy nhiều file cùng lúc
        private System.Threading.Timer? _debounceTimer = null;
        private const int DebounceDelayMs = 2000; // Chờ 2 giây sau lần thay đổi cuối

        // ─── Event thông báo thư viện đã cập nhật ────────────────────────────
        // Subscribe vào đây để MainForm / HttpServer biết khi có bài mới.
        public event Action? LibraryChanged;

        // Khởi tạo mặc định trỏ vào ổ K: của bạn
        public NASScanner(string rootPath = @"F:\Artist")//K:\Artist
        {
            _rootPath = rootPath;
        }
        // ════════════════════════════════════════════════════════════════════
        // API CHÍNH: Lấy danh sách bài hát
        //   - Lần đầu: scan thật → lưu cache → bật Watcher
        //   - Lần sau: trả cache ngay nếu còn sạch
        //   - Sau khi Watcher phát hiện thay đổi: scan lại 1 lần
        // ════════════════════════════════════════════════════════════════════

        public List<Song> GetSongs()
        {
            lock (_cacheLock)
            {
                if (!_cacheIsDirty && _cachedSongs != null)
                {
                    Console.WriteLine($"[NAS] Cache hit — {_cachedSongs.Count} bài, không cần scan lại.");
                    return new List<Song>(_cachedSongs); // Trả clone
                }
            }

            // Cache miss hoặc dirty → scan thật
            var freshList = ScanFromDisk();

            lock (_cacheLock)
            {
                _cachedSongs = freshList;
                _cacheIsDirty = false;
            }

            // Bật Watcher lần đầu (hoặc bật lại nếu trước đó NAS chưa mount)
            if (!_watcherActive)
                TryStartWatcher();

            LibraryChanged?.Invoke();
            return new List<Song>(freshList); // Trả clone
        }

        // Backward-compat: giữ tên cũ để HttpServer / MainForm không cần sửa ngay
        public List<Song> ScanSongs() => GetSongs();

        // ════════════════════════════════════════════════════════════════════
        // Ép scan lại ngay lập tức (dùng cho nút "Refresh" trên UI nếu muốn)
        // ════════════════════════════════════════════════════════════════════
        public List<Song> ForceRescan()
        {
            lock (_cacheLock) { _cacheIsDirty = true; }
            Console.WriteLine("[NAS] ForceRescan được yêu cầu.");
            return GetSongs();
        }

        // ════════════════════════════════════════════════════════════════════
        // Scan thật từ ổ đĩa — private, không trả clone (caller tự clone)
        // ════════════════════════════════════════════════════════════════════
        private List<Song> ScanFromDisk()
        {
            var songList = new List<Song>();

            Console.WriteLine($"[NAS] Đang scan: {_rootPath} ...");

            if (!Directory.Exists(_rootPath))
            {
                Console.WriteLine($"[NAS] Cảnh báo: không tìm thấy đường dẫn '{_rootPath}'. NAS chưa mount?");
                return songList;
            }

            try
            {
                foreach (var ext in _supportedExtensions)
                {
                    var files = Directory.GetFiles(_rootPath, ext, SearchOption.AllDirectories);

                    foreach (var filePath in files)
                    {
                        var song = ParseFileName(filePath);
                        if (song != null) songList.Add(song);
                    }
                }

                Console.WriteLine($"[NAS] Scan xong — tìm thấy {songList.Count} bài.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NAS] Lỗi khi scan: {ex.Message}");
            }

            return songList;
        }

        // ════════════════════════════════════════════════════════════════════
        // Parse tên file theo format: Ca sĩ | Tên bài | Tag.ext
        // Tương thích ngược với format cũ:  Ca sĩ - Tên bài - Karaoke.ext
        // ════════════════════════════════════════════════════════════════════
        private static Song? ParseFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts;

            // Ưu tiên format mới với pipe '|'
            if (fileName.Contains('|'))
            {
                parts = fileName.Split('|', StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                // Fallback: format cũ với " - "
                parts = fileName.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            }

            if (parts.Length >= 2)
            {
                return new Song(
                    title: parts[1].Trim(),
                    artist: parts[0].Trim(),
                    filePath: filePath
                );
            }

            // Đặt tên file không đúng format — vẫn thêm vào, tên = tên file
            Console.WriteLine($"[NAS] Cảnh báo: file không đúng format tên — '{fileName}'");
            return new Song(fileName, "Unknown", filePath);
        }

        // ════════════════════════════════════════════════════════════════════
        // FileSystemWatcher — theo dõi thư mục NAS
        // ════════════════════════════════════════════════════════════════════
        private void TryStartWatcher()
        {
            if (!Directory.Exists(_rootPath))
            {
                Console.WriteLine("[NAS] Watcher chưa khởi động: thư mục chưa tồn tại.");
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(_rootPath)
                {
                    // Theo dõi cả thư mục con
                    IncludeSubdirectories = true,

                    // Chỉ quan tâm đến tên file và thư mục (không cần nội dung)
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,

                    // Bật theo dõi
                    EnableRaisingEvents = true
                };

                // Lọc theo extension — Watcher chỉ nhận 1 filter nên dùng wildcard,
                // rồi filter thêm trong handler
                _watcher.Filter = "*.*";

                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemRenamed;

                // Bắt lỗi nội bộ của Watcher (VD: buffer tràn khi quá nhiều thay đổi)
                _watcher.Error += OnWatcherError;

                _watcherActive = true;
                Console.WriteLine($"[NAS] FileSystemWatcher đã khởi động — đang theo dõi: {_rootPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NAS] Không thể khởi động Watcher: {ex.Message}");
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // Chỉ quan tâm đến video files
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (ext is not (".mp4" or ".mkv" or ".avi")) return;

            Console.WriteLine($"[NAS] Phát hiện thay đổi: {e.ChangeType} — {e.Name}");
            ScheduleInvalidate();
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (ext is not (".mp4" or ".mkv" or ".avi")) return;

            Console.WriteLine($"[NAS] File đổi tên: {e.OldName} → {e.Name}");
            ScheduleInvalidate();
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // Thường xảy ra khi NAS mất kết nối hoặc buffer event tràn
            Console.WriteLine($"[NAS] Watcher lỗi: {e.GetException().Message}. Sẽ thử scan lại.");
            _watcherActive = false;
            ScheduleInvalidate();
        }

        // ════════════════════════════════════════════════════════════════════
        // Debounce: đặt timer 2 giây — nếu có thêm thay đổi thì reset timer
        // Tránh scan liên tục khi user copy cả folder 50 bài vào một lúc
        // ════════════════════════════════════════════════════════════════════
        private void ScheduleInvalidate()
        {
            // Reset timer mỗi lần có thay đổi mới
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                Console.WriteLine("[NAS] Debounce xong — invalidate cache và scan lại.");
                lock (_cacheLock) { _cacheIsDirty = true; }
                GetSongs(); // Scan ngay, trigger LibraryChanged cho subscriber
            }, null, DebounceDelayMs, Timeout.Infinite);
        }

        // ════════════════════════════════════════════════════════════════════
        // Dọn dẹp tài nguyên khi app tắt
        // ════════════════════════════════════════════════════════════════════
        public void Dispose()
        {
            _debounceTimer?.Dispose();
            _watcher?.Dispose();
            Console.WriteLine("[NAS] NASScanner đã Dispose.");
        }

        // Hàm thực thi quét và trả về danh sách bài hát
    }
}
