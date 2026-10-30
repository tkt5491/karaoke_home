using System.Text.Json;

namespace karaoke_home
{
    /// <summary>
    /// Đọc tvsettings.json đặt cạnh file .exe.
    /// Chuyển mạng (công ty → nhà) chỉ sửa ServerHost trong file JSON, không build lại.
    /// </summary>
    public class TvSettings
    {
        public string ServerHost    { get; set; } = "192.168.3.88";
        public string BgmFolder     { get; set; } = "";
        public string LocalTestFile { get; set; } = "";
        public string NasTestFile   { get; set; } = "";   // tính từ gốc share NAS
        public string YtVideoUrl    { get; set; } = "";
        public string YtAudioUrl    { get; set; } = "";

        // Mọi địa chỉ đều dựng từ ServerHost → IP chỉ nằm ở MỘT chỗ
        public string NasRoot   => $@"\\{ServerHost}\NAS";
        public string NasTestPath => string.IsNullOrWhiteSpace(NasTestFile) ? "" : Path.Combine(NasRoot, NasTestFile);
        public string WebAppUrl => $"http://{ServerHost}/";

        public static TvSettings Load(string fileName = "tvsettings.json")
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Settings] Không thấy {path} — dùng giá trị mặc định.");
                return new TvSettings();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling         = JsonCommentHandling.Skip,   // cho phép // ghi chú trong JSON
                AllowTrailingCommas         = true
            };

            var settings = JsonSerializer.Deserialize<TvSettings>(File.ReadAllText(path), options) ?? new TvSettings();
            Console.WriteLine($"[Settings] Server = {settings.ServerHost}");
            return settings;
        }
    }
}
