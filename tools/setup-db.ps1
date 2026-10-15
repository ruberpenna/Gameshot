<#
  GameShot 개발 DB 준비 스크립트

  1) DB 서버에 GameShot 데이터베이스와 앱 전용 로그인(gameshot_app)을 만들고
  2) 무작위 비밀번호로 만든 연결 문자열을 웹 프로젝트의 user-secrets에 저장합니다.

  관리자 비밀번호는 실행할 때 직접 입력하며, 파일이나 화면에 남지 않습니다.
  다시 실행하면 DB는 그대로 두고 앱 계정 비밀번호만 새로 바꿉니다.

  개발 PC에서 실행하면 2)를 바로 하고, DB 서버에서 실행하면(프로젝트가 없으면)
  연결 문자열을 이 스크립트 옆 gameshot-connection.txt에 남깁니다.
  그 파일은 개발 PC에서 import-connection.ps1로 user-secrets에 넣고 지웁니다.

  실행: powershell -ExecutionPolicy Bypass -File .\setup-db.ps1
#>
param(
    [string]$Server,
    [string]$Database  = 'GameShot',
    [string]$AppLogin  = 'gameshot_app',
    # 앱(개발 PC)이 DB 서버에 접속할 때 쓰는 주소
    [string]$AppServer = '192.168.0.27,1433'
)

$ErrorActionPreference = 'Stop'
$project   = Join-Path $PSScriptRoot '..\src\GameShot.Web'
$dotnet    = 'C:\Program Files\dotnet\dotnet.exe'
$devPcMode = (Test-Path (Join-Path $project 'GameShot.Web.csproj')) -and (Test-Path $dotnet)
if (-not $Server) { $Server = if ($devPcMode) { $AppServer } else { 'localhost' } }

function New-AppPassword {
    # 연결 문자열에서 문제가 되는 ; = ' " 문자는 쓰지 않음
    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; $lower = 'abcdefghijkmnopqrstuvwxyz'
    $digit = '23456789';                 $symbol = '!#%*-_'
    $all = $upper + $lower + $digit + $symbol
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $pick = {
        param($set)
        $b = New-Object byte[] 1
        $rng.GetBytes($b)
        $set[$b[0] % $set.Length]
    }
    $chars = @((& $pick $upper), (& $pick $lower), (& $pick $digit), (& $pick $symbol))
    1..20 | ForEach-Object { $chars += & $pick $all }
    -join ($chars | Sort-Object { Get-Random })
}

Write-Host "DB 서버: $Server" -ForegroundColor Cyan
$adminLogin = Read-Host 'SQL Server 관리자 로그인 (예: sa, 비워두면 Windows 인증)'

$conn = New-Object System.Data.SqlClient.SqlConnection
if ([string]::IsNullOrWhiteSpace($adminLogin)) {
    $conn.ConnectionString = "Server=$Server;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
} else {
    $adminPassword = Read-Host "$adminLogin 비밀번호" -AsSecureString
    $adminPassword.MakeReadOnly()
    $conn.ConnectionString = "Server=$Server;Database=master;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
    $conn.Credential = New-Object System.Data.SqlClient.SqlCredential($adminLogin, $adminPassword)
}

function Invoke-Sql([string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.ExecuteScalar()
}

try {
    $conn.Open()
} catch {
    Write-Host "접속 실패: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$version = Invoke-Sql "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) + N' / ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(100))"
Write-Host "SQL Server 버전: $version" -ForegroundColor Green

if ((Invoke-Sql "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)") -eq 1) {
    Write-Host '이 서버는 Windows 인증만 허용합니다. SSMS > 서버 속성 > 보안에서 "SQL Server 및 Windows 인증 모드"로 바꾸고 서비스를 재시작한 뒤 다시 실행하세요.' -ForegroundColor Red
    exit 1
}

$appPassword = New-AppPassword

# 1) 데이터베이스
if ((Invoke-Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database'") -eq 0) {
    Invoke-Sql "CREATE DATABASE [$Database]" | Out-Null
    Write-Host "데이터베이스 [$Database] 생성" -ForegroundColor Green
} else {
    Write-Host "데이터베이스 [$Database] 이미 있음 (그대로 사용)"
}

# 2) 로그인 (있으면 비밀번호만 교체)
if ((Invoke-Sql "SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$AppLogin'") -eq 0) {
    Invoke-Sql "CREATE LOGIN [$AppLogin] WITH PASSWORD = N'$appPassword', CHECK_POLICY = ON, DEFAULT_DATABASE = [$Database]" | Out-Null
    Write-Host "로그인 [$AppLogin] 생성" -ForegroundColor Green
} else {
    Invoke-Sql "ALTER LOGIN [$AppLogin] WITH PASSWORD = N'$appPassword'" | Out-Null
    Write-Host "로그인 [$AppLogin] 이미 있음 (비밀번호 교체)"
}

# 3) DB 사용자 + 권한 (마이그레이션으로 테이블을 만들기 위해 이 DB 안에서만 db_owner)
Invoke-Sql @"
USE [$Database];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$AppLogin')
    CREATE USER [$AppLogin] FOR LOGIN [$AppLogin];
IF IS_ROLEMEMBER('db_owner', N'$AppLogin') = 0
    ALTER ROLE db_owner ADD MEMBER [$AppLogin];
"@ | Out-Null
Write-Host "[$Database] 사용자/권한 설정 완료" -ForegroundColor Green
$conn.Close()

# 4) 앱 계정으로 실제 접속되는지 확인
$appCredentials   = "Database=$Database;User Id=$AppLogin;Password=$appPassword;Encrypt=True;TrustServerCertificate=True"
$connectionString = "Server=$AppServer;$appCredentials"
$test = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;$appCredentials")
try {
    $test.Open(); $test.Close()
    Write-Host "[$AppLogin] 계정 접속 확인" -ForegroundColor Green
} catch {
    Write-Host "앱 계정 접속 실패: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($devPcMode) {
    # 5) user-secrets 저장 (git에 올라가지 않는 %APPDATA%\Microsoft\UserSecrets 아래에 저장됨)
    & $dotnet user-secrets set 'ConnectionStrings:DefaultConnection' $connectionString --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'user-secrets 저장 실패' -ForegroundColor Red
        exit 1
    }
    Write-Host 'user-secrets에 연결 문자열 저장 완료' -ForegroundColor Green
} else {
    # 5) DB 서버에서 실행: 개발 PC가 가져갈 수 있게 파일로 남김
    $out = Join-Path $PSScriptRoot 'gameshot-connection.txt'
    [IO.File]::WriteAllText($out, $connectionString)
    Write-Host "연결 문자열을 $out 에 저장했습니다. 개발 PC에서 import-connection.ps1을 실행하면 가져온 뒤 지워집니다." -ForegroundColor Green
}

Write-Host ''
Write-Host "끝났습니다. SQL Server 버전($version)을 Claude에게 알려주면 됩니다." -ForegroundColor Cyan
