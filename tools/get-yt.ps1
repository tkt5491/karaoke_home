# ============================================================
#  tools/get-yt.ps1  (Tuần 6C)
#  Lấy link stream YouTube cho phím F3 và ghi thẳng vào tvsettings.json.
#
#  Luồng: Backend /api/search → lấy videoId → yt-dlp trên server → 2 link
#         (hình + tiếng) → ghi vào tvsettings.json (giữ nguyên comment).
#
#  Cách chạy (từ cmd, đứng ở C:\Code\karaoke_home):
#    powershell -ExecutionPolicy Bypass -File tools\get-yt.ps1 "anh nho em"
#
#  Link YouTube hết hạn sau vài giờ → F3 báo lỗi thì chạy lại script này.
# ============================================================
param(
    [Parameter(Mandatory = $true)][string]$Query,
    [string]$Server = "192.168.3.88",
    [string]$User   = "tkt"
)

$cfg = Join-Path $PSScriptRoot "..\tvsettings.json"
if (-not (Test-Path $cfg)) { Write-Host "Không thấy $cfg" -ForegroundColor Red; exit 1 }

# ── 1. Tìm bài qua chính Backend của mình ───────────────────────────────
$url = "http://$Server/api/search?q=$([uri]::EscapeDataString($Query))"
try   { $search = Invoke-RestMethod $url -ErrorAction Stop }
catch { Write-Host "Không gọi được Backend: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

if ($search.items.Count -eq 0) { Write-Host "Không có kết quả cho '$Query'" -ForegroundColor Red; exit 1 }
$song = $search.items[0]
Write-Host "1. Bài: $($song.title)  [$($song.videoId)]  fallback=$($search.isFallback)"

# ── 2. yt-dlp trên server lấy link ──────────────────────────────────────
# Ưu tiên H.264 (avc1) + AAC (m4a): GPU nào cũng giải mã được.
# Không có thì lấy codec bất kỳ ≤720p; cuối cùng mới lấy bản gộp sẵn (1 link).
$format = "bv*[height<=720][vcodec^=avc1]+ba[ext=m4a]/bv*[height<=720]+ba/b"
Write-Host "2. Đang hỏi yt-dlp trên server (khoảng 5-10 giây)..."
$links = @(ssh "$User@$Server" "yt-dlp -g -f '$format' 'https://www.youtube.com/watch?v=$($song.videoId)'")

if ($LASTEXITCODE -ne 0 -or $links.Count -eq 0) {
    Write-Host "yt-dlp lỗi. Thử cập nhật: ssh $User@$Server `"sudo yt-dlp -U`"" -ForegroundColor Red
    exit 1
}

$video = $links[0]
$audio = if ($links.Count -ge 2) { $links[1] } else { "" }
$kind  = if ($audio) { "hình + tiếng riêng" } else { "gộp sẵn 1 link" }
Write-Host "   Nhận $($links.Count) link ($kind)"

if ($video -match 'expire=(\d+)') {
    $exp = [DateTimeOffset]::FromUnixTimeSeconds([long]$Matches[1]).ToLocalTime()
    Write-Host "   Link hết hạn lúc: $($exp.ToString('HH:mm dd/MM'))"
}

# ── 3. Ghi vào tvsettings.json — đọc/ghi UTF-8 để không hỏng comment tiếng Việt ──
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($cfg, $utf8)
# Dùng hàm thay thế (scriptblock) thay vì chuỗi: ký tự $ trong chuỗi thay thế có nghĩa đặc biệt với regex
$text = [regex]::Replace($text, '("YtVideoUrl"\s*:\s*")[^"]*(")', { param($m) $m.Groups[1].Value + $video + $m.Groups[2].Value })
$text = [regex]::Replace($text, '("YtAudioUrl"\s*:\s*")[^"]*(")', { param($m) $m.Groups[1].Value + $audio + $m.Groups[2].Value })
[IO.File]::WriteAllText($cfg, $text, $utf8)

Write-Host "3. Đã ghi vào tvsettings.json → F5 trong Visual Studio, rồi bấm F3" -ForegroundColor Green
