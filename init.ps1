# voiceroid2-server 初回セットアップスクリプト。
# Release zip を解凍して init.cmd をダブルクリックすると本スクリプトが呼ばれる。
#
# やること:
#   1. Node.js / helper.exe / VOICEROID2 のインストール状況をチェック
#   2. api\.env を api\.env.example からコピーして VOICEROID2_HELPER_PATH を埋める
#   3. VOICEROID2_AUTH_CODE が空なら helper --get-key で取得して書き込む
#      (このとき VOICEROID2 エディタを一時的に起動してもらう)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Set-Location $PSScriptRoot

$DefaultInstallDir = 'C:\Program Files (x86)\AHS\VOICEROID2'
$EnvFile           = Join-Path $PSScriptRoot 'api\.env'
$EnvExample        = Join-Path $PSScriptRoot 'api\.env.example'

# release zip では helper\voiceroid2-helper.exe にフラットに置かれるが、
# 開発チェックアウトから直接走らせると helper\bin\Release\net481\ にある。
$HelperCandidates = @(
    'helper\voiceroid2-helper.exe',
    'helper\bin\Release\net481\voiceroid2-helper.exe'
)
$HelperExe = $HelperCandidates `
    | ForEach-Object { Join-Path $PSScriptRoot $_ } `
    | Where-Object { Test-Path $_ } `
    | Select-Object -First 1

function Update-EnvKey([string]$Key, [string]$Value) {
    $lines = if (Test-Path $EnvFile) {
        Get-Content -Encoding UTF8 -LiteralPath $EnvFile
    } else { @() }
    $newLine = "${Key}=${Value}"
    $output  = New-Object 'System.Collections.Generic.List[string]'
    $found   = $false
    foreach ($line in $lines) {
        if ($line -match "^$([Regex]::Escape($Key))=") {
            $output.Add($newLine); $found = $true
        } else {
            $output.Add($line)
        }
    }
    if (-not $found) { $output.Add($newLine) }
    [IO.File]::WriteAllLines($EnvFile, $output, [Text.UTF8Encoding]::new($false))
}

function Read-EnvKey([string]$Key) {
    if (-not (Test-Path $EnvFile)) { return $null }
    foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $EnvFile) {
        if ($line -match "^$([Regex]::Escape($Key))=(.*)$") { return $Matches[1].Trim() }
    }
    return $null
}

function Test-VoiceroidEditorRunning {
    return @(Get-Process -Name VoiceroidEditor -ErrorAction SilentlyContinue).Count -gt 0
}

function Wait-VoiceroidEditorRunning([int]$timeoutSec = 60) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        if (Test-VoiceroidEditorRunning) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

Write-Host '=== voiceroid2-server initialization ===' -ForegroundColor Cyan
Write-Host ''

# 1. Node.js
$nodeVersion = $null
try { $nodeVersion = & node --version 2>$null } catch {}
if (-not $nodeVersion) {
    Write-Host '[error] Node.js が見つかりません。Node.js 20 以上をインストールしてください。' -ForegroundColor Red
    Write-Host '        https://nodejs.org/'
    exit 1
}
Write-Host "Node.js   : $nodeVersion"

# 2. helper.exe
if (-not $HelperExe) {
    Write-Host '[error] helper.exe が見つかりません。以下のいずれかに存在する必要があります:' -ForegroundColor Red
    $HelperCandidates | ForEach-Object { Write-Host "          $_" }
    Write-Host '        Release zip 解凍直後ならそのまま、開発チェックアウトなら npm run build:helper してください。'
    exit 1
}
Write-Host "helper    : $HelperExe"

# 3. VOICEROID2
if (Test-Path (Join-Path $DefaultInstallDir 'aitalked.dll')) {
    Write-Host "VOICEROID2: $DefaultInstallDir"
} else {
    Write-Host "[warn] VOICEROID2 が標準位置に見つかりません: $DefaultInstallDir" -ForegroundColor Yellow
    Write-Host '       別の場所にインストールしている場合は、初期化後に api\.env の'
    Write-Host '       VOICEROID2_INSTALL_DIR を手で書き換えてください。'
}
Write-Host ''

# 4. .env 作成
if (-not (Test-Path $EnvFile)) {
    Write-Host 'api\.env を api\.env.example からコピーします...'
    Copy-Item -LiteralPath $EnvExample -Destination $EnvFile
}

# 5. VOICEROID2_HELPER_PATH を同梱パスに固定
Update-EnvKey 'VOICEROID2_HELPER_PATH' $HelperExe
Write-Host 'VOICEROID2_HELPER_PATH を api\.env に書き込みました。'

# 6. VOICEROID2_AUTH_CODE が空なら取得
$currentAuth = Read-EnvKey 'VOICEROID2_AUTH_CODE'
if (-not [string]::IsNullOrEmpty($currentAuth)) {
    Write-Host 'VOICEROID2_AUTH_CODE は既に設定済みなのでスキップします。'
} else {
    Write-Host ''
    Write-Host '=== 認証コードシードを取得します ===' -ForegroundColor Cyan
    Write-Host ''

    if (Test-VoiceroidEditorRunning) {
        Write-Host 'VOICEROID2 エディタの起動を検出しました。' -ForegroundColor Green
    } else {
        $voiceroidExe = Join-Path $DefaultInstallDir 'VoiceroidEditor.exe'
        if (Test-Path $voiceroidExe) {
            Write-Host "VOICEROID2 エディタを自動起動します: $voiceroidExe"
            Start-Process -FilePath $voiceroidExe | Out-Null
            if (-not (Wait-VoiceroidEditorRunning)) {
                Write-Host '[error] VOICEROID2 エディタが 60 秒以内に起動しませんでした。' -ForegroundColor Red
                exit 1
            }
            Write-Host '  → 起動検出。UI 初期化を 3 秒待ちます...'
            Start-Sleep -Seconds 3
        } else {
            Write-Host 'VOICEROID2 エディタが起動していません。' -ForegroundColor Yellow
            Write-Host '手動で (非管理者で) 起動してから Enter を押してください。'
            do {
                [void][System.Console]::ReadLine()
                if (-not (Test-VoiceroidEditorRunning)) {
                    Write-Host '  → まだ検出できません。VOICEROID2 エディタを起動してから Enter:' -ForegroundColor Yellow
                }
            } while (-not (Test-VoiceroidEditorRunning))
            Write-Host '  → 検出しました。' -ForegroundColor Green
        }
    }

    Write-Host 'helper --get-key を実行中...'
    # UI 初期化途中で失敗するケースに備えてリトライ。stderr は helper のログなので素通し。
    $authOutput = $null
    for ($i = 0; $i -lt 3; $i++) {
        $authOutput = (& $HelperExe --get-key) | Out-String
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($authOutput.Trim())) { break }
        if ($i -lt 2) {
            Write-Host '  → 失敗。3 秒待ってリトライ...' -ForegroundColor Yellow
            Start-Sleep -Seconds 3
        }
    }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($authOutput.Trim())) {
        Write-Host '[error] 認証コードを取得できませんでした。' -ForegroundColor Red
        Write-Host '        - VOICEROID2 エディタの起動が完了していますか?'
        Write-Host '        - helper.exe と VOICEROID2 エディタの権限 (UAC) が一致していますか?'
        exit 1
    }
    $authCode = $authOutput.Trim()
    Update-EnvKey 'VOICEROID2_AUTH_CODE' $authCode
    Write-Host '認証コードを api\.env に書き込みました。'
    Write-Host 'VOICEROID2 エディタは閉じて構いません (以後 API 側は DLL 直叩きで動きます)。'
}

Write-Host ''
Write-Host '=== 初期化完了 ===' -ForegroundColor Green
Write-Host 'start.cmd を実行して API サーバーを起動してください。'
Write-Host ''
