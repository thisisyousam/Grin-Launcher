# GrinLauncher 디자인 스펙

## 컨셉

애플 liquid glass 톤을 앱 전체의 표면 언어로 쓴다. 카드·패널·사이드바 모두 콘텐츠
위에 살짝 떠 있는 반투명 유리 표면으로 처리하고, 그 위에서만 무채색 타이포그래피로
절제를 유지한다. 화려한 그라데이션·네온 글로우는 여전히 금지 — "유리"라는 재질 하나로
깊이를 주되 색은 여전히 라임 하나로 아낀다.

> 2026-08-08: 사이드바를 아이콘 전용 플로팅 알약형 패널로 재설계하고, 배경 블러
> (glassmorphism)를 도입. 강조색은 `#89D22F` 단 하나만 쓰고, 그 외 모든 색은 무채색 +
> 반투명 유리 톤으로 제한한다. 기존 5-토큰(홈/서버 목록/스킨/설정/계정) 텍스트 내비
> 사이드바는 폐기하고 아이콘 전용 플로팅 사이드바로 교체.
>
> 2026-08-08 (2차): 사이드바 전용이던 유리 재질을 카드 전반(히어로, 로그, 모드/스킨
> 목록 아이템, 설정 카드, 계정 카드)으로 확장 — 사용자가 "사이드바만이 아니라 전체를
> 유리로" 요청. 반복 사용을 위해 `Controls/GlassPanel.axaml` 재사용 컨트롤로 표준화하고,
> 톤은 `App.axaml`의 `GlassOverlayBrush`/`GlassBorderBrush`/`GlassShadow` 테마 토큰으로
> 통일. CTA/outline 버튼과 스킨 뷰어 무대 배경은 유리로 바꾸지 않음 — 버튼은 "표면"이
> 아니라 "동작"이고, 무대 배경은 3D 캐릭터를 읽기 위한 고정 콘텐츠 배경이라 성격이
> 다르다고 판단 (근거는 금지 사항 절 참고).
>
> 2026-08-08 (3차): 위 2차 작업에서 사이드바까지 `Controls/GlassPanel.axaml`로 통합했으나,
> "사이드바 디자인은 유지해야지"라는 사용자 피드백에 따라 사이드바는 다시 독립된 마크업 +
> 전용 토큰(`SidebarGlassOverlayBrush`/`SidebarGlassBorderBrush`/`SidebarGlassShadow`,
> `App.axaml`)으로 분리. 톤 값 자체는 카드용 `Glass*` 토큰과 동일하게 맞춰 눈으로 보이는
> 결과물은 같지만, 앞으로 카드 쪽 유리 톤을 조정해도 사이드바는 별도로 관리되어 영향받지
> 않는다. **사이드바는 앞으로도 `GlassPanel`을 재사용하지 말 것** — 사이드바는 이 앱의
> 시그니처 요소이므로 카드 스타일 변경과 분리된 자기 고유의 마크업을 유지해야 한다.
>
> 2026-08-08 (4차): 2차에서 남겨뒀던 "CTA/outline 버튼은 유리로 바꾸지 않는다"는 예외를
> 사용자가 명시적으로 뒤집음 — "내부 버튼들도 유리처럼" 요청, 범위를 CTA/outline 버튼과
> 사이드바 선택 아이콘 캡슐까지 포함해서 확인받음. `Button.cta`/`Button.outline`
> (`App.axaml`)을 카드와 같은 3겹 유리 구조(`ControlTemplate`으로 직접 구현, `Border` +
> `ExperimentalAcrylicBorder` + 틴트 오버레이)로 교체. CTA는 대비를 최대한 지키기 위해
> 라임 오버레이(`AccentGlassOverlayBrush`)를 90% 알파로 남겼고(카드용 60%보다 훨씬
> 진함), outline은 카드와 동일한 `GlassOverlayBrush`/`GlassBorderBrush`를 그대로 재사용.
> 사이드바 선택 아이콘도 기존 불투명 흰/회색 원(`ApplyNavHighlight()`,
> `MainWindow.axaml.cs`) 대신 `NavIconActiveGlassBrush`(75% 알파) + 얇은
> `SidebarGlassBorderBrush` 테두리로 바꿔 사이드바 배경과 같은 유리 톤을 준다. 버튼
> hover 피드백은 브러시 스왑 대신 `Opacity 0.85` 트랜지션으로 단순화(템플릿을 통째로
> 갈아끼우면서 `/template/ ContentPresenter` 셀렉터가 더는 유효하지 않아짐).

