# PRD: Game Server Template Scaffold Scripts

> Feature: `game-server-template-scaffold-scripts`
> 작성일: 2026-05-09
> 작성: PM Agent Team (pm-lead orchestration)
> 상태: Draft v1
> 다음 단계: `/pdca plan game-server-template-scaffold-scripts`
> 선행 PRD: [`game-server-template-from-network-engine.prd.md`](./game-server-template-from-network-engine.prd.md)

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | 직전 사이클(`game-server-template-from-network-engine`)에서 `FastPortGameServerTemplate`을 출시했지만, 새 게임 서버를 시작하려는 사용자는 여전히 (1) `git clone` 또는 GitHub "Use this template", (2) 폴더/파일 명을 수동으로 rename, (3) `RootNamespace`/`AssemblyName`/`PackageId` 수정, (4) 모든 `.cs`의 `namespace`/`using` 치환, (5) `.proto`의 `csharp_namespace` 치환, (6) `.sln` 등록 또는 제거, (7) `git init` 같은 7단계의 수동 yak-shaving을 거쳐야 한다. 5분 echo 부트스트랩보다 *renaming* 단계가 더 오래 걸리는 역설이 발생한다. |
| **Solution** | `scripts/scaffold-game-server.sh` (macOS/Linux) + `scripts/scaffold-game-server.ps1` (Windows) 두 개의 cross-platform scaffolding 스크립트를 제공한다. 사용자가 신규 프로젝트 이름과 대상 경로를 인자로 넘기면, 스크립트가 `FastPortGameServerTemplate/` 디렉토리를 복사하여 모든 `FastPortGameServerTemplate*` 토큰을 사용자 지정 이름으로 안전하게 치환한 뒤, 옵션에 따라 `git init` + `.sln` 등록까지 마친다. v1 범위는 **server 템플릿 단독** (SampleClient, `dotnet new` 등록, NuGet 업로드, GUI는 모두 비범위). |
| **Function/UX Effect** | (1) 한 줄 명령으로 부트스트랩 시간을 7단계 → 1단계로 단축. (2) 사용자가 잘못된 토큰을 놓쳐 빌드가 깨지는 사고 0건 보장 (script가 결정론적). (3) macOS/Linux/Windows 어디서나 동일한 결과물 생성 (cross-platform parity). |
| **Core Value** | "GitHub Template Repository의 'Use this template' 버튼을 누른 직후의 yak-shaving 7단계를 0단계로 만든다." 즉, 직전 사이클이 *템플릿*을 만들었다면, 본 사이클은 *템플릿을 즉시 쓸 수 있게 만드는 도구*를 만든다. |

---

## 1. PM Discovery (Opportunity Solution Tree)

### 1.1 Outcome (북극성)

> **"사용자가 신규 게임 서버 프로젝트를 'echo 동작 시작' 상태로 가져가는 데 걸리는 wall-clock 시간"**
> 측정: 직전 사이클은 echo 동작 자체는 5분 이내로 줄였으나, *renaming* 단계가 새 wall-clock 병목. 목표: rename + bootstrap 합쳐 ≤ 60초.

### 1.2 5-Step Discovery Chain

#### Step 1 — Brainstorm (가능성 발산)

직전 PRD의 `dotnet new` 등록은 의도적으로 v1 비범위로 보류되었다. 그렇다면 사용자가 새 게임 서버를 시작할 때 부담은 어디에 있는가?

- "Use this template" 버튼은 *repo 자체*를 복제한다 — 본 repo는 monorepo이므로 `LibCommons`, `LibNetworks`, `FastPortServer`, `FastPortTestSmokeServer` 등 14개 프로젝트가 따라온다. 사용자는 게임 서버 1개만 원했다.
- 사용자가 직접 폴더를 복사해도 `FastPortGameServerTemplate*` 문자열이 csproj/cs/proto/sln 곳곳에 박혀 있다.
- 직전 사이클의 `EchoHandler`/`Sample.proto`는 *예제로* 의미 있지만, 실제 게임에서는 거의 즉시 지워질 코드다. scaffold 시점에 유지/삭제 결정을 노출할 가치가 있다.
- macOS/Linux 사용자는 `bash` + `sed`로 끝나지만, Windows 사용자가 `git bash` 없이 PowerShell만 쓰면 별도 스크립트가 필요하다.
- "사용자가 만드는 모든 새 게임 서버 프로젝트 이름이 PascalCase + valid C# identifier"라는 가정은 거의 맞지만, 검증 없이는 broken csproj가 생성될 수 있다.
- `dotnet new` 템플릿 등록은 이 모든 것을 우아하게 해결하지만, NuGet 발행/유지보수 비용이 따라온다 (직전 PRD의 R-1, R-2 회피).

#### Step 2 — Assumptions (가정 추출)

| 가정 ID | 내용 | Impact | Risk | 우선순위 |
|---------|------|:------:|:----:|:------:|
| B1 | 사용자가 신규 프로젝트를 시작할 때 *renaming*이 실제 마찰 지점이다 (yak-shaving 가설) | High | Low | P0 |
| B2 | 사용자는 cross-platform `.sh` + `.ps1` 두 스크립트만으로 충분하다 (`dotnet new`까지는 불필요) | High | Med | P0 |
| B3 | 정규식/문자열 치환이 충분히 안전하다 (broken-build 케이스가 드물다) | High | High | P0 |
| B4 | 사용자는 PascalCase + valid C# identifier로 이름을 짓는다 | Med | Med | P1 |
| B5 | 사용자는 이름 충돌 (예: `Application`, `Configuration`처럼 폴더와 같은 이름) 같은 corner case를 만들지 않는다 | Med | High | P1 |
| B6 | 대상 경로는 repo 외부일 때가 더 흔하다 (sln 등록은 옵션) | Med | Low | P2 |
| B7 | `git init`까지 묶어주는 게 가치가 있다 (사용자가 수동으로 init하는 대신) | Low | Low | P2 |

#### Step 3 — Prioritize (Impact × Risk)

P0: B1, B2, B3 — 마찰 가설 + 채널 결정 + 치환 안전성이 v1 성공의 3축
P1: B4, B5 — 입력 검증 + 충돌 방지가 robustness 핵심
P2: B6, B7 — UX 디테일

#### Step 4 — Experiments (가정별 검증 계획)

