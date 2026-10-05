# CLAUDE.md

공통 에이전트 규칙(언어, 코딩, 주석 규칙)은 `AGENTS.md`에 있고, 저장소 구조는 `docs/CODEMAP.md`에 있다.
Claude Code는 아래 import로 두 파일을 자동으로 읽는다. 규칙은 `AGENTS.md` 한 곳에서만 수정한다.

@AGENTS.md
@docs/CODEMAP.md

## Claude Code 전용 메모

### Git / PR
- 커밋 author·committer: `boinred <boinred@outlook.com>`. `Co-Authored-By: Claude` 트레일러는 넣지 않는다.
- `main`은 브랜치 보호(ruleset) 대상: 직접 push 금지, PR + 필수 체크 `build (ubuntu-latest)`, `build (macos-latest)`, `build (windows-latest)` 통과 후 merge commit으로 머지.
- 릴리스 브랜치 이름은 `builds/release` (점 `.`이 아니라 슬래시 `/`). 승격은 `main` → `builds/release` PR.

### 변경 시 함께 해야 하는 일
- `LibCommons/**`, `LibNetworks/**`, `template-projects/FastPortGameServerTemplate/**`, `template-projects/Protos/**` 수정 → scaffold golden hash 갱신 필수:
  `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체 통과 확인.
- `.github/workflows/*.yml`의 job `name` 변경 → main ruleset 필수 체크 이름도 함께 바꿔야 PR이 머지됨.