## 컬러 토큰

**규칙: 색이라고 부를 수 있는 건 `#89D22F` 하나뿐이다.** 그 외에는 전부 흰색/검정의
불투명도 조절(무채색 + 알파)로만 표현한다. 아래 표의 나머지 항목은 "색상"이 아니라
"무채색의 농도"로 이해할 것.

### 라이트 모드

| 토큰               | 값                                              | 용도                                     |
| ------------------ | ----------------------------------------------- | ---------------------------------------- |
| `--bg`             | `#F4F5F2`                                       | 창 전체 배경 (유리 카드가 비칠 바탕)     |
| `--surface`        | `#FFFFFF`                                       | 유리를 안 쓰는 예외 표면(썸네일 배경 등) |
| `--glass-overlay`  | `#FFFFFF` @ 60% (`#99FFFFFF`)                   | 모든 유리 표면(사이드바+카드) 공통 틴트  |
| `--glass-border`   | `#FFFFFF` @ 40% (`#66FFFFFF`)                   | 유리 표면 하이라이트 테두리, 1px         |
| `--glass-shadow`   | `0 8 24 rgba(0,0,0,.12), 0 1 2 rgba(0,0,0,.08)` | 유리 표면 플로팅 그림자                  |
| `--border`         | `#000000` @ 7%                                  | 구분선, 테두리                           |
| `--text-primary`   | `#1A1B1E`                                       | 본문 텍스트                              |
| `--text-secondary` | `#6B7280`                                       | 보조 텍스트                              |
| `--accent`         | `#89D22F`                                       | 유일한 강조색 (CTA, 선택 상태, 진행바)   |
| `--accent-text`    | `#12210A`                                       | 라임 배경 위 텍스트                      |

> 2026-08-13: 다크 모드 토큰/토글을 전부 제거하고 라이트 모드만 남김 (사용자 요청).
> `App.axaml`은 이제 `ResourceDictionary.ThemeDictionaries` 없이 위 라이트 토큰만
> 평범한 `ResourceDictionary`로 갖고, `RequestedThemeVariant="Light"`로 고정한다.
> 설정 페이지의 다크 모드 토글, `MainWindow.axaml.cs`의 테마 전환 로직
> (`OnThemeToggleRequested`/`UpdateThemeToggle`)과 `SkinsView`의 `RefreshTheme()`도
> 함께 삭제 — 더는 전환할 테마가 없으므로.

**폐기된 토큰**: `--accent-tint`, `--accent-text-on-tint`, `--hero-bg`, `--titlebar-bg`,
`--sidebar-tint`, `--sidebar-material` (카드 전반에 쓰는 `--glass-*` 토큰으로 통합됨).
활성 상태는 틴트 배경이 아니라 "불투명 흰 원 + 그림자로 떠 보이는" 아이콘 캡슐로
표현하므로 별도 라임 틴트 색이 필요 없다.

## 타이포그래피

_(기존과 동일, 변경 없음)_

- **본문/UI 전체**: Pretendard. 없으면 `Segoe UI` / `-apple-system` 폴백.
- **숫자/버전 표시**: `JetBrains Mono`.
- 굵기는 Regular(400) / Medium(500) / SemiBold(600)만 사용.
- 크기 스케일: 히어로 타이틀 22px, 섹션 헤더 18px, 카드 본문 13–14px, 보조 텍스트
  11–12px, 버튼 라벨 13–16px — 사이드바가 아이콘 전용이 되면서 "사이드바 내비 라벨
  13px SemiBold" 항목은 삭제.

## 레이아웃

