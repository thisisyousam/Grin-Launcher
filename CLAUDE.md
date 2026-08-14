# GrinLauncher 프로젝트 정보

## 기술 스택

- C# / .NET 7 (예정: net8.0으로 업그레이드 고려)
- CmlLib.Core (Minecraft 런처 라이브러리)
- CmlLib.Core.Auth.Microsoft (MS 로그인)
- Avalonia UI (GUI, 다크/라이트 모드 지원)

## CmlLib.Core 사용 시 필수 규칙

- 코드 생성 전 반드시 최신 문서 확인: https://cmllib.github.io/CmlLib.Core-wiki/en/llms-full.txt
- 패키지는 `.csproj` 직접 수정 금지, 항상 CLI로 설치/재설치:
  `dotnet add package CmlLib.Core`
- API를 추측하지 말 것. 확실하지 않으면 API Reference 확인:
  https://cmllib.github.io/CmlLib.Core/api/toc.html
- 알려진 정확한 네임스페이스:
  - Fabric 설치: `CmlLib.Core.ModLoaders.FabricMC` (❌ `CmlLib.Core.Installer.FabricMC` 아님)
  - 이벤트: `launcher.FileProgressChanged`, `launcher.ByteProgressChanged`
  - 오프라인 세션: `MSession.CreateOfflineSession("영문닉네임")` (한글/특수문자 불가)

## UI/디자인 규칙

- UI를 새로 만들거나 수정할 때는 항상 `design.md` 먼저 읽고 그 스펙(컬러 토큰, 타이포그래피,
  레이아웃, 금지 사항)을 그대로 따를 것
- `design.md`에 없는 디자인 판단이 필요하면 그 톤(심플, 라임 단일 강조색, 무채색 나머지)을
  유지하는 선에서 임의 결정하고, 어떤 근거로 정했는지 간단히 남길 것

## 현재 작업: MS 로그인 연결

### 원하는 화면 흐름

1. 앱 실행 시 **MS 로그인 화면이 가장 먼저** 뜬다 (게임 목록/플레이 화면이 아님)
2. 로그인 진행 중에는 로딩 상태 표시
3. **로그인 성공 시에만** 메인 실행 화면(모드 목록, 플레이 버튼)으로 전환
4. 로그인 실패 시 로그인 화면에 머무르며 에러/재시도 UI 표시 (오프라인 세션으로 조용히
   대체해서 넘어가지 말 것 — 로그인 성공이 메인 화면 진입 조건)

### 작업 지침

- 이전에 새로 등록한 Azure 앱에서 Xbox 인증 API 호출 시 `403 Invalid app registration`
  에러가 발생한 적이 있음. 하지만 이걸 "안 되는 것"으로 단정하고 오프라인 세션으로
  우회하지 말 것.
- 대신 아래 순서로 실제 연결을 시도할 것:
  1. `CmlLib.Core.Auth.Microsoft` 최신 문서(`https://cmllib.github.io/CmlLib.Core-wiki/en/llms-full.txt`,
     특히 `auth.microsoft` 섹션 전체)를 다시 읽고, Azure 앱 등록 설정
     (계정 유형, 리디렉션 URI, 공용 클라이언트 흐름 허용 여부 등)이 문서가 요구하는
     조건과 정확히 일치하는지 하나씩 대조
  2. Microsoft Entra Portal에서 현재 앱 등록의 실제 설정값을 확인하고, 문서와 다른 부분이
     있으면 그것부터 교정
  3. 그래도 동일한 403이 재현되면, 에러 응답 본문(`errorMessage` 등 상세 필드)을 그대로
     캡처해서 보고할 것 — "안 됩니다"라는 결론이 아니라 정확한 에러 원문과
     시도한 설정을 같이 제시
  4. 코드 구현 자체는 완전하게 만들 것 (로그인 화면 → `JELoginHandlerBuilder.BuildDefault()`
     → `Authenticate()` → 성공 시 메인 화면 전환). 인증이 막혀 있을 가능성을 이유로
     기능 구현을 생략하거나 처음부터 오프라인 세션으로만 짜지 말 것
- 로그인 화면 UI(입력 필드 없음, 브라우저 인증 코드 안내, 로딩/에러 상태)는 `design.md`
  스펙 그대로 적용

## 프로젝트 목표

지인들에게 배포할 커스텀 마인크래프트 런처.

- Fabric 자동 설치
- GitHub Releases에서 모드 자동 다운로드 (manifest.json 기반)
- MS 로그인 연결 (진행 중, 위 항목 참고)
- GUI 디자인 개선 (design.md 기준)
- 이후 목표: exe/dmg 패키징

## 저장소

https://github.com/thisisyousam/Grin-Launcher
