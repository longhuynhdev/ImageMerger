<#
.SYNOPSIS
    Tải các bộ ảnh thật để test ImageMerger (đặc biệt là độ chính xác ước tính dung lượng).

.DESCRIPTION
    Mặc định tải (khoảng 20 MB):
      - Kodak Lossless True Color Suite: 24 ảnh PNG 768x512, benchmark nén kinh điển.
      - recurser/exif-orientation-examples: ảnh JPG có đủ 8 giá trị EXIF Orientation.
    Với -IncludeClic tải thêm (khoảng 370 MB):
      - CLIC 2020 validation, bộ mobile và professional: ảnh độ phân giải cao.

    Kết quả nằm ở testdata/downloaded/ (đã nằm trong .gitignore). File đã có sẽ được bỏ qua.

.EXAMPLE
    pwsh tools/DownloadTestImages.ps1
    pwsh tools/DownloadTestImages.ps1 -IncludeClic
#>
param(
    [switch]$IncludeClic
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest nhanh hơn nhiều khi tắt progress bar
$root = Join-Path $PSScriptRoot '..\testdata\downloaded' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $root) { $root = New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot '..\testdata\downloaded') }
$root = "$root"

function Get-File([string]$Url, [string]$Dest) {
    if (Test-Path $Dest) { Write-Host "  đã có: $(Split-Path $Dest -Leaf)"; return }
    New-Item -ItemType Directory -Force (Split-Path $Dest) | Out-Null
    Write-Host "  tải: $Url"
    # Một số server (r0k.us) trả 403 khi tải dồn dập → nghỉ giữa các lần và thử lại.
    foreach ($attempt in 1..4) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile "$Dest.part"
            break
        } catch {
            if ($attempt -eq 4) {
                Write-Warning "Bỏ qua $Url ($($_.Exception.Message))"
                Remove-Item "$Dest.part" -ErrorAction SilentlyContinue
                return
            }
            $wait = 5 * $attempt
            Write-Host "    lỗi ($($_.Exception.Message)), thử lại sau ${wait}s..."
            Start-Sleep -Seconds $wait
        }
    }
    Move-Item "$Dest.part" $Dest -Force
    Start-Sleep -Milliseconds 500
}

Write-Host "Kodak (24 ảnh)..."
foreach ($i in 1..24) {
    $name = 'kodim{0:D2}.png' -f $i
    Get-File "https://r0k.us/graphics/kodak/kodak/$name" (Join-Path $root "kodak\$name")
}

Write-Host "EXIF orientation (18 ảnh)..."
foreach ($kind in 'Landscape', 'Portrait') {
    foreach ($i in 0..8) {
        $name = "${kind}_$i.jpg"
        Get-File "https://raw.githubusercontent.com/recurser/exif-orientation-examples/master/$name" (Join-Path $root "exif-orientation\$name")
    }
}

if ($IncludeClic) {
    foreach ($set in 'mobile_valid_2020', 'professional_valid_2020') {
        Write-Host "CLIC $set..."
        $zip = Join-Path $root "$set.zip"
        $dest = Join-Path $root "clic\$set"
        if (Test-Path $dest) { Write-Host "  đã có: $dest"; continue }
        Get-File "https://data.vision.ee.ethz.ch/cvl/clic/$set.zip" $zip
        Expand-Archive $zip $dest
        Remove-Item $zip
    }
}

Write-Host "Xong: $root"