```
┌───────────────────────────────────────────────────────┐
│ [G] Grin Launcher                                     │  ← 타이틀바, 44px, OS 네이티브 프레임
├──┬────────────────────────────────────────────────────┤
│✦ │                                                     │
│⌂ │                                                     │
│≡ │              페이지별 콘텐츠 (아래 참고)                │
│▤ │                                                     │
│⚙ │                                                     │
│  │                                                     │
│◻ │                                                     │
│🗑 │                                                     │
└──┴────────────────────────────────────────────────────┘
  플로팅 유리       콘텐츠 (padding 40/32)
  사이드바 64px
  (창 가장자리에서 8px 띄움)
```

- **타이틀바 (44px)**: 좌측 로고 배지 + "Grin Launcher"만 표시. OS 네이티브 창 프레임
  유지. 창은 1280×800 기본, 리사이즈 가능, 최소 960×600.
- **사이드바 (64px, 플로팅 알약형 유리 패널)**:
  - 창 왼쪽 가장자리에서 8px 띄운 독립된 캡슐 패널. `CornerRadius 26`, 다른 유리 카드와
    동일한 `GlassPanel` 재질 적용.
  - 텍스트 라벨 없이 아이콘만: 상단에 AI/스파클 액션 하나 → 구분선 → 홈 / 서버 목록 /
    새로 만들기 / 스킨 / **모드팩(선택 상태)** → 긴 구분선(스페이서) → 계정 / 휴지통
    순서로 세로 배치.
  - **선택된 항목**만 유리 원 배경으로 떠 보이게 하고(그림자 없음), 나머지 아이콘은
    배경 없이 outline 라인만 표시.
  - 활성 상태 표현에 라임을 쓰지 않는다 — 선택 = "떠 보이는 흰 원"이라는 형태 대비로만
    표현하고, 라임은 여전히 CTA·진행바 전용으로 아낀다.
  - 사이드바는 로그인 성공 후 메인 셸에서만 렌더링된다.
  - 계정 아바타 요약은 사이드바 폭이 좁아지며 더 이상 텍스트로 들어갈 자리가 없으므로,
    사이드바 최하단 아이콘 하나로 축약해 유지한다.
- **콘텐츠 영역**: 페이지 구성은 홈/스킨/설정 3개 — 계정 페이지는 별도 페이지가 아니라
  설정 페이지에 병합됨(아래 2026-08-14 참고). 카드들은 아래 "카드/버튼" 절 기준으로 전부
  유리 표면.
- 여백은 8px 배수로만 (8 / 16 / 24 / 32 / 40).

## 카드/버튼

- **모든 카드/패널은 유리 표면**이다 (히어로, 로그, 모드/스킨 목록 아이템, 설정 카드,
  계정 카드, 사이드바). `Controls/GlassPanel.axaml`을 재사용해서 구현 — 얇은 하이라이트
  테두리(`--glass-border`) + 실제 블러(`ExperimentalAcrylicBorder`, Digger) + 반투명
  틴트 오버레이(`--glass-overlay`) 3겹 구조. 코너 라디우스: 히어로 카드 20px, 일반
  리스트/패널 카드 14px, 뱃지/버튼 pill(999px).
- **유리가 아닌 예외**:
  - 스킨 목록 아이템의 "적용됨" 표시 테두리는 바인딩된 강조색이 필요해 `GlassPanel` 위에
    별도 `Border` 오버레이로 얹는다 (공유 컨트롤에 억지로 통합하지 않음).
  - 썸네일/아바타처럼 이미지를 얹는 작은 사각 프레임은 `--surface` 불투명 배경 유지 —
    유리 위에 유리를 겹치면 이미지 대비가 떨어진다.
  - 스킨 뷰어의 하늘색→풀색 무대 배경은 유리로 바꾸지 않는다 (아래 금지 사항 참고).
