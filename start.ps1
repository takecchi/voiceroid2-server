# voiceroid2-server 起動スクリプト。
# Release zip を解凍して init.cmd 後に start.cmd をダブルクリックすると本スクリプトが呼ばれる。
#
# やること:
#   1. api\.env と VOICEROID2_AUTH_CODE が設定済みか確認
#   2. cwd を api\ に変えてから node dist\src\main.js を起動
#      (@nestjs/config が process.cwd() を基点に .env を読むため、api\ に cd する必要がある)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$EnvFile = Join-Path $PSScriptRoot 'api\.env'
$MainJs  = Join-Path $PSScriptRoot 'api\dist\src\main.js'

if (-not (Test-Path $EnvFile)) {
    Write-Host '[error] api\.env が存在しません。先に init.cmd を実行してください。' -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $MainJs)) {
    Write-Host "[error] $MainJs が見つかりません。zip 解凍が不完全な可能性があります。" -ForegroundColor Red
    exit 1
}

$content = Get-Content -Raw -Encoding UTF8 -LiteralPath $EnvFile
if (-not ($content -match '(?m)^VOICEROID2_AUTH_CODE=(.+)$')) {
    Write-Host '[error] VOICEROID2_AUTH_CODE が設定されていません。init.cmd を実行してください。' -ForegroundColor Red
    exit 1
}

Write-Host '=== voiceroid2-server starting ===' -ForegroundColor Cyan
Write-Host 'Swagger : http://localhost:8181/api'
Write-Host '停止     : Ctrl+C'
Write-Host ''

# @nestjs/config の envFilePath は相対パスで process.cwd() 基準に解決されるので
# api\ に cd してから node を起動する。
Set-Location (Join-Path $PSScriptRoot 'api')
& node 'dist\src\main.js'
exit $LASTEXITCODE
