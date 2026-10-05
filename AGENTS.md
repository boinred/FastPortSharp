# Agent Instructions (Codex / Claude Code 공통)

LLM 에이전트(Codex, Claude Code 등)가 이 저장소에서 서버 코드를 개발할 때 따르는 규칙이다. 규칙은 이 파일 한 곳에서만 고친다.

## Language

- 기본 응답 언어는 한국어로 한다.
- 사용자가 다른 언어를 명시적으로 요청한 경우에만 해당 언어로 답한다.
- 코드, 명령어, 파일 경로, API 이름, 에러 메시지는 원문을 유지하고, 설명은 한국어로 작성한다.

## Coding Skill Rule

- For code writing, editing, refactoring, debugging, test work, and code review, use the `karpathy-guidelines` skill before starting implementation.
- Apply the skill with emphasis on explicit assumptions, simplicity first, surgical changes, verifiable success criteria, and verification.
- If the skill is not available in the current session, read `/Users/boinred/.codex/skills/karpathy-guidelines/SKILL.md` and follow those instructions as the fallback.

## 코드 탐색 규칙

- 코드를 탐색하기 전에 `docs/llm/README.md`를 먼저 읽는다. "도메인 고르기" 표에서 요청에 맞는 도메인 문서 1~2개를 골라, 그 문서의 "작업별 시작점"에 있는 파일만 연다.
- 용어(특히 `BaseSessionClient`/`BaseSessionServer`처럼 이름과 역할이 엇갈리는 것)는 `docs/llm/glossary.md`에서 확인한다.
- 심볼은 이름으로 찾는다. 예: `grep -n "void RequestDisconnect" -r LibNetworks`. 문서에는 줄 번호가 없다.
- `docs/llm/README.md`의 "큰 파일" 표에 있는 파일은 통째로 읽지 않는다. grep으로 위치를 찾고 그 부분만 읽는다. 이미 대화에서 확인한 내용은 다시 읽지 않는다.
- 문서와 코드가 다르면 코드를 믿는다. 작업이 끝나면 문서를 고친다.

## 문서 유지 규칙

- 커밋하기 전에 이번 변경이 `docs/llm/` 문서 내용과 맞는지 확인한다.
- 파일·타입·public/protected 멤버·패킷 ID·설정 키·명령어·CI job을 추가, 이동, 삭제, 변경했으면 해당 도메인 문서를 먼저 고친다.
- 문서 수정은 코드 변경과 **같은 커밋**에 넣는다. 코드를 커밋한 뒤 문서를 따로 커밋하지 않는다.
- 수정 기준은 `docs/llm/README.md`의 "문서 유지 규칙"을 따른다.

## C# 코드 스타일

대상: .NET 10 / C# 14 (`<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`). 포맷터·분석기 설정 파일(`.editorconfig`)은 아직 없으므로 아래 규칙은 리뷰로 지킨다.

- 네임스페이스는 file-scoped(`namespace LibNetworks.Sessions;`)로 쓴다.
- 새 파일은 UTF-8, LF로 저장한다. 기존 파일의 인코딩은 요청 없이 바꾸지 않는다. 일부 파일(`LibCommons/LatencyStats.cs` 등)은 CP949라서, 다시 저장하면 diff가 커지고 scaffold golden hash가 바뀐다.
- 필드 이름은 **고치는 파일의 기존 규칙을 따른다.** 새 파일은 소속 프로젝트의 규칙을 따른다.
  - 엔진·템플릿·샘플(`LibCommons`, `LibNetworks`, `template-projects`, `FastPortServer`, `FastPortClient`): 인스턴스 필드 `m_PascalCase`, static 필드 `s_PascalCase`, 상수 `C_PascalCase` 또는 `PascalCase`.
  - 도구·대시보드(`tests-projects/*`, `FastPortDashboard.*`): `_camelCase`.
  - 기존 이름을 한꺼번에 바꾸는 리팩터링은 요청이 있을 때만 한다.
- 타입·메서드·프로퍼티·이벤트는 PascalCase, 지역 변수·매개변수는 camelCase로 쓴다. 인터페이스는 `I` 접두어를 붙인다.
- nullable 경고를 `!`로 숨기지 않는다. 불변식으로 null이 아님이 보장될 때만 쓰고 바로 위에 이유 주석을 단다. `#pragma warning disable`도 같은 기준이다.
- 비동기 메서드는 `Task`/`ValueTask`를 반환하고 `async void`를 쓰지 않는다(이벤트 핸들러 제외). `ConfigureAwait(false)`는 고치는 파일의 기존 방식을 따른다(`TimerQueue.cs`는 쓰고 `BaseSession.cs`는 쓰지 않는다). 한 파일 안에서 섞지 않는다.
- `CancellationToken`을 받는 API는 끝까지 전달한다. 취소로 인한 `OperationCanceledException`은 오류 로그로 남기지 않는다.
- 핫 패스(수신·송신·accept 루프)에서는 할당을 늘리지 않는다. `ArrayPool<byte>.Shared`에서 빌린 버퍼는 반환 책임자를 한 곳으로 정하고, 반환 후 다시 쓰지 않는다. 핫 패스에 로그를 추가할 때는 `IsEnabled` 검사로 감싼다.
- 세션당 상태는 해당 세션의 worker Task/잠금 규칙 안에서만 바꾼다. 새 공유 상태를 만들면 어떤 스레드가 읽고 쓰는지 주석으로 적는다.
- 엔진(`LibNetworks`)은 텔레메트리·로깅 구현을 모른다. 관측이 필요하면 `protected virtual OnNetwork*` hook을 추가하고 구현은 앱(예: SmokeServer 세션)에서 override한다.
- 템플릿(`template-projects/FastPortGameServerTemplate`)은 `LibCommons`, `LibNetworks`만 참조한다. `Protocols`, `FastPortServer`, 테스트 프로젝트를 참조하지 않는다.
- 공개 API(엔진의 public/protected 멤버)를 바꾸면 템플릿·샘플·SmokeServer·Dashboard 사용처를 함께 고치고 빌드로 확인한다.

