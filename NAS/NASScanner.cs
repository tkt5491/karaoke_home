using System;
using System.Collections.Generic;
using System.IO;

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
        private readonly string _rootPath;

        // Khởi tạo mặc định trỏ vào ổ K: của bạn
        public NASScanner(string rootPath = @"K:\Artist")
        {
            _rootPath = rootPath;
        }

        // Hàm thực thi quét và trả về danh sách bài hát
        public List<Song> ScanSongs()
        {
            List<Song> songList = new List<Song>();

            try
            {
                // Bước 1: Kiểm tra an toàn xem ổ K: có đang kết nối không
                if (!Directory.Exists(_rootPath))
                {
                    // Trong thực tế có thể dùng MessageBox hoặc Log ra UI
                    Console.WriteLine($"[Cảnh báo] Không tìm thấy đường dẫn: {_rootPath}. Vui lòng kiểm tra lại kết nối LAN/Map Drive.");
                    return songList;
                }

                // Bước 2: Quét tất cả file .mp4 trong thư mục cha và tất cả thư mục con
                string[] files = Directory.GetFiles(_rootPath, "*.mp4", SearchOption.AllDirectories);

                // Bước 3: Phân tích từng file để bóc tách Tên bài hát và Ca sĩ
                foreach (string filePath in files)
                {
                    // Lấy tên file không có phần mở rộng (VD: "Son Tung MTP - Lac Troi - Karaoke")
                    string fileName = Path.GetFileNameWithoutExtension(filePath);

                    // Tách chuỗi dựa vào cụm " - "
                    string[] parts = fileName.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

                    // Vì format của bạn rất chuẩn: [Ca sĩ] - [Tên bài] - Karaoke
                    if (parts.Length >= 2)
                    {
                        string artist = parts[0].Trim();
                        string title = parts[1].Trim();

                        songList.Add(new Song(title, artist, filePath));
                    }
                    else
                    {
                        // Đề phòng có file nào đó ai đó đặt tên sai format
                        songList.Add(new Song(fileName, "Unknown Artist", filePath));
                    }
                }
            }
            catch (Exception ex)
            {
                // Bắt các lỗi bất ngờ như mất mạng giữa chừng, không có quyền truy cập file...
                Console.WriteLine($"[Ngoại lệ] Lỗi trong quá trình quét NAS: {ex.Message}");
            }

            return songList; // Trả về danh sách đã quét xong
        }
    }
}