- **버튼**: pill(999px) 라디우스 통일, 카드와 같은 3겹 유리 구조 (2026-08-08 4차, 아래
  "유리가 아닌 예외"의 구 CTA/outline 항목은 폐기). Primary CTA는 라임 유리 —
  `AccentGlassOverlayBrush`(90% 알파, 카드용 60%보다 진하게 잡아 대비를 최대한 지킴) +
  `--accent-text`. 보조 버튼은 카드와 동일한 `GlassOverlayBrush`/`GlassBorderBrush` 톤의
  무채색 유리.
- **사이드바 아이콘 버튼**: 선택 시 `NavIconActiveGlassBrush`(75% 알파) 유리 원 +
  `SidebarGlassBorderBrush` 테두리(2026-08-08 4차, 구 불투명 흰/회색 원에서 전환),
  비선택은 투명. 그림자 없음(2026-08-13, 사용자 요청으로 선택 상태 그림자 제거).

## 유리 재질 구현 메모 (Avalonia)

- **카드류**는 재사용 컨트롤 `Controls/GlassPanel.axaml`로 구현 (`UserControl`,
  `TemplatedControl`이 이미 제공하는 `CornerRadius`를 그대로 활용). 새 카드형 유리
  표면이 필요하면 이 컨트롤을 쓰고, 구조를 다시 손으로 쌓지 않는다.
- **사이드바는 예외** — `GlassPanel`을 쓰지 않고 `MainWindow.axaml` 안에 같은 3겹
  구조를 독립적으로 갖고 있으며, 톤은 `SidebarGlassOverlayBrush`/
  `SidebarGlassBorderBrush`/`SidebarGlassShadow` 전용 토큰을 참조한다 (값은 카드용
  `Glass*` 토큰과 동일하지만 키가 분리되어 있어 서로 영향을 주지 않는다). 사이드바를
  카드와 같은 컨트롤로 묶지 말 것 — 위 컨셉 절의 2026-08-08 (3차) 참고.
- **버튼(`Button.cta`/`Button.outline`)도 예외** — `GlassPanel`이 아니라 `App.axaml`의
  `Style.Setter Property="Template"`에 같은 3겹 구조를 `ControlTemplate`으로 직접 심음
  (2026-08-08 4차). `Button`은 클릭 상태(`:pointerover`/`:disabled`)가 있어 `Content`/
  `Padding`/`HorizontalContentAlignment`/`VerticalContentAlignment`를 전부
  `TemplateBinding`으로 넘겨야 각 사용처의 개별 `Padding` 지정이 그대로 유지된다. hover는
  브러시 스왑이 아니라 `Opacity 0.85` 트랜지션으로 처리 — 템플릿을 통째로 교체하면서
  기존 `/template/ ContentPresenter` 셀렉터가 더는 유효하지 않기 때문.
- 3겹 구조 (카드/사이드바/버튼 공통 패턴):
  1. 바깥 `Border` — `Background="Transparent"`, `BorderBrush="{DynamicResource
GlassBorderBrush}"` 1px, `BoxShadow="{DynamicResource GlassShadow}"`.
     `ExperimentalAcrylicBorder`는 `BorderBrush`/`BoxShadow`를 지원하지 않아 바깥에
     일반 `Border`를 하나 더 씌워야 한다.
  2. `ExperimentalAcrylicBorder` + `ExperimentalAcrylicMaterial` — `BackgroundSource=
"Digger"`로 실제 뒷배경(OS 창 뒤, 데스크톱)을 파고드는 블러를 낸다.
  3. 내부 `Panel` 안에 반투명 `Border`(카드는 `Background="{DynamicResource
GlassOverlayBrush}"`, 사이드바는 `SidebarGlassOverlayBrush}"`,
     `IsHitTestVisible="False"`) + 콘텐츠(카드는 `ContentPresenter`, 사이드바는 직접
     `Grid`).