## Commenting Rule

- 코드 작성 또는 수정 시 멤버 변수, 멤버 함수, 주요 분기, 반복문, 상태 전이, 예외 처리, 비동기 흐름, 네트워크 I/O, 성능 관련 로직에는 가능한 한 `//` 한 줄 주석을 많이 작성한다.
- 주석은 코드 바로 위에 배치하고, 해당 코드가 왜 필요한지 또는 어떤 상태/불변식을 다루는지 한 줄로 설명한다.
- 주석 문체는 개조식으로 작성한다. `사용된다`, `처리한다`, `전달한다`처럼 서술형 종결 대신 `용도: ...`, `상태: ...`, `목적: ...`, `흐름: ...` 형태의 명사형/구문형 표현을 우선한다.
- 특히 C# class의 private/protected/public field, property, constructor, method에는 역할을 설명하는 `//` 주석을 적극적으로 추가한다.
- 복잡한 로직은 단계별로 `//` 주석을 나눠서 읽는 사람이 흐름을 따라갈 수 있게 한다.
- 주석은 실제 코드 동작과 일치해야 하며, 변경 시 코드와 함께 갱신한다.
- 의미 없는 반복 설명이나 코드와 모순되는 주석은 작성하지 않는다.

## 테스트 규칙

- 테스트 프레임워크는 MSTest다. 엔진·도구 테스트는 `tests-projects/FastPortTests`, 대시보드 Core 테스트는 `tests-projects/FastPortDashboardTests`에 둔다.
- 테스트 클래스 파일 이름은 대상 타입을 따른다(`<대상>Tests.cs`). 메서드 이름은 영어 `대상_상황_기대결과` 형식으로 쓴다(`BaseSession_InvalidPacketHeaderOnly_DisconnectsWithoutDelivery`).
- `FastPortTests`는 메서드 단위 병렬 실행이다(`MSTestSettings.cs`의 `Parallelize(Scope = ExecutionScope.MethodLevel)`). 고정 포트, 공유 static 상태, 실행 순서 의존을 만들지 않는다. 소켓 테스트는 loopback 포트 0과 기존 `SocketPair` 헬퍼 패턴을 쓴다.
- 비동기 대기는 고정 `Task.Delay` 대신 완료 신호(`TaskCompletionSource`)와 타임아웃으로 기다린다. 타이밍에 따라 흔들리는 단언을 만들지 않는다.
- 버그 수정은 먼저 실패하는 테스트로 재현하고, 수정 후 통과를 확인한다.
- 커밋 전 최소 확인: `dotnet build FastPortSharp.sln -c Release`와 변경 영역 테스트(`--filter`). 엔진을 바꿨으면 `dotnet test FastPortSharp.sln -c Release` 전체를 돌린다. 명령은 `docs/llm/platform.md`의 "빌드·테스트"에 있다.

## 변경 시 함께 해야 하는 일

- `LibCommons/**`, `LibNetworks/**`, `template-projects/FastPortGameServerTemplate/**`, `template-projects/Protos/**`를 고치면(주석만 바꿔도) scaffold golden hash를 갱신한다: `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체 통과를 확인한다.
- `scripts/scaffold-game-server.sh`와 `.ps1`은 같은 결과(바이트 단위)를 내야 한다. 한쪽을 고치면 다른 쪽도 고친다.
- `NetworkDisconnectReason` 값을 추가하면 SmokeServer 세션의 reason 문자열 매핑도 추가한다.
- `.github/workflows/*.yml`의 job `name`을 바꾸면 `main` ruleset 필수 체크 이름도 바꿔야 PR이 머지된다.

## Git / 커밋 메시지

- 작업 브랜치에서 개발하고 PR로 `main`에 머지한다. `main`은 브랜치 보호 대상이다(직접 push 금지, 필수 체크 `build (ubuntu-latest)`, `build (macos-latest)`, `build (windows-latest)`).
- 릴리스 브랜치 이름은 `builds/release`다(`builds.release` 아님). 승격은 `main` → `builds/release` PR로 한다.
- 커밋 author·committer는 `boinred <boinred@outlook.com>`이다.
- 형식은 `<type>(<scope>): <설명>`이다. 커밋 메시지와 PR 제목에 모두 쓴다.
  - type: `feat`(기능), `fix`(버그 수정), `perf`(성능), `refactor`(동작 변화 없는 구조 변경), `test`(테스트만), `docs`(문서만), `ci`(워크플로), `chore`(그 밖의 설정·정리).
  - scope는 선택이다. 변경이 한 영역에 속하면 넣는다: `buffers`, `packet`, `session`, `listener`, `connector`, `sample`, `template`, `scaffold`, `smoke-server`, `load-runner`, `load-validation`, `telemetry`, `dashboard`, `llm-docs`.
  - 설명은 영어 명령형 소문자로 시작하고 첫 줄은 72자 미만, 끝에 마침표를 찍지 않는다.
  - 예: `fix(session): guard receive path against malformed headers`, `docs(llm-docs): add session domain map`.
