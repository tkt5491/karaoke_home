using karaoke_home.NAS;

namespace karaoke_home.Queue
{
    public class QueueManager
    {
        // Sử dụng List để dễ dàng quản lý việc Xóa và Ưu tiên
        private List<Song> _waitingList;

        public QueueManager()
        {
            _waitingList = new List<Song>();
        }

        // 1. Thêm bài hát (Order bình thường - Đẩy xuống cuối hàng)
        public void AddSong(Song song)
        {
            _waitingList.Add(song);
            Console.WriteLine($"[Queue] Đã thêm: {song.Title}");
        }

        // 2. Chức năng ƯU TIÊN (Chèn lên đầu hàng chờ)
        public void PrioritySong(Song song)
        {
            // Chèn vào vị trí số 0 (sẽ được phát ngay sau khi bài hiện tại kết thúc)
            _waitingList.Insert(0, song);
            Console.WriteLine($"[Queue] Đã ƯU TIÊN: {song.Title}");
        }

        // 3. Lấy bài hát tiếp theo để phát (Đồng thời xóa nó khỏi danh sách chờ)
        public Song GetNextSong()
        {
            if (_waitingList.Count > 0)
            {
                Song nextSong = _waitingList[0];
                _waitingList.RemoveAt(0);
                return nextSong;
            }
            return null; // Trả về null nếu hết bài
        }

        // 4. Lấy danh sách đang chờ (Để sau này hiển thị lên Web cho điện thoại xem)
        public List<Song> GetCurrentQueue()
        {
            return _waitingList;
        }

        // 5. Xóa một bài hát bất kỳ trong hàng chờ (Chức năng Xóa)
        public bool RemoveSong(int index)
        {
            if (index >= 0 && index < _waitingList.Count)
            {
                _waitingList.RemoveAt(index);
                return true;
            }
            return false;
        }

        // 6. Hàm tiện ích để biết bài tiếp theo là gì (Để hiển thị lên UI)
        public Song PeekNextSong()
        {
            if (_waitingList.Count > 0) return _waitingList[0];
            return null;
        }
    }
}
