<#
  DB 서버에서 setup-db.ps1이 남긴 gameshot-connection.txt를
  웹 프로젝트의 user-secrets에 넣고, 원본 파일은 지웁니다.

  실행: powershell -ExecutionPolicy Bypass -File G:\gameshot\tools\import-connection.ps1
#>
param(
    [string]$Source = 'X:\gameshot_setup\gameshot-connection.txt'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\GameShot.Web'
$dotnet  = 'C:\Program Files\dotnet\dotnet.exe'

if (-not (Test-Path $Source)) {
    Write-Host "$Source 파일이 없습니다. DB 서버에서 setup-db.ps1을 먼저 실행하세요." -ForegroundColor Red
    exit 1
}

$connectionString = [IO.File]::ReadAllText($Source).Trim()

$test = New-Object System.Data.SqlClient.SqlConnection($connectionString)
try {
    $test.Open(); $test.Close()
    Write-Host '개발 PC에서 DB 접속 확인' -ForegroundColor Green
} catch {
    Write-Host "개발 PC에서 DB 접속 실패: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

& $dotnet user-secrets set 'ConnectionStrings:DefaultConnection' $connectionString --project $project | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'user-secrets 저장 실패' -ForegroundColor Red
    exit 1
}

Remove-Item $Source -Force
Write-Host 'user-secrets에 저장하고 원본 파일을 지웠습니다.' -ForegroundColor Green
