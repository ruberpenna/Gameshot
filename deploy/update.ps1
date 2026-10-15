<#
  GameShot 배포(업데이트). 관리자 PowerShell에서 실행한다.

  1) 테스트  2) Release로 발행(staging)  3) 서비스 멈춤  4) app 폴더 교체  5) 서비스 시작  6) /healthz 확인
  data 폴더(설정·키·업로드·로그)는 건드리지 않는다. DB 마이그레이션은 앱이 시작하면서 적용한다.

  예) powershell -ExecutionPolicy Bypass -File deploy\update.ps1
      -SkipTests  : 테스트 생략
      -NoService  : 서비스 없이 발행만 (처음 배포할 때 install-service.ps1 전에)
#>
param(
    [string]$Root = 'G:\gameshot_app',
    [string]$ServiceName = 'gameshot',
    [int]$Port = 9876,
    [switch]$SkipTests,
    [switch]$NoService
)

$ErrorActionPreference = 'Stop'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$appDir = Join-Path $Root 'app'
$staging = Join-Path $Root 'staging'

$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($service -and -not $NoService -and
    -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '서비스를 멈추고 다시 켜야 해서 관리자 PowerShell에서 실행해야 합니다.'
}

if (-not $SkipTests) {
    Write-Host '테스트 중...' -ForegroundColor Cyan
    & $dotnet test (Join-Path $repo 'GameShot.sln') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw '테스트가 실패해서 배포를 멈춥니다.' }
}

Write-Host '발행 중...' -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
& $dotnet publish (Join-Path $repo 'src\GameShot.Web\GameShot.Web.csproj') -c Release -o $staging --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '발행에 실패했습니다.' }
# 업로드 이미지는 data\uploads에서 제공하므로 앱 폴더에 넣지 않는다
Remove-Item (Join-Path $staging 'wwwroot\uploads') -Recurse -Force -ErrorAction SilentlyContinue

if ($service -and -not $NoService) {
    Write-Host "서비스 $ServiceName 멈추는 중..." -ForegroundColor Cyan
    Stop-Service $ServiceName
}

New-Item -ItemType Directory -Force $appDir | Out-Null
robocopy $staging $appDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "파일 복사에 실패했습니다 (robocopy $LASTEXITCODE)." }
Remove-Item $staging -Recurse -Force

if ($service -and -not $NoService) {
    Start-Service $ServiceName
    Write-Host '시작 확인 중...' -ForegroundColor Cyan
    $ok = $false
    for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
        Start-Sleep -Seconds 2
        try { $ok = (Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 5).Content -eq 'Healthy' } catch { }
    }
    if (-not $ok) { throw "서비스가 정상으로 뜨지 않았습니다. $Root\data\logs\$ServiceName.log 를 확인하세요." }
    Write-Host "배포 완료: http://127.0.0.1:$Port 정상 (Healthy)" -ForegroundColor Green
} else {
    Write-Host "발행 완료: $appDir" -ForegroundColor Green
}