- **B1**: 본인이 직접 `MyLobbyServer`, `MyChatServer` 같은 toy 이름으로 scaffold 후 echo 동작까지 시간 측정. 60초 ≤ wall-clock?
- **B2**: 본인 환경 (macOS) + Windows 가상 머신에서 동일 스크립트 동작 검증. 사용자 1명에게 README만 보고 Windows에서 시도 요청.
- **B3**: 의도적으로 corner case 입력 (`Application`, `Sample`, `MyApp$bad`, 한글 이름)을 시험하여 broken-build 검출. unit/integration test 자동화.
- **B4, B5**: 입력 검증 함수에 negative test case 5종 이상 (특수문자, 예약어, regex meta, 기존 토큰 충돌, 빈 문자열).

#### Step 5 — Opportunity Solution Tree

```
Outcome: 신규 게임 서버 부트스트랩 wall-clock 추가 단축 (≤ 60초)
│
├── Opportunity 1: "Renaming yak-shaving"
│   ├── Solution 1.1: Cross-platform scaffold 스크립트 (.sh + .ps1) ← v1 채택
│   ├── Solution 1.2: dotnet new template 등록 (NuGet 발행 포함)
│   └── Solution 1.3: GUI installer (Electron/.NET MAUI)
│
├── Opportunity 2: "잘못 치환된 토큰 → 빌드 실패"
│   ├── Solution 2.1: 결정론적 치환 (정규식 정확화 + 화이트리스트 확장자)
│   ├── Solution 2.2: 입력 검증 (C# identifier + 파일시스템 안전 + 충돌 토큰 차단)
│   └── Solution 2.3: scaffold 직후 `dotnet build` smoke 실행 옵션
│
├── Opportunity 3: "Cross-platform 차이점이 결과물을 다르게 만든다"
│   ├── Solution 3.1: Parity matrix 명시 + 양 스크립트가 동일 출력 보장 테스트
│   ├── Solution 3.2: 라인 엔딩(LF) + UTF-8 (no BOM) 강제
│   └── Solution 3.3: 경로 공백/대소문자/심볼릭 링크 정규화
│
├── Opportunity 4: "scaffold 후 사용자가 추가로 해야 하는 일"
│   ├── Solution 4.1: `git init` + initial commit 옵션
│   ├── Solution 4.2: monorepo 내부일 때 `.sln` 등록 자동화
│   └── Solution 4.3: README/QUICKSTART의 placeholder를 사용자 이름으로 치환
│
└── Opportunity 5: "사용자가 v1 비범위 항목을 기대했다가 실망"
    ├── Solution 5.1: README에 v1 비범위 명시 (SampleClient/`dotnet new`/NuGet/GUI 모두)
    └── Solution 5.2: roadmap 후보로 SampleClient + `dotnet new` 명시
```

> v1 권장 솔루션 묶음: **1.1 + 2.1 + 2.2 + 2.3(옵션) + 3.1 + 3.2 + 3.3 + 4.1 + 4.2 + 4.3 + 5.1 + 5.2**

---

## 2. PM Strategy

### 2.1 JTBD 6-Part Value Proposition

| 파트 | 내용 |
|------|------|
| **For** (대상) | 직전 사이클의 ICP와 동일한 Solo Soyoung / Indie Ian / Studio Sora — 본인 + 1-5인 인디 + 5-20인 스튜디오 backend lead |
| **Who** (상황) | `FastPortGameServerTemplate`을 발견했지만, 새 프로젝트 이름으로 cleanly fork할 자동화가 없어 매번 같은 7단계 수동 작업을 반복하는 상황 |
| **The** (제품 카테고리) | Cross-platform 셸 기반 프로젝트 scaffolding 스크립트 (Cookiecutter 정신, NuGet 의존 0) |
| **That** (혜택) | 한 줄 명령으로 폴더/파일/네임스페이스/proto/sln/git까지 일관 치환된, 즉시 빌드 가능한 새 게임 서버 프로젝트 생성 |
| **Unlike** (차별점) | "Use this template" 버튼: monorepo 통째로 복제 / 수동 sed: 잘못 치환 위험 / `dotnet new` 등록: NuGet 발행 부담 / Cookiecutter: Python 의존 / Yeoman: Node.js 의존 |
| **Our product** | bash + PowerShell 표준 도구만 사용, 외부 런타임 의존 0, MIT, repo 안에 머무름, 입력 검증 + 결정론적 치환 + parity 테스트 |

### 2.2 Lean Canvas (9 sections)

| 섹션 | 내용 |
|------|------|
| **1. Problem** | (a) `FastPortGameServerTemplate` rename 7단계 수동 작업 (b) 정규식 직접 작성 시 broken-build 위험 (c) `dotnet new` 등록은 NuGet 발행 부담이 부재한 단계에 비해 과도 |
| **2. Customer Segments** | Primary: 본인/내부 팀 / Secondary: C# 인디 (Persona 1, 2) / Tertiary: 스튜디오 backend lead (Persona 3) |
| **3. Unique Value Proposition** | "한 줄 명령, 외부 런타임 의존 0, 60초 안에 빌드 가능한 새 게임 서버 프로젝트" |
| **4. Solution** | (a) `scripts/scaffold-game-server.sh` (b) `scripts/scaffold-game-server.ps1` (c) 입력 검증 + 결정론적 치환 (d) 옵션 git init + sln 등록 (e) parity 테스트 (f) README 문서 |
| **5. Channels** | Repo `README.md`에 명시, `FastPortGameServerTemplate/README.md` "Quickstart" 섹션 link, 직전 PRD의 §7 roadmap 채널 (Reddit/dev.to)에 자연 노출 |
| **6. Revenue Streams** | 없음 (OSS / MIT) |
| **7. Cost Structure** | 본인 시간(주된 비용). v1 추정 노력: 0.5-1.5 일 (스크립트 2종 + 검증 + README) |
| **8. Key Metrics** | (a) scaffold → echo 시간 측정 (목표 ≤ 60초) (b) corner-case 테스트 통과율 (목표 100%) (c) 외부 사용자 1명이 README만 보고 성공 1회 |
| **9. Unfair Advantage** | 직전 사이클이 이미 고품질 템플릿을 만들었으므로 본 사이클은 *오직 자동화 정확성*만 책임지면 됨. surface가 작아 검증 비용이 낮다 |

### 2.3 SWOT (가벼운 형태 — surface가 작아 짧게)

