using karaoke_home.NAS;

namespace karaoke_home.Queue
{
    public class QueueManager
    {
        // ─── Cấu hình ────────────────────────────────────────────────────────
        private const int MaxQueueSize = 50; // Giới hạn tối đa để tránh spam

        // ─── State nội bộ ────────────────────────────────────────────────────
        private readonly List<Song> _waitingList = new();
        private readonly object _lock = new(); // Dùng để lock mọi thao tác trên list

        // ─── Event thông báo queue thay đổi (để UI/HttpServer tự update) ─────
        public event Action? QueueChanged;

        // ════════════════════════════════════════════════════════════════════
        // 1. Thêm bài hát bình thường — đẩy xuống cuối hàng
        // ════════════════════════════════════════════════════════════════════
        public bool AddSong(Song song)
        {
            lock (_lock)
            {
                if (_waitingList.Count >= MaxQueueSize)
                {
                    Console.WriteLine($"[Queue] Từ chối: hàng chờ đã đầy ({MaxQueueSize} bài). Bài bị từ chối: {song.Title}");
                    return false;
                }

                _waitingList.Add(song);
                Console.WriteLine($"[Queue] Đã thêm: {song.Title} (vị trí {_waitingList.Count})");
            }

            QueueChanged?.Invoke(); // Gọi ra ngoài lock để tránh deadlock
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Ưu tiên — chèn lên đầu hàng, phát ngay sau bài hiện tại
        // ════════════════════════════════════════════════════════════════════
        public bool PrioritySong(Song song)
        {
            lock (_lock)
            {
                if (_waitingList.Count >= MaxQueueSize)
                {
                    Console.WriteLine($"[Queue] Từ chối priority: hàng chờ đã đầy. Bài bị từ chối: {song.Title}");
                    return false;
                }

                _waitingList.Insert(0, song);
                Console.WriteLine($"[Queue] Đã ƯU TIÊN: {song.Title}");
            }

            QueueChanged?.Invoke();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Lấy bài tiếp theo để phát — xóa khỏi danh sách chờ
        // ════════════════════════════════════════════════════════════════════
        public Song? GetNextSong()
        {
            Song? next;

            lock (_lock)
            {
                if (_waitingList.Count == 0) return null;

                next = _waitingList[0];
                _waitingList.RemoveAt(0);
                Console.WriteLine($"[Queue] Đang phát: {next.Title} | Còn lại: {_waitingList.Count} bài");
            }

            QueueChanged?.Invoke();
            return next;
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. Xem bài tiếp theo mà không lấy ra — dùng cho UI "Next: ..."
        // ════════════════════════════════════════════════════════════════════
        public Song? PeekNextSong()
        {
            lock (_lock)
            {
                return _waitingList.Count > 0 ? _waitingList[0] : null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. Lấy danh sách hiện tại — trả về CLONE để tránh race condition
        //    Caller không thể modify list nội bộ qua reference này
        // ════════════════════════════════════════════════════════════════════
        public List<Song> GetCurrentQueue()
        {
            lock (_lock)
            {
                return new List<Song>(_waitingList); // Clone, không trả reference gốc
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. Xóa bài tại vị trí index
        // ════════════════════════════════════════════════════════════════════
        public bool RemoveSong(int index)
        {
            bool removed;

            lock (_lock)
            {
                if (index < 0 || index >= _waitingList.Count)
                {
                    Console.WriteLine($"[Queue] RemoveSong: index {index} ngoài phạm vi (size={_waitingList.Count})");
                    return false;
                }

                Console.WriteLine($"[Queue] Đã xóa: {_waitingList[index].Title} (index {index})");
                _waitingList.RemoveAt(index);
                removed = true;
            }

            if (removed) QueueChanged?.Invoke();
            return removed;
        }

        // ════════════════════════════════════════════════════════════════════
        // 7. Nâng ưu tiên bài đang ở vị trí index lên đầu hàng
        // ════════════════════════════════════════════════════════════════════
        public bool PromoteSong(int index)
        {
            lock (_lock)
            {
                if (index <= 0 || index >= _waitingList.Count) return false;

                var song = _waitingList[index];
                _waitingList.RemoveAt(index);
                _waitingList.Insert(0, song);
                Console.WriteLine($"[Queue] Đã nâng ưu tiên: {song.Title} lên đầu hàng");
            }

            QueueChanged?.Invoke();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // 8. Thuộc tính tiện ích — đọc nhanh số bài và trạng thái
        // ════════════════════════════════════════════════════════════════════
        public int Count
        {
            get { lock (_lock) { return _waitingList.Count; } }
        }

        public bool IsEmpty
        {
            get { lock (_lock) { return _waitingList.Count == 0; } }
        }

        public bool IsFull
        {
            get { lock (_lock) { return _waitingList.Count >= MaxQueueSize; } }
        }
    }
}
