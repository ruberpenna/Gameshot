# GameShot

콘솔 게임 쇼핑몰 연습 프로젝트. 2018년 Classic ASP로 만들다 만 화면 목업을 ASP.NET Core로 새로 만든다.

## 구성

- ASP.NET Core 10 MVC + Razor, ASP.NET Identity
- EF Core 10 + SQL Server 2019 (호환성 수준 150)
- xUnit 테스트 (SQLite 메모리 DB)

```
src/GameShot.Web     웹 앱
tests/GameShot.Tests 테스트
tools/               DB 준비 스크립트
```

## 개발 환경 준비

1. .NET 10 SDK 설치
2. DB 서버에서 `tools/setup-db.ps1` 실행 → GameShot DB와 `gameshot_app` 계정 생성, 연결 문자열을 `gameshot-connection.txt`로 남김
3. 개발 PC에서 `tools/import-connection.ps1` 실행 → 연결 문자열을 user-secrets에 저장하고 파일 삭제
4. 마이그레이션 적용과 실행

```
dotnet tool restore
dotnet ef database update --project src/GameShot.Web
dotnet run --project src/GameShot.Web --launch-profile http
```

http://localhost:5120 에서 열린다. 개발 환경에서는 시작할 때 플랫폼·장르와 샘플 상품을 넣는다.

## 테스트

```
dotnet test
```
