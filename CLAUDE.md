# CLAUDE.md

공통 에이전트 규칙(언어, 코드 탐색, 문서 유지, C# 스타일, 주석, 테스트, 커밋)은 `AGENTS.md`에 있고, 코드 지도는 `docs/llm/`에 있다.
Claude Code는 아래 import로 `AGENTS.md`만 자동으로 읽는다. 코드 지도(`docs/llm/README.md`)는 매 세션 컨텍스트를 아끼려고 import하지 않고, `AGENTS.md`의 "코드 탐색 규칙"에 따라 코드 탐색 전에 직접 읽는다. 규칙은 `AGENTS.md` 한 곳에서만 수정한다.

@AGENTS.md

## Claude Code 전용 메모

- 커밋에 `Co-Authored-By: Claude` 트레일러를 넣지 않는다.
- 프로젝트 설정 `.claude/settings.json`이 `superpowers@claude-plugins-official` 플러그인을 켠다. 스킬 사용 방식은 `AGENTS.md`의 "개발 워크플로 (superpowers)"를 따르고, 스킬 절차와 `AGENTS.md`가 충돌하면 `AGENTS.md`를 따른다.