- **실측 주의**: macOS 빌드에서 `ExperimentalAcrylicMaterial`의 `TintColor`/
  `TintOpacity`/`MaterialOpacity`를 크게 바꿔도 렌더링이 전혀 달라지지 않는 현상을
  확인함 (2026-08-08, 사이드바에서 0.55→0.9까지 올려도 픽셀 단위로 동일). 즉 이 세
  속성은 이 플랫폼에서 신뢰할 수 없다 — 실제로 보이는 톤은 전적으로 2단계 소프트웨어
  오버레이(`GlassOverlayBrush` / `SidebarGlassOverlayBrush`)가 담당한다. Material 값
  자체는 (Windows 등 다른 백엔드 호환을 위해) `TintColor="White"`, `TintOpacity="0.85~
0.9"`, `MaterialOpacity="0.6~0.75"` 정도로 남겨두되, **이 값을 조정해서 톤을 바꾸려
  하지 말 것** — 오버레이 브러시의 알파를 조정한다.
- 오버레이 알파는 60%로 설정 (2026-08-08 최초 구현 시 85%로 너무 진하게 잡아 "그냥
  solid 디자인 아니냐"는 피드백을 받고 낮춤). 더 낮추면 Digger가 끌어오는 OS 창 뒤
  배경(사용자 데스크톱, 창 뒤에 있는 다른 앱 등 예측 불가한 대상)이 그대로 비쳐 다시
  탁하게 보일 수 있으니, 더 투명하게 갈 땐 실제 화면 스크린샷으로 확인하고 조정할 것.
- 부모 `Window`에 `TransparencyLevelHint="AcrylicBlur"`, `Background="Transparent"`가
  없으면 블러가 단색으로 렌더링되니 반드시 함께 설정.
- 사이드바는 콘텐츠와 겹치는 플로팅 레이어이므로, 콘텐츠 영역 `Grid`와 별도 z-order로
  얹거나 `Panel`로 감싸 사이드바를 콘텐츠 위에 오버레이한다 (컬럼 분할이 아니라 오버레이).

> 2026-08-14: `AccountView`를 별도 페이지에서 삭제하고 `SettingsView`에 병합 — "설정뷰에
> 좌측에는 설정 유지, 우측에는 계정을 합쳐달라"는 요청. `SettingsView`를 `Grid
> ColumnDefinitions="*,*"`(`ColumnSpacing 32`)로 나눠 왼쪽 컬럼은 기존 설정 카드(메모리/
> Java 경로/게임 디렉토리) 그대로, 오른쪽 컬럼에 계정 카드(아바타·닉네임·UUID·연결 상태·
> 로그아웃 버튼)를 그대로 옮겨 배치. 두 컬럼 모두 상단에 섹션 타이틀(`설정`/`계정`)을
> 얹어 시각적으로 구분. 사이드바의 계정 아바타 아이콘(최하단)은 페이지가 없어졌으므로
> 클릭 시 설정 페이지로 이동하도록 변경 — 설정 아이콘과 목적지는 같아지지만, 아바타
> 아이콘 자체가 로그인된 스킨 얼굴을 계속 보여주는 요약 기능은 유지되므로 아이콘을
> 없애지 않고 남겨둠(근거: 별도 지시 없었고, 아이콘 제거보다 보수적인 변경).
>
> 2026-08-14 (2차): 위 변경으로 계정 아바타(`NavAccountButton`)와 설정 아이콘이 같은
> `key="settings"`를 공유하게 되면서, 설정 페이지가 활성화될 때마다 `ApplyNavHighlight`가
> 계정 아바타 원에도 유리 하이라이트 배경을 얹는 부작용이 생겼다. 아바타 내부의 32x32
> 얼굴 이미지 Border는 불투명 배경이 없어("썸네일/아바타는 --surface 불투명 배경 유지"
> 규칙과 별개로 이 프레임엔 애초에 배경이 없었음), 실제 MS 로그인 후 스킨 얼굴 뒤로 흰
> 유리 원이 비쳐 보이는 문제로 발견됨("스킨 뒤에 흰색 원 생김" 버그 리포트). 계정
> 아바타는 별도 선택 상태를 가질 필요가 없는 얼굴 요약 프레임이므로, `MainWindow.axaml.cs`의
> `ApplyNavHighlight`에서 `NavAccountButton`만 하이라이트 대상에서 제외(항상 배경 없이
> 얼굴 이미지 그대로) — 설정 아이콘 쪽만 선택 표시를 하도록 수정.

## 시그니처 요소

**진행바**: 기존과 동일 — 얇은 라임 바 + 모노스페이스 퍼센트 숫자. 유일한 동적 요소라는
위치는 변하지 않는다.

**유리 표면 전체**: 이번 개편의 시그니처 요소. 창 안의 모든 카드/패널이 "떠 있는 유리
조각"이라는 인상을 공유하며, 그 위에서 무채색 타이포그래피와 라임 강조색만으로 절제를
유지한다.

> 2026-08-13 (3차): 스킨 목록 선택 표시를 라임 테두리 링에서 사이드바 선택 아이콘과
> 같은 유리 캡슐(`NavIconActiveGlassBrush` 배경 + `SidebarGlassBorderBrush` 테두리
> 1px)로 교체 — "선택된 목록 아이템도 사이드바 배경처럼 글래스 디자인" 요청. 사이드바가
> 이미 "선택 = 라임이 아니라 떠 보이는 흰 유리"라는 규칙을 갖고 있었으므로(위 사이드바
> 절 참고), 리스트 선택 표시도 같은 규칙으로 통일한 것 — 라임은 여전히 CTA/진행바 전용.
> 같은 작업에서 `SkinPreviewImage`(3D 뷰어 초기화 실패 시 2D 폴백)도 원본 텍스처 시트를
> 그대로 Stretch하던 것을, 정면 파츠(머리/몸통/팔/다리)만 오려 3D 뷰어와 같은 정면
> 포즈로 합성한 이미지로 교체 (`SkinsView.axaml.cs`의 `BuildFrontViewBitmap`) — UV
> 언랩 시트를 그대로 보여주면 조각조각 흩어져 알아보기 어려웠음.
>
> 2026-08-14 (3차): 스킨 목록 선택 표시를 "사이드바와 톤만 맞춘 단일 틴트 Border"에서
> "사이드바 배경과 완전히 동일한 3겹 유리 구조"로 교체 — "사이드메뉴배경이랑 무조건
> 일치시켜줘"라는 요청에 따라, 톤(색상값)만 맞추는 수준을 넘어 사이드바의 실제 블러
> 레이어(`ExperimentalAcrylicBorder` + `ExperimentalAcrylicMaterial BackgroundSource=
> "Digger"`, `TintColor #999999`/`TintOpacity 0.2`/`MaterialOpacity 0.6`)까지 그대로
> 복제했다. 브러시 토큰도 `NavIconActiveGlassBrush`(선택 아이콘용) 대신 사이드바 배경
> 자체가 쓰는 `SidebarGlassOverlayBrush`/`SidebarGlassBorderBrush`/`SidebarGlassShadow`로
> 바꿔 이름 그대로 "사이드바 배경"과 일치시켰다. `SkinItem.BorderBrush`/
> `SelectedBackground` 브러시 바인딩은 `IsSelected` bool 하나로 단순화하고, 겹 구조는
> XAML에서 `IsVisible="{Binding IsSelected}"`로 켜고 끈다. 사이드바 쪽 코드/토큰은
> 변경하지 않았다 — "복제"이지 "공유 컨트롤 통합"이 아니므로, 위 유리 재질 구현 메모의
> "사이드바는 독립 마크업 유지" 원칙과도 충돌하지 않는다.
>
> 위 3겹 유리 복제 직후 "선택하면 스킨 이미지가 가려짐"이라는 버그 리포트를 받아 같은
> 날 바로 수정 — `ExperimentalAcrylicBorder`(Digger)는 "자신보다 먼저 그려진 것"을
> 블러 소스로 샘플링하는데, 처음 구현에서 선택 유리 오버레이를 `GlassPanel`(썸네일
> 이미지 포함) 위에 얹어 이미지까지 같이 블러되어 보였다. `Views/SkinsView.axaml`의
> 카드 내부 `Panel`을 [배경 전용 `GlassPanel`(콘텐츠 없음)] → [선택 유리 오버레이] →
> [썸네일/이름/삭제 버튼 콘텐츠]로 z-order를 세 겹으로 분리해, 콘텐츠가 항상 선택
> 유리보다 위(나중에 그려짐)에 오도록 고정했다 — "스킨 레이어가 제일 앞쪽에 배치되면
> 좋겠어" 요청 그대로. 목록 썸네일(`BuildFrontViewBitmap`)은 애초에 모자/재킷 등
> 겉레이어까지 합성해서 만들고 있었으므로, 이 z-order 수정만으로 "helmet 같은 겉
> 스킨도 렌더"까지 같이 해결됨 — 별도로 합성 로직을 건드릴 필요는 없었다.
>
> "스킨은 맨 앞으로 빼주고"를 처음엔 사이드바 아이콘 순서 이동으로 잘못 이해해
> `MainWindow.axaml`의 홈/스킨 `Grid.Row`를 바꿨다가, "사이드바 목록 말고 스킨
> 적용하는 부분 스킨 목록"이라는 정정을 받아 되돌렸다(사이드바는 홈 → 스킨 → 설정
> 원래 순서 유지). 실제 요청은 `SkinsView`의 스킨 목록(카드 리스트) 안에서 현재
> 적용된(선택된) 스킨을 맨 앞 카드로 보여달라는 것이었다. 처음엔
> `_skins.OrderByDescending(s => s.Id == _selectedId)`로 "선택된" 스킨을 앞으로
> 옮겼는데, 이러면 계정에서 불러온 "현재 스킨"(`id="default"`)이 아닌 다른 스킨을
> 적용하는 순간 "현재 스킨" 카드가 뒤로 밀려버렸다. "현재 스킨은 무조건 1번째 자리에
> 고정" 요청에 따라 정렬 기준을 `s.Id == "default"`로 바꿔, 무엇을 적용/선택하든
> `id="default"` 카드만 항상 1번째에 고정되도록 수정(`Refresh()`). 안정 정렬이라
> "default"를 제외한 나머지는 기존(업로드) 순서를 유지하고, 원본 `_skins` 배열
> 자체는 정렬하지 않아 업로드 순서 기록도 보존된다.
>
> 위 z-order 수정 직후 앱이 실행되자마자(로그인 화면 뜨기 전) 크래시하는 리포트를
> 받아 원인을 찾음 — 배경 전용으로 쓰려고 `<controls:GlassPanel CornerRadius="14" />`를
> Content 없이 자체 닫힘 태그로 썼는데, `GlassPanel`(`UserControl`)은 `InitializeComponent()`
> 시점에 자기 자신의 루트 `Border`를 기본 `Content`로 먼저 잡아버리는 구조라 —
> `Content`를 외부에서 명시적으로 안 채우면 내부 `<ContentPresenter Content="{Binding
> #Root.Content}">`가 "자기 자신을 포함하는 루트 Border"를 다시 자식으로 붙이려다
> `InvalidOperationException: The control Border already has a visual parent
> ContentPresenter`로 죽었다. 로그인 화면이 있어도 재현됐던 건 우연이 아니라 매 실행
> 레이아웃 타이밍에 따라 크래시가 나거나 안 나거나 했던 것 — 결국 진단을 위해
> `MainWindow` 생성자에 임시로 로그인 스킵 + 스킨 페이지 강제 표시 코드를 넣어
> 확정 재현한 뒤 원인을 찾고 되돌렸다. 수정은 `<controls:GlassPanel CornerRadius="14">
> <Panel /></controls:GlassPanel>`처럼 빈 `Panel`을 명시적 `Content`로 채워 그 기본값을
> 덮어쓰는 것 — 앞으로 `GlassPanel`을 "배경만" 용도로 쓸 땐 반드시 빈 `Panel`(또는 다른
> 빈 컨트롤)을 Content로 명시할 것, 자체 닫힘 태그로 두지 말 것.
>
> 2026-08-14 (4차): "전체 패널 모두 글래스디자인으로" 요청에 따라 스킨 목록 카드의
> 유리 재질을 선택된 카드 전용에서 모든 카드 공통으로 바꿈 — 지금까지는 사이드바와
> 같은 3겹 acrylic 오버레이가 `IsVisible="{Binding IsSelected}"`로 선택된 카드에만
> 켜졌는데, 이제 항상 켠다. 이 acrylic이 곧 카드의 유일한 배경이 되므로 그 아래 있던
> 배경 전용 `GlassPanel`(위 크래시 항목에서 빈 `Panel`로 땜질했던 그 레이어)은
> 완전히 제거 — 이제 겹쳐 쓰지 않는다(`Views/SkinsView.axaml`에서 `controls:` 네임스페이스
> 자체도 미사용이라 같이 삭제). 모든 카드가 같은 유리 톤을 쓰면서 "어떤 스킨이
> 적용 중인지" 구분이 사라지는 문제가 생겨, 색 대신 테두리 두께로 구분하기로
> 사용자와 확인(선택지: 테두리 두께 vs 완전 동일 — 테두리 두께 선택). 선택된
> 카드는 `BorderThickness 2`, 나머지는 `1`(`SkinItem.CardBorderThickness`,
> `SkinsView.axaml.cs`의 `Refresh()`) — 색상은 그대로 `SidebarGlassBorderBrush`
> 하나만 써서 "선택 = 라임이 아니라 형태 차이"라는 사이드바 규칙과 결을 맞췄다. 새 강조색을 쓰지 않고 기존 아이콘
> 톤(`TextSecondaryBrush`, outline, `StrokeThickness 1.4`)을 그대로 따르는 휴지통
> outline 아이콘으로 처리 — "금지 사항"의 라임 단일 강조색 규칙 유지. 계정에서 불러온
> "현재 스킨"(`id="default"`)은 로컬 파일이 아니라 삭제 대상에서 제외, 카드에 버튼
> 자체를 숨김. 목록 아이템 카드가 `HorizontalContentAlignment="Left"` 때문에 내용
> 크기만큼만 좁게 그려지고 선택 테두리도 같이 좁아지는 문제가 있어 `Stretch`로 바꿔
> 카드/테두리 모두 목록 폭 전체를 채우도록 수정. `SkinsView` 레이아웃도 미리보기+팔
> 두께 토글+목록 헤더는 고정, 카드 리스트에만 `ScrollViewer`를 걸어 목록만 스크롤되게
> 재구성(바깥 `Grid RowDefinitions="Auto,*"`).