| | 내용 |
|---|------|
| **Strengths** | 직전 사이클 산출물의 신뢰 위에 얹음, 외부 의존 0, MIT, 표준 셸 도구 |
| **Weaknesses** | 정규식 치환은 본질적으로 fragile, Windows에서 `.sh` 미지원/macOS-Linux에서 `.ps1` 미지원으로 2개 스크립트 유지 부담 |
| **Opportunities** | 향후 `dotnet new` 템플릿으로 자연 승급 가능 (스크립트 로직을 template hook으로 이전) |
| **Threats** | 사용자가 corner case로 broken project 생성 시 신뢰 손상, `dotnet new`이 충분히 좋다는 인식이 v1 채택을 막을 수 있음 |

**SO 전략**: 직전 사이클 신뢰 + 의존 0 → README와 PRD에서 "표준 도구만으로 60초"를 강조
**WT 전략**: corner case 검증 자동화 + `dotnet new` 비교표를 README에 명시 (왜 스크립트인지 honest)

### 2.4 Strategic Frameworks (가볍게 적용)

본 feature는 작은 surface의 도구이므로 무거운 프레임워크는 의도적으로 생략. 다음 두 가지만 적용:

- **YAGNI 검증**: SampleClient scaffolding은 v1에서 제거 — 사용자가 클라이언트가 필요하면 별도 결정 요점이며, 스크립트 복잡도 2배 증가 대비 가치 미확인.
- **Buy vs Build (도구 측면) 비교**: `dotnet new` (build 비용 high, 신뢰 high), Cookiecutter (Python 의존 추가), Yeoman (Node 의존 추가), 자체 스크립트 (build 비용 low, 의존 0). v1에서는 **자체 스크립트 = 사용자 의존 0 + 가장 빠른 실험** 채택.

---

## 3. PM Research

### 3.1 Personas (직전 사이클에서 pull-forward, scaffold 사용 맥락에 맞게 정제)

#### Persona 1 — "Solo Soyoung" (본인/내부 팀 대표)

- **Role**: 1인 개발자, 본 프로젝트 메인테이너
- **Scaffold-specific JTBD**: "다음 toy game server 시작 시 `./scripts/scaffold-game-server.sh MyLobbyServer ~/dev/my-lobby-server`만 치고 즉시 echo 동작까지 가고 싶다."
- **Pain Points (현재)**: `cp -r FastPortGameServerTemplate ~/dev/MyNewGame` → 10여 곳 sed → IDE에서 일부 누락 검출 → `git init` → 5-10분 소요
- **Tools**: macOS, zsh, VS Code, GitHub
- **Success Metric**: scaffold → echo wall-clock ≤ 60초

#### Persona 2 — "Indie Ian" (C# 인디 게임 개발자)

- **Role**: 1-3인 인디 스튜디오, Unity/C# 능숙, 서버는 처음
- **Scaffold-specific JTBD**: "GitHub에서 FastPortSharp을 발견했는데, 'Use this template' 버튼이 monorepo를 다 가져와서 당황. 단일 게임 서버 프로젝트만 깔끔하게 받고 싶다."
- **Pain Points**: monorepo 14개 프로젝트 중 어디부터 봐야 할지 막막, sed 명령에 익숙하지 않음, 정규식 실수 두려움
- **Tools**: Windows + Visual Studio 또는 macOS + Rider, PowerShell 또는 bash
- **Success Metric**: README 보고 한 번에 성공, 아무도 도와주지 않아도 60초 안에 빌드 가능한 새 csproj

#### Persona 3 — "Studio Sora" (소규모 스튜디오 backend lead)

- **Role**: 5-20인 스튜디오 backend/server 담당
- **Scaffold-specific JTBD**: "사내 신규 프로젝트의 일관된 부트스트랩 절차로 채택하고 싶다. CI/CD 파이프라인의 first step에 scaffold 스크립트가 끼어들 수 있어야 한다."
- **Pain Points**: Linux CI runner는 bash, Windows 개발자는 PowerShell, 둘 다 동일 결과 보장 필요. 사내 명명 규약과 충돌 시 명확한 에러 메시지 필요.
- **Tools**: Rider, GitHub Actions/Azure DevOps, Linux + Windows 혼합
- **Success Metric**: CI 환경에서 non-interactive로 안정 동작, exit code 의미 명확

### 3.2 Competitive / Tooling Scan

| 도구 | 카테고리 | 강점 | 약점 | FastPort scaffold 차별 |
|---|---|---|---|---|
| **GitHub "Use this template"** | repo-level fork | 한 클릭, 학습 곡선 0 | monorepo 통째로 복제, rename 안 됨 | 폴더 1개만 복사 + 자동 rename |
| **`dotnet new` 템플릿 (커스텀)** | first-party CLI | Microsoft 표준, NuGet 발행 시 글로벌 검색 가능 | template.json 작성 + symbol 정의 + 변환 규칙 + NuGet 발행 + SemVer 부담 | 셸 스크립트 = 발행 비용 0 |
| **수동 `cp -r` + `sed`** | manual | 즉시 사용 가능 | corner case에 broken, 일관성 없음, 문서화 부담 | 결정론적 + 검증된 + 문서화됨 |
| **Cookiecutter** | Python tool | 변수 치환, hook 풍부 | Python 런타임 필요 | 런타임 의존 0 |
| **Yeoman** | Node.js tool | 풍부한 generator 생태계 | Node.js + npm 필요 | 런타임 의존 0 |
| **Plop.js** | Node.js | 코드 generator 친화적 | Node.js 필요 | 런타임 의존 0 |
| **PowerShell `New-Item` 기반 사내 스크립트** | 일반적 패턴 | 의존 0 | 1회성, 표준화/검증 부재 | 표준화 + parity matrix + 검증 |

#### `dotnet new` 등록 비교 (Special section — 의도적 보류 이유)

| 측면 | `dotnet new` 등록 | scaffold 스크립트 (본 PRD) |
|---|---|---|
| 외부 검색성 | High (`dotnet new search`로 발견 가능) | Low (repo README에서만 발견) |
| 사용자 친숙도 | High (.NET 개발자에게 표준) | Medium (셸 스크립트도 표준) |
| 발행 비용 | NuGet 발행 + SemVer + 호환 매트릭스 + symbol 정의 + 변환 규칙 작성 | 셸 스크립트 작성만 |
| 유지보수 비용 | 엔진/템플릿 변경 시 NuGet bump + template.json 갱신 | 엔진/템플릿 변경 시 스크립트 정규식만 점검 |
| 사용자 의존 | `dotnet` SDK (이미 있음) | bash 또는 PowerShell (이미 있음) |
| 입력 검증 | template engine의 symbol constraint (제한적) | 자유로운 셸 검증 (강력) |
| 사후 처리 (git init / sln 등록) | post-action 사용 (제한적) | 셸로 자유 |
| v1 비범위 결정 사유 | 직전 PRD에서 NuGet 발행 자체를 비범위로 결정 (`R-1`, `R-2` 회피) | 본 PRD의 채택 이유 = 발행 부담 0 |

