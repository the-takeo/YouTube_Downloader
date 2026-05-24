# YouTube Downloader - ネイティブメッセージングホスト登録スクリプト
#
# 使い方:
#   .\register-host.ps1 -ExtensionId <拡張機能のID>
#
# 拡張機能のIDは chrome://extensions で「デベロッパーモード」を有効にすると確認できます。

param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    [Parameter(Mandatory = $false)]
    [string]$ExePath = ""
)

$HostName = "com.youtube_downloader"

# EXEのパスが指定されていなければ自動検出
if (-not $ExePath) {
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $ExePath = Join-Path $repoRoot "bin\Debug\net9.0\win-x64\YouTube_Downloader.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Error "EXEが見つかりません: $ExePath`nビルド後に再実行するか、-ExePath で直接指定してください。"
        exit 1
    }
}

$ExePath = (Resolve-Path $ExePath).Path

# マニフェストの保存先
$ManifestDir  = "$env:LOCALAPPDATA\YouTubeDownloader"
$ManifestPath = "$ManifestDir\native-host-manifest.json"

New-Item -ItemType Directory -Force -Path $ManifestDir | Out-Null

$Manifest = [ordered]@{
    name            = $HostName
    description     = "YouTube Downloader Native Host"
    path            = $ExePath
    type            = "stdio"
    allowed_origins = @("chrome-extension://$ExtensionId/")
}

$Manifest | ConvertTo-Json | Set-Content -Path $ManifestPath -Encoding UTF8

# レジストリに登録
$RegPath = "HKCU:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Force -Path $RegPath | Out-Null
Set-ItemProperty -Path $RegPath -Name "(Default)" -Value $ManifestPath

Write-Host ""
Write-Host "セットアップ完了！" -ForegroundColor Green
Write-Host "  EXE      : $ExePath"
Write-Host "  マニフェスト: $ManifestPath"
Write-Host "  レジストリ  : $RegPath"
Write-Host ""
Write-Host "Chromeを再起動してから拡張機能を使用してください。" -ForegroundColor Yellow
