# GameShot 배포

이 PC(192.168.0.24)에서 Windows 서비스로 돌린다. Penna와 같은 구성이다.

```
인터넷 → (Cloudflare) → Nginx Proxy Manager 192.168.0.33 → 이 PC :9876 (Kestrel, 서비스 gameshot)
```

## 폴더

```
G:\gameshot_app\
  app\      발행된 앱. update.ps1이 매번 통째로 바꾼다
  data\
    config.json  운영 설정 (연결 문자열·SMTP·관리자·포트). 비밀값이 있어 git에 넣지 않는다
    keys\        로그인 쿠키·재설정 링크 암호화 키 (지우면 모두 로그아웃)
    uploads\     관리자가 올린 상품 이미지 (/uploads/...)
    mail\        SMTP가 없을 때 메일 파일
    logs\        서비스 로그 (10MB마다 교체)
```

백업할 것은 DB와 `data\` 폴더다.

## 처음 한 번

```
powershell -ExecutionPolicy Bypass -File deploy\new-config.ps1 -PublicBaseUrl https://도메인
powershell -ExecutionPolicy Bypass -File deploy\update.ps1 -NoService
# 관리자 PowerShell에서
powershell -ExecutionPolicy Bypass -File deploy\install-service.ps1
```

- `new-config.ps1`은 개발 PC의 user-secrets에서 연결 문자열·SMTP·관리자 이메일을 가져와 `data\config.json`을 만든다.
  `-PublicBaseUrl`은 메일 속 링크 주소다. 도메인을 정하면 `-Force`로 다시 만든다.
- `install-service.ps1`은 서비스 `gameshot`을 등록하고 방화벽에서 9876을 연다.
  기본은 Nginx Proxy Manager(192.168.0.33)만 허용하고, `-AllowLan`을 붙이면 내부망 전체에서 직접 접속할 수 있다.

## 업데이트

```
git pull
# 관리자 PowerShell에서
powershell -ExecutionPolicy Bypass -File deploy\update.ps1
```

테스트 → 발행 → 서비스 멈춤 → 앱 교체 → 서비스 시작 → `/healthz` 확인 순서로 진행한다.
DB 마이그레이션은 앱이 시작하면서 적용한다(`Database:MigrateOnStartup`).

## Nginx Proxy Manager

Proxy Host를 하나 추가한다.

- Domain: 쓸 도메인
- Forward: `http` / `192.168.0.24` / `9876`
- Cloudflare Flexible을 쓰면 Penna처럼 Force SSL은 끈다 (앱도 HTTPS 리다이렉트를 하지 않는다)

## 확인

- `http://127.0.0.1:9876/healthz` → `Healthy` (DB 연결까지 확인)
- 로그: `G:\gameshot_app\data\logs\gameshot.log`
- 서비스: `Get-Service gameshot`, 재시작 `Restart-Service gameshot`(관리자)