**결론**: `dotnet new` 등록은 *훌륭한 다음 단계*이지만, 본 사이클의 마찰 지점은 "어디에 발행할지"가 아니라 "rename 자동화"이다. 스크립트가 그 마찰을 즉시 해결하고, 미래에 `dotnet new` 등록으로 매끄럽게 승급(스크립트의 치환 로직을 template hook으로 이전) 가능. 이는 과도한 사전 투자(YAGNI 위반)를 회피하는 의식적 결정이다.

### 3.3 Market Sizing — Skipped

직전 사이클(`game-server-template-from-network-engine`) §3.3과 동일하므로 생략. 본 feature는 동일 SOM(6-25 팀)을 대상으로 한 *마찰 감소 도구*이며, TAM/SAM/SOM이 본질적으로 같다.

### 3.4 Customer Journey Map (Indie Ian, scaffold 시점에 집중)

| 단계 | 행동 | 감정 | Pain | scaffold 개입 |
|------|------|------|------|---------------|
| Discovery | GitHub README에서 "Quickstart"를 본다 | 호기심 | 단계가 많아 보이면 이탈 | 한 줄 명령 강조 |
| Trial | `./scripts/scaffold-game-server.sh MyGame ~/dev/MyGame` 실행 | 긴장 | 동작 안 하면 신뢰 즉시 손상 | 명확한 에러, 입력 검증 친절 |
| Verify | `cd ~/dev/MyGame && dotnet build` | 기대 | broken csproj 시 절망 | 결정론적 치환 + 자동 smoke |
| First echo | `dotnet run` | 안도 | echo 동작이 별개로 안 되면 진척감 사라짐 | 직전 사이클 README가 그대로 동작 |
| Customize | 자기 패킷 추가 | 자신감 | 템플릿 placeholder가 본인 이름으로 치환되어 있어야 자연스러움 | README/QUICKSTART도 치환 |
| Push | `git remote add origin ...` | 만족 | git init이 안 되어 있으면 추가 작업 | 옵션 git init |

---

## 4. Beachhead, ICP, GTM (Execution)

### 4.1 ICP (Ideal Customer Profile, scaffold 맥락)

직전 사이클 ICP와 동일하되, 추가 조건:

- **OS 분포**: macOS/Linux dominant, Windows에서도 명령행 사용 가능 (PowerShell 5+ 또는 PowerShell Core 7+)
- **셸 친숙도**: bash 또는 PowerShell 둘 중 하나에 익숙 (둘 다 강요하지 않음)
- **`dotnet` SDK**: .NET 10 SDK 설치됨 (직전 사이클 prerequisite와 동일)

### 4.2 Beachhead Segment (Geoffrey Moore 4기준)

| 기준 | 평가 | 점수(5) |
|------|------|---------|
| **Identifiable** | 직전 사이클에서 이미 식별된 segment의 *동일 사용자* — 다시 식별할 비용 0 | 5 |
| **Reachable** | repo README + 직전 PRD 채널 (Reddit/dev.to)이 자연 노출 경로 | 4 |
| **Compelling reason to buy** | "같은 사람들이 60초 vs 7단계 수동 사이에서 선택" — 명백한 절감 | 5 |
| **Whole product** | v1: 스크립트 + README + parity 테스트 = MVP 충분. SampleClient/`dotnet new`은 v2 | 4 |

→ **Beachhead = 직전 사이클과 동일한 Solo/Indie C# devs** (Persona 1 + 2). 차이점은 "이미 템플릿을 발견한 직후의 사용자" — funnel의 Trial 단계 진입자.

### 4.3 GTM Strategy (가벼움 — 본 feature는 별도 GTM이 아니라 직전 사이클에 *얹는다*)

**Phase 0 — Internal dogfooding (즉시)**
- 본인이 `MyLobbyServer`, `MyChatServer` 같은 toy 이름으로 5번 이상 scaffold 후 echo 동작 검증.
- macOS + Windows VM (또는 GitHub Actions windows-latest) 양쪽 검증.

**Phase 1 — Soft launch (직전 사이클 v1.0 출시와 동시)**
- `README.md` Quickstart 섹션 갱신: "한 줄 명령으로 시작" 추가.
- `FastPortGameServerTemplate/README.md`의 "Distribution" 섹션 갱신.
- 별도 마케팅 활동 없음 (직전 사이클 채널에 자연 노출).

**Phase 2 — Roadmap signal (M+1 이후)**
- 첫 외부 사용자 1명이 사용했다면 그 결과(60초 측정값)를 dev.to 블로그에 추가.
- `dotnet new` 등록의 cost-benefit 재평가 (사용자 수가 임계 넘으면 승급).

### 4.4 Channels & Metrics

| 채널 | 활동 | 1-3개월 목표 |
|------|------|-----------|
| Repo README | Quickstart에 "한 줄 scaffold" 추가 | scroll/click 측정 (best-effort) |
| `FastPortGameServerTemplate/README.md` | Distribution 섹션에 scaffold 명령 추가 | — |
| Dogfooding | 본인이 새 toy game 1개 이상 scaffold로 시작 | 1+ 회 |
| 외부 사용자 검증 | 1명이 README만 보고 성공 (정성적) | 1 회 |

### 4.5 Battlecards (요약 — 사용자 질문 대응)

| 사용자 질문 | 우리의 대응 |
|---|---|
| "왜 `dotnet new` 안 써?" | "발행 비용을 v1에서 회피했고, 스크립트는 동일 효용을 의존 0으로 제공. 사용자 수가 임계 넘으면 승급 예정." |
| "Cookiecutter면 충분하지 않아?" | "Python 런타임 의존 추가는 .NET 개발자 환경에 비대칭 부담. bash/PowerShell은 OS 표준." |
| "`sed`로 5초만에 짤 수 있는데?" | "1회성은 그렇지만, corner case (regex meta, 토큰 충돌, 라인 엔딩, BOM)를 모두 통과한 검증된 도구는 별개." |
| "Windows 사용자도 `git bash`가 있는데?" | "있는 사람도 있지만, 없는 사람을 차별하지 않는 게 cross-platform parity의 의미." |

