# Agent Instructions (Codex / Claude Code 공통)

## Language

- 기본 응답 언어는 한국어로 한다.
- 사용자가 다른 언어를 명시적으로 요청한 경우에만 해당 언어로 답한다.
- 코드, 명령어, 파일 경로, API 이름, 에러 메시지는 원문을 유지하고, 설명은 한국어로 작성한다.

## Coding Skill Rule

- For code writing, editing, refactoring, debugging, test work, and code review, use the `karpathy-guidelines` skill before starting implementation.
- Apply the skill with emphasis on explicit assumptions, simplicity first, surgical changes, verifiable success criteria, and verification.
- If the skill is not available in the current session, read `/Users/boinred/.codex/skills/karpathy-guidelines/SKILL.md` and follow those instructions as the fallback.

## Code Map Rule

- 코드 탐색 전에 `docs/CODEMAP.md`를 먼저 읽고, 작업에 필요한 파일만 열어 토큰을 절약한다.
- 프로젝트·디렉터리 추가/삭제, 공개 타입 이동, 빌드·테스트 명령 변경처럼 코드맵 내용이 달라지는 변경을 하면 같은 커밋에서 `docs/CODEMAP.md`도 갱신한다.

## Commenting Rule

- 코드 작성 또는 수정 시 멤버 변수, 멤버 함수, 주요 분기, 반복문, 상태 전이, 예외 처리, 비동기 흐름, 네트워크 I/O, 성능 관련 로직에는 가능한 한 `//` 한 줄 주석을 많이 작성한다.
- 주석은 코드 바로 위에 배치하고, 해당 코드가 왜 필요한지 또는 어떤 상태/불변식을 다루는지 한 줄로 설명한다.
- 주석 문체는 개조식으로 작성한다. `사용된다`, `처리한다`, `전달한다`처럼 서술형 종결 대신 `용도: ...`, `상태: ...`, `목적: ...`, `흐름: ...` 형태의 명사형/구문형 표현을 우선한다.
- 특히 C# class의 private/protected/public field, property, constructor, method에는 역할을 설명하는 `//` 주석을 적극적으로 추가한다.
- 복잡한 로직은 단계별로 `//` 주석을 나눠서 읽는 사람이 흐름을 따라갈 수 있게 한다.
- 주석은 실제 코드 동작과 일치해야 하며, 변경 시 코드와 함께 갱신한다.
- 의미 없는 반복 설명이나 코드와 모순되는 주석은 작성하지 않는다.
