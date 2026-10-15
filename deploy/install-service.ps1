<#
  GameShot Windows 서비스 등록 + 방화벽 열기. 관리자 PowerShell에서 한 번 실행한다.
  다시 실행해도 안전하다 (있으면 설정만 고친다).

  - 서비스 gameshot : G:\gameshot_app\app\GameShot.Web.exe (NSSM, 자동 시작, 로그 data\logs)
  - 방화벽 'GameShot web' : TCP 9876 인바운드 (기본은 Nginx Proxy Manager 192.168.0.33만 허용)

  먼저 deploy\new-config.ps1 과 deploy\update.ps1 -NoService 로 설정·앱을 준비해 둔다.

  예) powershell -ExecutionPolicy Bypass -File deploy\install-service.ps1
      -AllowLan : 같은 공유기(내부망)의 모든 기기에서 9876으로 직접 접속 허용
#>
param(
    [string]$Root = 'G:\gameshot_app',
    [string]$ServiceName = 'gameshot',
    [int]$Port = 9876,
    [string]$ProxyIp = '192.168.0.33',
    [switch]$AllowLan
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '관리자 PowerShell에서 실행하세요.'
}

$exe = Join-Path $Root 'app\GameShot.Web.exe'
$config = Join-Path $Root 'data\config.json'
$logDir = Join-Path $Root 'data\logs'
if (-not (Test-Path $exe)) { throw "$exe 가 없습니다. deploy\update.ps1 -NoService 를 먼저 실행하세요." }
if (-not (Test-Path $config)) { throw "$config 가 없습니다. deploy\new-config.ps1 을 먼저 실행하세요." }

$nssm = (Get-Command nssm -ErrorAction SilentlyContinue).Source
if (-not $nssm) {
    $nssm = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter nssm.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -like '*win64*' | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $nssm) { throw 'nssm을 찾을 수 없습니다. (winget install NSSM.NSSM)' }

if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    & $nssm stop $ServiceName | Out-Null
    & $nssm set $ServiceName Application $exe | Out-Null
} else {
    & $nssm install $ServiceName $exe | Out-Null
}
& $nssm set $ServiceName DisplayName 'GameShot (ASP.NET Core)' | Out-Null
& $nssm set $ServiceName Description '콘솔 게임 쇼핑몰 GameShot (연습용)' | Out-Null
& $nssm set $ServiceName AppDirectory (Join-Path $Root 'app') | Out-Null
& $nssm set $ServiceName AppEnvironmentExtra 'ASPNETCORE_ENVIRONMENT=Production' "GAMESHOT_CONFIG=$config" | Out-Null
& $nssm set $ServiceName AppStdout (Join-Path $logDir "$ServiceName.log") | Out-Null
& $nssm set $ServiceName AppStderr (Join-Path $logDir "$ServiceName.log") | Out-Null
& $nssm set $ServiceName AppRotateFiles 1 | Out-Null
& $nssm set $ServiceName AppRotateBytes 10485760 | Out-Null
& $nssm set $ServiceName AppStopMethodConsole 15000 | Out-Null
& $nssm set $ServiceName Start SERVICE_AUTO_START | Out-Null
& $nssm start $ServiceName | Out-Null

$rule = 'GameShot web'
Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$remote = if ($AllowLan) { 'LocalSubnet' } else { $ProxyIp }
New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -RemoteAddress $remote -Action Allow | Out-Null
Write-Host "방화벽: TCP $Port 를 $remote 에서 허용" -ForegroundColor Green

Start-Sleep -Seconds 5
try {
    $health = (Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 10).Content
    Write-Host "서비스 $ServiceName : $((Get-Service $ServiceName).Status), 상태 확인: $health" -ForegroundColor Green
} catch {
    Write-Host "서비스는 등록했지만 아직 응답이 없습니다. $logDir\$ServiceName.log 를 확인하세요." -ForegroundColor Yellow
}