### 4.6 Growth Loops

scaffold-specific loop는 작다 (1회성 도구). 직전 사이클의 Dogfood / Content / OSS contributor loop를 *가속*하는 보조 역할.

- **Friction-reduction loop**: scaffold가 빨라질수록 본인 dogfood 빈도 증가 → 템플릿 자체 결함 발견 빈도 증가 → 직전 사이클 품질 향상.

---

## 5. PRD Core (8 sections)

### 5.1 Problem Statement

직전 사이클에서 `FastPortGameServerTemplate`을 출시했지만, 신규 게임 서버 프로젝트를 시작하려면 사용자가 (1) repo clone 또는 폴더 복사, (2) 폴더 rename, (3) `*.csproj`의 `RootNamespace`/`AssemblyName`/`PackageId` 수정, (4) 모든 `.cs`의 `namespace`/`using` 치환, (5) `Sample.proto`의 `csharp_namespace` 치환, (6) `FastPortSharp.sln`에서 등록 변경 또는 제거, (7) `git init` + initial commit이라는 7단계 수동 작업을 거쳐야 한다. 정규식 실수 한 번에 broken csproj가 만들어지며, 5분 echo 부트스트랩 약속이 깨진다.

### 5.2 Goals & Non-Goals

**Goals (v1)**:

- `scripts/scaffold-game-server.sh` (macOS/Linux, bash 4+) 제공
- `scripts/scaffold-game-server.ps1` (Windows, PowerShell 5.1+ 및 PowerShell Core 7+) 제공
- 두 스크립트가 인자 받는 방식 + 결과물이 **bit-for-bit identical** (라인 엔딩/인코딩 제외)
- 신규 프로젝트 이름과 대상 경로를 인자로 받음
- 다음 6가지 치환을 결정론적으로 수행:
  1. 폴더/파일 명: `FastPortGameServerTemplate*` → 사용자 이름
  2. `*.csproj`: `<RootNamespace>`, `<AssemblyName>`, `<PackageId>`
  3. `*.cs`: `namespace`/`using` 선언
  4. `*.proto`: `option csharp_namespace`
  5. `FastPortSharp.sln`: 대상이 repo 내부일 때만 등록
  6. `git init` + initial commit (옵션, 기본 ON)
