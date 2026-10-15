<#
  운영 설정 파일(G:\gameshot_app\data\config.json)을 만든다. 관리자 권한 필요 없음.

  연결 문자열·SMTP·관리자 이메일은 개발 PC의 user-secrets 파일에서 가져온다 (화면에 출력하지 않음).
  이미 파일이 있으면:
    -PublicBaseUrl만 주면 그 값만 바꾸고 나머지는 그대로 둔다
    -Force를 주면 user-secrets에서 새로 만든다

  예) powershell -ExecutionPolicy Bypass -File deploy\new-config.ps1 -PublicBaseUrl https://gameshot.example.com
  설정을 바꾼 뒤에는 서비스를 재시작해야 반영된다 (관리자: Restart-Service gameshot)
#>
param(
    [string]$Root = 'G:\gameshot_app',
    [int]$Port = 9876,
    [string]$PublicBaseUrl = "http://192.168.0.24:9876",
    [string[]]$KnownProxies = @('192.168.0.33'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$csproj = Join-Path $PSScriptRoot '..\src\GameShot.Web\GameShot.Web.csproj'
$dataDir = Join-Path $Root 'data'
$configPath = Join-Path $dataDir 'config.json'
$utf8 = New-Object Text.UTF8Encoding($false)

if ((Test-Path $configPath) -and -not $Force) {
    if (-not $PSBoundParameters.ContainsKey('PublicBaseUrl')) {
        Write-Host "$configPath 가 이미 있습니다. 메일 링크 주소만 바꾸려면 -PublicBaseUrl, 새로 만들려면 -Force를 붙이세요." -ForegroundColor Yellow
        exit 0
    }
    # 메일 링크 주소만 바꾸고 나머지(비밀값 포함)는 그대로 둔다
    $existing = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
    $existing.Mail.PublicBaseUrl = $PublicBaseUrl.TrimEnd('/')
    [IO.File]::WriteAllText($configPath, ($existing | ConvertTo-Json -Depth 6), $utf8)
    Write-Host "메일 링크 주소를 $($existing.Mail.PublicBaseUrl) 로 바꿨습니다. 서비스를 재시작해야 반영됩니다 (관리자: Restart-Service gameshot)." -ForegroundColor Green
    exit 0
}

foreach ($dir in 'keys', 'uploads', 'mail', 'logs') {
    New-Item -ItemType Directory -Force (Join-Path $dataDir $dir) | Out-Null
}

# user-secrets 파일을 직접 읽는다. (관리자 창에서 dotnet user-secrets list가 값을 못 돌려준 적이 있어서)
$secretsId = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1
$secretsFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
if (-not $secretsId -or -not (Test-Path $secretsFile)) {
    throw "user-secrets 파일이 없습니다: $secretsFile"
}
$secrets = [IO.File]::ReadAllText($secretsFile) | ConvertFrom-Json
function Secret([string]$key) { $secrets.PSObject.Properties[$key].Value }

if (-not (Secret 'ConnectionStrings:DefaultConnection')) {
    throw 'user-secrets에 ConnectionStrings:DefaultConnection이 없습니다. tools\import-connection.ps1을 먼저 실행하세요.'
}

$adminEmails = @($secrets.PSObject.Properties | Where-Object { $_.Name -like 'Admin:Emails:*' } | ForEach-Object { $_.Value })

$config = [ordered]@{
    Kestrel = @{ Endpoints = @{ Http = @{ Url = "http://0.0.0.0:$Port" } } }
    ConnectionStrings = @{ DefaultConnection = Secret 'ConnectionStrings:DefaultConnection' }
    Database = @{ MigrateOnStartup = $true }
    # Cloudflare·프록시가 HTTPS를 맡는다 (앱에서 리다이렉트하면 무한 반복)
    Https = @{ Redirect = $false }
    Proxy = @{ KnownProxies = $KnownProxies }
    DataProtection = @{ KeysPath = (Join-Path $dataDir 'keys') }
    Storage = @{ UploadsPath = (Join-Path $dataDir 'uploads') }
    Admin = @{ Emails = $adminEmails }
    Mail = [ordered]@{
        PublicBaseUrl = $PublicBaseUrl
        PickupDirectory = (Join-Path $dataDir 'mail')
        SmtpHost = Secret 'Mail:SmtpHost'
        SmtpPort = $(if (Secret 'Mail:SmtpPort') { [int](Secret 'Mail:SmtpPort') } else { 465 })
        SmtpSslOnConnect = $(if (Secret 'Mail:SmtpSslOnConnect') { [bool]::Parse((Secret 'Mail:SmtpSslOnConnect')) } else { $true })
        SmtpUserName = Secret 'Mail:SmtpUserName'
        SmtpPassword = Secret 'Mail:SmtpPassword'
        FromAddress = Secret 'Mail:FromAddress'
        FromName = 'GameShot'
    }
}

[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))

# 비밀값이 들어 있으니 현재 사용자·SYSTEM·관리자만 읽게 한다
icacls $configPath /inheritance:r /grant:r "${env:USERNAME}:(F)" "SYSTEM:(F)" "Administrators:(F)" | Out-Null

Write-Host "만들었습니다: $configPath" -ForegroundColor Green
Write-Host "  포트 $Port, 메일 링크 주소 $PublicBaseUrl, 신뢰 프록시 $($KnownProxies -join ', ')"
Write-Host "  관리자 이메일 $($adminEmails.Count)개, SMTP $(if (Secret 'Mail:SmtpHost') { '사용' } else { '없음 (메일은 data\mail에 파일로)' })"
