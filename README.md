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

## 관리자

`/admin` (대시보드, 주문 처리, 상품·SKU·이미지 관리)은 Admin 역할만 들어갈 수 있다.
회원가입한 뒤 그 이메일을 설정에 넣고 앱을 다시 시작하면 역할이 붙는다. 역할은 로그인할 때 반영되므로 다시 로그인한다.

```
dotnet user-secrets set "Admin:Emails:0" "me@example.com" --project src/GameShot.Web
```

운영에서는 환경 변수 `Admin__Emails__0`을 쓴다. 업로드한 상품 이미지는 `wwwroot/uploads/`에 저장되며 git에 넣지 않는다(DB와 함께 백업).

## 메일

비밀번호 찾기, 가입 인증, 주문(결제 완료·발송·취소) 메일은 `EmailMessages` 테이블(아웃박스)에 쌓이고,
백그라운드 작업이 10초마다 보낸다. 실패하면 1·4·9·16분 뒤 다시 시도하고 5번 실패하면 포기한다.

SMTP 설정이 없으면 실제로 보내지 않고 `src/GameShot.Web/App_Data/mail/`에 .eml 파일로 남긴다.
네이버 SMTP를 쓰려면 user-secrets(운영은 환경 변수 `Mail__...`)에 넣는다.

```
dotnet user-secrets set "Mail:SmtpHost" "smtp.naver.com" --project src/GameShot.Web
dotnet user-secrets set "Mail:SmtpUserName" "네이버아이디" --project src/GameShot.Web
dotnet user-secrets set "Mail:SmtpPassword" "비밀번호 또는 앱 비밀번호" --project src/GameShot.Web
dotnet user-secrets set "Mail:FromAddress" "네이버아이디@naver.com" --project src/GameShot.Web
```

메일 속 링크의 주소는 `Mail:PublicBaseUrl`(기본 http://localhost:5120)이다. 배포하면 실제 주소로 바꾼다.

## 테스트

```
dotnet test
```