- 입력 검증 (C# identifier prefix + 파일시스템 안전 + 충돌 토큰 차단)
- 라인 엔딩 LF 강제 (생성된 파일), UTF-8 (no BOM) 강제
- README + 사용 예제

**Non-Goals (v1, roadmap 후보)**:

- `FastPortGameServerTemplate.SampleClient` scaffolding (사용자가 별도로 복사 가능, v2 후보)
- `dotnet new` 템플릿 등록 (직전 사이클에서 의식적으로 보류, 사용자 임계 후 재평가)
- NuGet 패키지 발행 (직전 사이클 결정 유지)
- GUI wrapper (Electron/MAUI 등)
- 다중 템플릿 선택 (현재 템플릿 1종)
- TestSmokeServer / TestLoadRunner 같은 검증 인프라 scaffold

### 5.3 Requirements

#### Functional

- **F-1**: `scripts/scaffold-game-server.sh` 작성 (bash 4+).
- **F-2**: `scripts/scaffold-game-server.ps1` 작성 (PowerShell 5.1+).
- **F-3**: 인자 사양 (양 스크립트 동일):
  - `<NewProjectName>` (위치 인자, 필수): 신규 프로젝트 이름
  - `<DestinationPath>` (위치 인자, 필수): 대상 디렉토리
  - `--no-git` (옵션): `git init`/initial commit 생략
  - `--no-sln-register` (옵션): repo 내부 destination일 때도 `.sln` 자동 등록 생략
  - `--force` (옵션): destination이 존재하면 거절 대신 덮어쓰기 (기본은 거절)
  - `--dry-run` (옵션): 변경 사항만 출력, 파일 수정 안 함
  - `-h` / `--help` / `-?`: 사용법 출력
- **F-4**: 입력 검증 — `<NewProjectName>`는 다음 모두 만족:
  - 정규식 `^[A-Z][A-Za-z0-9]{0,63}$` 일치 (PascalCase, valid C# identifier prefix, 64자 이내)
  - 다음 차단어 목록과 불일치: `Application`, `Configuration`, `Handlers`, `Sessions`, `Telemetry`, `Protocols`, `LibCommons`, `LibNetworks`, `FastPortServer`, `FastPortClient`, `FastPortGameServerTemplate` (template의 기존 토큰 충돌 방지)
  - 모든 OS의 reserved filename과 불일치 (Windows: `CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`)
- **F-5**: 6가지 치환을 결정론적으로 수행 (§5.4 Replacement Specification 참조).
- **F-6**: 결과 파일은 LF 라인 엔딩 + UTF-8 no BOM 강제. PowerShell이 BOM을 추가하지 않도록 명시적 인코딩 지정.
- **F-7**: destination이 이미 존재하고 `--force`가 없으면 exit code 2로 거절.
- **F-8**: scaffold 후 `cd <destination> && dotnet build -c Release` smoke 명령을 README에 명시 (자동 실행은 옵션, v1 기본 OFF — 사용자 의존).
- **F-9**: `--dry-run`은 모든 변경을 stdout에 dump하고 파일을 수정하지 않음. exit code 0.
- **F-10**: 에러 메시지는 사람-친화적이고 (a) 무엇이 잘못됐는지 (b) 어떻게 고치는지 명시.

#### Non-Functional

- **NF-1**: 외부 런타임 의존 0 (bash, PowerShell, `git`, `dotnet`만 사용. `sed`, `awk`는 OS 표준 도구로 사용 가능하나 BSD/GNU 차이 회피).
- **NF-2**: 실행 시간 ≤ 5초 (10K 파일이 아닌 ~30 파일 대상이므로 여유).
- **NF-3**: idempotent — `--dry-run`을 제외하고 *동일 인자 재실행*은 destination이 이미 존재하므로 거절(F-7) → 명시적으로 idempotent 보장 (덮어쓰기는 `--force`로만).
- **NF-4**: cross-platform parity — macOS/Linux/Windows에서 결과 파일이 라인 엔딩(둘 다 LF)/인코딩(둘 다 UTF-8 no BOM)/내용 모두 동일.
- **NF-5**: CI 친화 — non-interactive, exit code 의미 있음, stderr는 에러만, stdout은 진행/결과.
- **NF-6**: ShellCheck (`.sh`) + PSScriptAnalyzer (`.ps1`) 통과.

### 5.4 Replacement Specification (결정론적 치환)

#### 5.4.1 화이트리스트 확장자

치환 대상 확장자 (이외는 binary로 간주, 변경 없음):
`.cs`, `.csproj`, `.proto`, `.sln`, `.json` (단, `appsettings.json` 등 안에 `FastPortGameServerTemplate` 토큰이 들어 있을 때만), `.md` (README 등 placeholder 치환용)

#### 5.4.2 치환 규칙 (정확한 토큰)

| 단계 | Source 토큰 | Target 토큰 | 적용 위치 |
|---|---|---|---|
| 1 | `FastPortGameServerTemplate` | `<NewProjectName>` | 모든 화이트리스트 확장자 텍스트 + 폴더/파일명 |

> 단일 토큰만 치환하므로 정규식 충돌 위험이 본질적으로 작음. `FastPortGameServerTemplate`는 26자의 unique compound name이고, source 코드 내 다른 곳에 우연히 등장하지 않는다 (직전 사이클 PRD §5.4 architecture 경계로 보장됨).

#### 5.4.3 치환에서 *제외*되는 케이스 (사용자 우려 대응)

다음 케이스에서는 *원본 토큰을 보존*하여 broken-build/오작동 방지:

- 주석/문서 내 *upstream repo reference*: 이 토큰은 v1에서는 *모두 치환*하는 것이 가장 단순. README/주석에 "this is forked from FastPortGameServerTemplate"라는 문장은 새 프로젝트에서 *의미 없으므로* 사용자 이름으로 치환되는 것이 오히려 자연스럽다. **결정**: v1에서는 단일 토큰을 모두 치환. 만약 사용자가 upstream reference를 보존하고 싶다면 scaffold 후 README를 수동 수정. (트레이드오프를 README에 명시.)
- Binary 파일 (`.dll`, `.exe`, `.png` 등): 화이트리스트 확장자에 포함되지 않으므로 자동 제외.
- `bin/`, `obj/`, `.git/`, `.vs/`, `.idea/` 폴더: scaffold 시 제외 (복사 자체를 안 함).

#### 5.4.4 정규식 metacharacter 안전성

`<NewProjectName>`은 입력 검증(F-4)에서 `^[A-Z][A-Za-z0-9]{0,63}$`로 강제되므로 정규식 metacharacter (`. * + ? ( ) [ ] { } \ | ^ $`)가 본질적으로 포함될 수 없다. 따라서 치환 시 *target side* escape 부담 없음. *source side*인 `FastPortGameServerTemplate`도 모두 alphanumeric이므로 안전.

#### 5.4.5 `.sln` 등록

```
destination_path가 repo의 sub-path인가?
├── Yes (and --no-sln-register 없음): FastPortSharp.sln에 신규 csproj entry 추가
└── No: 등록 단계 완전 skip (warning 출력)
```

판정: `realpath <destination>`이 `realpath <repo-root>`로 시작하는지로 결정.

#### 5.4.6 `git init`

```
--no-git 없음:
  cd <destination>
  git init
  git add .
  git commit -m "Initial scaffold from FastPortGameServerTemplate (<commit-sha-of-source-repo>)"
```

initial commit message에 source commit SHA를 포함하여 *어느 버전의 템플릿에서 fork했는지* 추적 가능.

### 5.5 Cross-Platform Parity Matrix

| 차원 | macOS (bash 4+) | Linux (bash 4+) | Windows (PowerShell 5.1) | Windows (PowerShell Core 7+) | 비고 |
|---|---|---|---|---|---|
| 인자 파싱 | `getopts` 또는 manual | 동일 | `param()` block | 동일 | 인자 사양 동일 |
| 폴더 복사 | `cp -R` | `cp -R` | `Copy-Item -Recurse` | 동일 | symlink follow 안 함 |
| 텍스트 치환 | `sed -i ''` (BSD) / `sed -i` (GNU) → 회피 위해 `awk` 또는 임시 파일 | 동일 | `(Get-Content) -replace + Set-Content` | 동일 | BSD/GNU sed 차이 회피 |
| 파일 인코딩 | UTF-8 (기본) | UTF-8 | **명시적** UTF8NoBOM | UTF-8 (기본) | PowerShell 5.1은 기본이 UTF-16 LE BOM, 명시 필수 |
| 라인 엔딩 | LF | LF | **CRLF→LF 변환 필수** | **CRLF→LF 변환 필수** | git autocrlf 영향 회피 |
| 경로 구분자 | `/` | `/` | `\` (PowerShell 자동 처리) | 동일 | PowerShell이 forward slash도 받음 |
| 경로에 공백 | 따옴표 필수 (`"$dest"`) | 동일 | `"$dest"` | 동일 | bash/PS 모두 quoting 필수 |
| 경로에 한글/유니코드 | UTF-8 locale 가정 (`LANG=en_US.UTF-8` 등) | 동일 | UTF-8 코드페이지 (chcp 65001) 권장 | UTF-8 (기본) | Windows 5.1은 한글 경로 시 chcp 65001 권장 |
| 대소문자 | case-sensitive | case-sensitive | case-insensitive | case-insensitive | `FastPortGameServerTemplate` vs `fastportgameservertemplate` 매칭 — bash 스크립트는 대소문자 정확 매칭, PowerShell도 명시적 case-sensitive flag 사용 |
| `git` 호출 | `git` (PATH) | 동일 | `git.exe` (PATH) | 동일 | 미설치 시 명확한 에러 |
| `dotnet sln` 호출 | `dotnet sln add` | 동일 | 동일 | 동일 | 둘 다 표준 명령 |
| Exit code | bash 표준 | 동일 | `exit 1` 등 명시적 | 동일 | 0=성공, 2=인자/입력 오류, 3=destination 충돌, 4=시스템 오류 |
| 권한 | `chmod +x scaffold-game-server.sh` | 동일 | (PS는 ExecutionPolicy 영향) | 동일 | README에 ExecutionPolicy 가이드 |
| 테스트 환경 | macOS GHA runner | ubuntu-latest | windows-latest (powershell 5.1) | windows-latest (pwsh 7) | CI에서 4 환경 모두 검증 |

### 5.6 User Stories (INVEST)

| ID | Story | Priority |
|---|---|---|
| US-1 | As Solo Soyoung, `./scripts/scaffold-game-server.sh MyLobbyServer ~/dev/MyLobbyServer` 실행 후 60초 내 빌드되는 새 프로젝트 획득 | P0 |
| US-2 | As Indie Ian (Windows), `.\scripts\scaffold-game-server.ps1 MyLobbyServer C:\dev\MyLobbyServer` 실행 후 동일 결과 획득 | P0 |
| US-3 | As Studio Sora, CI 환경에서 `--no-git` 옵션으로 non-interactive scaffold 가능, exit code로 성공/실패 판별 | P0 |
| US-4 | As Solo Soyoung, `--dry-run`으로 변경 사항을 사전 확인 가능 | P1 |
| US-5 | As Indie Ian, 잘못된 이름 입력 시 (`my-game`, `Application`, `MyApp$`) 친절한 에러 + 재시도 가이드 | P0 |
| US-6 | As Solo Soyoung, scaffold 결과물의 `git log`에서 source repo의 commit SHA 추적 가능 | P2 |
| US-7 | As Indie Ian, repo 외부 destination 사용 시 `.sln` 등록이 자동으로 skip되며 그 이유가 stdout에 명시 | P1 |
| US-8 | As Studio Sora, destination이 이미 존재할 때 `--force` 없이는 보호되어 실수로 기존 작업 파괴 방지 | P0 |

### 5.7 Test Scenarios

| ID | 시나리오 | 검증 |
|---|---|---|
| T-1 | macOS bash로 `MyLobby ~/tmp/MyLobby` scaffold | 결과 디렉토리 빌드 OK (Release 0 warning) |
| T-2 | ubuntu-latest로 동일 명령 실행 | T-1과 결과물이 byte-identical (라인 엔딩/인코딩 강제) |
| T-3 | windows-latest PowerShell 5.1로 동일 명령 (경로만 Windows) | T-1, T-2와 byte-identical |
| T-4 | windows-latest PowerShell 7로 동일 명령 | T-1, T-2, T-3과 byte-identical |
| T-5 | 잘못된 이름 입력 (`my-game`, `1Game`, `Application`, `MyApp$`, ``) | 모두 exit code 2 + 친절한 에러 |
| T-6 | 경로에 공백 포함 (`~/My Games/Lobby Server`) | 정상 처리 |
| T-7 | 경로에 한글 (`~/dev/내게임`) | 정상 처리 (UTF-8 locale 가정) |
| T-8 | destination 이미 존재 + `--force` 없음 | exit code 3 + 거절 메시지 |
| T-9 | destination 이미 존재 + `--force` 있음 | 덮어쓰기 성공 |
| T-10 | `--dry-run` | 파일 수정 0건 + 변경 목록 stdout |
| T-11 | repo 내부 destination | `.sln` 자동 등록 + `dotnet build FastPortSharp.sln` 통과 |
| T-12 | repo 외부 destination | `.sln` skip + `dotnet build <new-csproj>` 통과 |
| T-13 | `--no-git` | `.git/` 없음 |
| T-14 | git init 기본 동작 | initial commit + commit message에 source SHA 포함 |
| T-15 | scaffold 후 `dotnet run` + sample client로 echo 1001/1002 round-trip | RTT 로그 확인 |
| T-16 | ShellCheck `scaffold-game-server.sh` | 0 finding |
| T-17 | PSScriptAnalyzer `scaffold-game-server.ps1` | 0 finding (Information 이상) |
| T-18 | scaffold 후 모든 `.cs`/`.proto`/`.csproj`에 `FastPortGameServerTemplate` 토큰 0개 (단, README에 의도적 reference는 허용 — §5.4.3) | grep -r 0 hits in code files |
| T-19 | name이 정규식 meta를 *우회*하려는 시도 (이론적으로 불가능하지만 sanity) | 입력 검증에서 차단 |
| T-20 | Windows에서 PowerShell ExecutionPolicy `Restricted` | README 가이드대로 `-ExecutionPolicy Bypass` 또는 RemoteSigned로 동작 |

### 5.8 Pre-mortem (Top 3+ Risks — 사용자 명시 요청)

> "프로젝트가 6개월 후 실패했다면 그 이유는?"

| Risk | 발생 시 영향 | 완화 |
|------|------|------|
| **R-1: 정규식 metacharacter / 토큰 충돌로 broken csproj 생성** (B3, B5 가정 실패) | 첫 외부 사용자 신뢰 손상, 본 feature 무가치 | (a) 입력 검증으로 source/target 양쪽 메타문자 본질적 배제 (§5.4.4) (b) 차단어 목록 (`Application`, `Configuration` 등 §5.4.2/F-4) (c) T-5/T-19 자동화 (d) `--dry-run` 옵션 (e) scaffold 후 smoke build 권장 (README) |
| **R-2: Source 코드의 string literal/주석 안에 `FastPortGameServerTemplate`이 의도적으로 들어 있어, 치환되면 안 되는데 치환됨** | 원본 references 깨짐 (예: README의 "forked from") | (a) v1에서는 *모두 치환*하는 것이 단순 + 사용자 의도와 정렬 (새 프로젝트에서 upstream reference는 의미 적음) (b) README에 트레이드오프 명시 (§5.4.3) (c) `--dry-run`으로 사용자가 검토 가능 (d) v2 후보: `--preserve-upstream-references` 플래그로 README/CHANGELOG 등 특정 파일 제외 |
| **R-3: Cross-platform parity 깨짐 — Windows에서 BOM 추가/CRLF 변환** | windows 사용자가 만든 프로젝트가 Linux CI에서 깨짐, parity 약속 위반 | (a) PowerShell 5.1 명시적 UTF8NoBOM 강제 (b) git에 LF 강제 (`.gitattributes`) (c) CI에서 4 환경 매트릭스 검증 (T-1~T-4 byte-identical) (d) parity 깨지면 release block |
| **R-4: 사용자가 `dotnet new`이 더 좋다고 판단하고 본 도구를 무시** (B2 가정 실패) | 본 feature 채택 0, 작업 낭비 | (a) README에 명시적 비교표 (§3.2 dotnet new 비교) (b) "발행 비용 0"을 honest하게 강조 (c) 사용자 임계 넘으면 `dotnet new`으로 매끄럽게 승급 가능한 설계 (치환 로직을 template hook으로 이전) (d) 30일 후 외부 사용자 0명이면 dotnet new 등록을 우선순위로 재평가 |
| **R-5: 사용자가 한글/공백/심볼이 들어간 경로로 시도해 silent하게 깨짐** | 사용자 신뢰 손상 (B4 가정 실패) | (a) 입력 검증으로 *프로젝트 이름*은 strict (영문/숫자) (b) *경로*는 quoting 강제 + locale UTF-8 가정 명시 (c) README에 example로 공백/한글 경로 검증 (T-6, T-7) |
| **R-6: scaffold 직후 사용자가 `dotnet build`만 하고 echo 검증을 안 해서, 빌드는 되지만 런타임 broken** | 첫 사용 경험 부정적 | (a) README의 5분 echo 가이드를 scaffold README에도 포함 (placeholder 치환됨) (b) v2 후보: `--smoke-build` 옵션으로 scaffold 후 `dotnet build` 자동 실행 |

### 5.9 Stakeholder Map

| Stakeholder | 역할 | 관여도 |
|---|---|---|
| 본인 (boinred) | 메인테이너, primary user, 첫 dogfooder | Owner |
| 직전 사이클의 `FastPortGameServerTemplate` | 본 feature의 의존 (template 자체가 변하면 스크립트도 갱신) | High (coupling) |
| 외부 인디 개발자 (Indie Ian) | beachhead user | Medium |
| 외부 스튜디오 (Studio Sora) | CI 통합 사용자 | Low → Medium (M3+) |
| GitHub Actions | CI parity 검증 환경 (4 OS/Shell 매트릭스) | Low (지속 비용 0) |
| `dotnet` SDK / `git` | 사용자 환경 prereq | Low (already required) |

---

## 6. Special Decisions (사용자 명시 요청 항목)

### 6.1 왜 셸 스크립트, `dotnet new` 등록이 아닌가? (Honest comparison)

§3.2의 `dotnet new` 비교표 참조. 요약:

- 직전 PRD에서 NuGet 발행을 의식적 비범위로 결정 (`R-1` 외부 채택 불확실 + `R-2` 발행 부담 회피)
- 본 사이클의 마찰 지점은 *발행 채널 부재*가 아니라 *rename 자동화 부재*
- 셸 스크립트 = 발행 비용 0 + 사용자 의존 0 + 즉시 실험 가능
- 사용자 임계 (예: 외부 사용자 5+) 넘으면 `dotnet new` 등록으로 승급 (치환 로직을 template hook으로 이전 가능한 설계)

### 6.2 v1 비범위 (Out-of-Scope, 명시)

- `FastPortGameServerTemplate.SampleClient` scaffolding (v2 후보)
- `dotnet new` 템플릿 등록 (사용자 임계 후 재평가)
- NuGet 패키지 발행
- GUI/web wrapper
- 다중 템플릿 선택
- TestSmokeServer / TestLoadRunner scaffold
- `.gitattributes`/`.editorconfig` 자동 생성 (template 자체에 이미 들어 있다면 복사됨, 없으면 v2)
- 사용자 라이선스 자동 작성 (사용자 결정 사항)

### 6.3 정규식 치환 안전성 — 결정 근거

§5.4.4 + R-1 mitigation 참조. 핵심:

- `FastPortGameServerTemplate` 26자 unique compound name → 우연 충돌 없음
- 입력 검증으로 target name이 alphanumeric으로 강제 → metacharacter 본질적 배제
- 차단어 목록으로 의도적 충돌 차단 (`Application`, `Configuration` 등 — 폴더명과 일치하는 토큰)
- `--dry-run`으로 사용자가 사전 검토 가능
- T-5/T-18/T-19 테스트로 자동 회귀 검증

---

## 7. Roadmap (Indicative)

| 버전 | 시점 | 내용 |
|------|------|------|
| **v1.0** | 본 사이클 | Cross-platform scaffold scripts + parity matrix + README + 20개 테스트 시나리오 통과 |
| **v1.1** | M+1 | `--smoke-build` 옵션 (scaffold 직후 `dotnet build` 자동 실행), `--preserve-upstream-references` 플래그 |
| **v1.2** | M+2 | SampleClient scaffolding 옵션 (`--with-sample-client`) |
| **v2.0** | 사용자 임계 후 | `dotnet new` 템플릿 등록 (치환 로직을 template hook으로 이전, 셸 스크립트는 deprecation 안 하고 유지) |
| **v3.0** | 12M+ | 다중 템플릿 (lobby/turn-based/chat 변형 선택) |

---

## 8. Success Criteria (v1)

- [ ] `scripts/scaffold-game-server.sh` + `scripts/scaffold-game-server.ps1` 제공
- [ ] T-1 ~ T-20 모든 테스트 시나리오 통과 (특히 T-1~T-4 byte-identical parity)
- [ ] 본인 dogfooding: scaffold → echo 동작까지 wall-clock ≤ 60초 (3회 이상 측정)
- [ ] README에 `dotnet new` 비교 + Quickstart + ExecutionPolicy 가이드 + 트레이드오프 명시
- [ ] ShellCheck + PSScriptAnalyzer 0 finding
- [ ] GitHub Actions에서 macOS / ubuntu / windows-ps5.1 / windows-ps7 4환경 CI 매트릭스 통과
- [ ] HANDOFF.md "Important Architecture Decisions" 위반 없음 (`FastPortGameServerTemplate`은 여전히 `LibCommons` + `LibNetworks`만 의존, scaffold 결과물도 동일)

---

## 9. Attribution

본 PRD는 [pm-skills](https://github.com/phuryn/pm-skills) (Pawel Huryn, MIT)의 framework들을 차용:

- 5-Step Discovery Chain & Opportunity Solution Tree (Teresa Torres 영감)
- JTBD 6-Part Value Proposition
- Lean Canvas (Ash Maurya)
- Beachhead 4-criteria (Geoffrey Moore — Crossing the Chasm)
- Pre-mortem (Gary Klein)

직전 사이클 PRD `game-server-template-from-network-engine.prd.md`의 페르소나/시장 분석을 pull-forward하여 *마찰 감소 도구* 맥락에 맞게 정제. 프로젝트별 컨텍스트는 `README.md`, `HANDOFF.md`, `FastPortGameServerTemplate/` 산출물, git history에서 합성됨.

---

> **다음 단계**: `/pdca plan game-server-template-scaffold-scripts`
> (본 PRD가 Plan 문서에 자동 참조됩니다.)