## 테마

- 라이트 모드만 지원한다 (2026-08-13, 사용자 요청으로 다크 모드/토글 제거). 설정
  페이지에 있던 토글은 삭제했고, `App.axaml`은 `RequestedThemeVariant="Light"`로 고정.
- 유리 토큰(`GlassOverlayBrush`/`GlassBorderBrush`/`GlassShadow` 등)은 이제 테마
  딕셔너리 없이 `App.axaml`의 평범한 `ResourceDictionary`에 직접 정의되어 있다.

## 금지 사항

- 라임(`#89D22F`) 외 추가 강조색 도입 금지 — 이 규칙은 그대로 유지.
- 그라데이션 배경, 네온 글로우, 과한 그림자 금지 (카드용 옅은 섀도우, 유리 재질은
  예외로 허용). 스킨 뷰어의 하늘색→풀색 미리보기 배경도 예외로 둔다 — UI 크롬이
  아니라 3D 캐릭터가 서 있는 콘텐츠/무대 배경이라 카드·버튼·배지 같은 실제 UI 표면과는
  성격이 다르다고 판단 (2026-08-08 사용자 확인). 이 배경을 유리로 바꾸면 3D 모델 자체가
  블러에 묻혀 오히려 가독성이 떨어지므로 유리 확장 대상에서도 제외한다.
- 아이콘은 outline 스타일 통일 (filled/outline 혼용 금지) — 선택 상태는 배경 원으로만
  구분하고 아이콘 자체를 filled로 바꾸지 않는다.
- 실제로 동작하지 않는 장식용 컨트롤 추가 금지.
