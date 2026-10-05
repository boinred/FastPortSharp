# 공통 기반 (빌드·테스트·CI·브랜치·개발 환경)

여러 도메인이 함께 쓰는 빌드·테스트 명령, CI, 브랜치 규칙, 개발 환경을 모았다. 코드 스타일과 커밋 규칙은 루트 `AGENTS.md`에 있다.

**이 문서를 읽는 경우**: 빌드·테스트 실행, 테스트 하나만 돌리기, CI 실패, 워크플로 수정, 필수 체크, PR 머지 조건, 릴리스 승격, 로컬 개발 환경 준비, 줄바꿈(LF/CRLF) 문제, 솔루션 구성.

**다른 문서로 가는 경우**: scaffold 스크립트·golden 테스트 → [game-server-template.md](game-server-template.md). 대시보드 MAUI 빌드 → [dashboard.md](dashboard.md). 부하 검증 실행 → [load-testing.md](load-testing.md).

## 핵심 규칙

- **`main`에는 직접 push하지 않는다.** PR + 필수 체크 3개(`build (ubuntu-latest)`, `build (macos-latest)`, `build (windows-latest)`) 통과 후 merge commit으로 머지한다.
- **job `name`을 바꾸면 ruleset 필수 체크 이름도 바꾼다.** 이름이 안 맞으면 체크가 영원히 대기 상태가 되어 PR이 머지되지 않는다. ruleset은 GitHub 저장소 설정(Rules → Rulesets → `main-protection`)에서 저장소 소유자가 고친다.
- 릴리스 브랜치는 `builds/release`다. `main` → `builds/release` PR로 승격한다. `build.yml`, `dashboard.yml`이 두 브랜치를 모두 감시한다.
- **줄바꿈은 LF다.** `.gitattributes`가 `*.sln`만 CRLF로, 나머지 텍스트는 LF로 고정한다. scaffold 결과를 OS 사이에 바이트 단위로 비교하기 때문이다. Windows CI는 checkout 전에 `core.autocrlf false`를 설정한다.
- `FastPortSharp.sln`에는 대시보드 프로젝트가 없다. 대시보드는 `FastPortSharp.Dashboard.sln`으로 빌드한다.

## 솔루션·프로젝트 공통 설정

| 항목 | 값 | 위치 |
|---|---|---|
| Target framework | `net10.0` | 각 `*.csproj` (`Directory.Build.props` 없음) |
| Nullable / ImplicitUsings | `enable` / `enable` | 각 `*.csproj` |
| 테스트 | MSTest, `FastPortTests`는 메서드 단위 병렬 | `tests-projects/FastPortTests/MSTestSettings.cs` |
| proto 코드 생성 | `<Protobuf Include>` 빌드 항목(`Protocols`는 `Grpc.AspNetCore`, 템플릿·SampleClient·Dashboard.Core는 `Grpc.Tools`) | `Protocols/Protocols.csproj`, 템플릿·SampleClient·`FastPortDashboard.Core` csproj |
| 포맷터·분석기 설정 | 없음(`.editorconfig` 없음). 스타일은 `AGENTS.md` 규칙과 리뷰로 지킨다 | — |

## 빌드·테스트

| 목적 | 명령 |
|---|---|
| 엔진·도구 빌드 | `dotnet build FastPortSharp.sln -c Release` |
| 엔진·도구 테스트 전체 | `dotnet test FastPortSharp.sln -c Release` |
| 테스트 클래스 하나 | `dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~BaseSessionReceivePolicyTests"` |
| 테스트 메서드 하나 | `dotnet test tests-projects/FastPortTests -c Release --filter "Name=BaseSession_InvalidPacketHeaderOnly_DisconnectsWithoutDelivery"` |
| 대시보드 Core 테스트 | `dotnet test tests-projects/FastPortDashboardTests -c Release` (MAUI workload 불필요) |
| scaffold golden 테스트 | `tests/scaffold/run.sh` (PowerShell 버전 검사: `--script ps1`) |
| golden 갱신 | `tests/scaffold/run.sh --update-golden case-01-simple` |

- 엔진(`LibCommons`, `LibNetworks`)을 고쳤으면 `FastPortSharp.sln` 전체 테스트와 scaffold 테스트를 모두 돌린다. 엔진은 템플릿·SmokeServer·Dashboard가 함께 쓴다.
- 소켓 테스트는 loopback 포트 0을 쓴다. 고정 포트를 쓰면 병렬 실행에서 충돌한다.

## CI 워크플로

| 워크플로 | 트리거 | job 이름 | 내용 |
|---|---|---|---|
| `.github/workflows/build.yml` | `main`, `builds/release` push·PR, 수동 | `build (ubuntu-latest)`, `build (macos-latest)`, `build (windows-latest)` | `FastPortSharp.sln` restore → Release build → test. **main 필수 체크** |
| `.github/workflows/dashboard.yml` | 위 두 브랜치 + 대시보드·`LibTestTelemetry`·엔진·`FastPortSharp.Dashboard.sln` 경로 변경 | `dashboard (macos-latest)`, `dashboard (windows-latest)` | MAUI workload 설치(`--version 10.0.401` 고정) → restore(`-p:Configuration=Release`) → build → test |
| `.github/workflows/scaffold.yml` | `main` push·모든 PR 중 scaffold 스크립트·`tests/scaffold`·템플릿·`template-projects/Protos`·엔진·`.gitattributes` 경로 변경, 수동 | `<os> / <sh\|ps1>`, `cross-OS byte-identical compare` | 3개 OS × sh/ps1로 scaffold 케이스 실행 후 결과 sha256을 OS 사이에 비교 |

- CI 실패를 볼 때는 먼저 base 브랜치(`main`)에서도 같은 job이 실패하는지 확인한다.
- `dashboard` job의 MAUI workload 버전을 올리면 runner의 Xcode가 요구하는 MacCatalyst SDK를 지원하는지 확인한다 → [dashboard.md](dashboard.md).
- scaffold windows/ps1 job은 오래 걸린다(관측값 약 16분).

## 개발 환경

- .NET SDK 10이 필요하다(CI는 `actions/setup-dotnet`의 `10.0.x`).
- scaffold ps1 검사를 Linux·macOS에서 돌리려면 PowerShell 7(`pwsh`)이 필요하다. 전역 도구로 설치할 수 있다: `dotnet tool install --global PowerShell`.
- 대시보드 빌드는 macOS 또는 Windows와 MAUI workload가 필요하다.
- macOS는 `/var`가 `/private/var`의 symlink다. 절대 경로로 sln을 빌드하면 같은 프로젝트를 두 경로로 restore해 충돌할 수 있다. scaffold smoke build가 대상 폴더로 이동한 뒤 상대 경로로 빌드하는 이유다.
- Claude Code 프로젝트 설정은 `.claude/settings.json`이다. `superpowers@claude-plugins-official` 플러그인을 켜고 공식 마켓플레이스를 등록한다.

## 읽지 않아도 되는 곳

- `.vscode/`: 개인 편집기 설정.
- `FastPortSharp.sln`의 "솔루션 항목"에 있는 `docs\latency-*.md`, `docs\baseline-benchmark-results.md`는 이미 지워진 파일을 가리킨다. 열 필요 없다.
