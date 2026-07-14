# Global Codex Instructions

## Language

- 기본 응답 언어는 한국어로 한다.
- 사용자가 다른 언어를 명시적으로 요청한 경우에만 해당 언어로 답한다.
- 코드, 명령어, 파일 경로, API 이름, 에러 메시지는 원문을 유지하고, 설명은 한국어로 작성한다.

## Coding Skill Rule

- For code writing, editing, refactoring, debugging, test work, and code review, use the `karpathy-guidelines` skill before starting implementation.
- Apply the skill with emphasis on explicit assumptions, simplicity first, surgical changes, verifiable success criteria, and verification.
- If the skill is not available in the current session, read `/Users/boinred/.codex/skills/karpathy-guidelines/SKILL.md` and follow those instructions as the fallback.

## PDCA Automation Rule

- `$pdca do {feature}`로 개발을 진행한 경우, 구현 완료 후 가능한 한 자동으로 `$pdca analyze {feature}`를 실행하여 design/code gap을 확인한다.
- 분석 결과 iterate가 필요한 경우, 사용자 추가 지시를 기다리지 말고 가능한 범위에서 `$pdca iterate {feature}` 흐름까지 자동 진행한다.
- iterate 이후에는 다시 analyze를 수행하고, match rate가 완료 기준에 도달하거나 더 이상 안전하게 자동 수정할 수 없을 때까지 반복한다.
- 완료 기준에 도달한 경우, 가능한 한 `$pdca report {feature}`까지 자동으로 작성하여 PDCA 흐름을 마무리한다.
- 자동 진행 중 테스트 실패, 설계 충돌, 위험한 변경, 사용자 결정이 필요한 범위가 발견되면 즉시 중단하고 현재 상태와 필요한 결정을 보고한다.

## Commenting Rule

- 코드 작성 또는 수정 시 멤버 변수, 멤버 함수, 주요 분기, 반복문, 상태 전이, 예외 처리, 비동기 흐름, 네트워크 I/O, 성능 관련 로직에는 가능한 한 `//` 한 줄 주석을 많이 작성한다.
- 주석은 코드 바로 위에 배치하고, 해당 코드가 왜 필요한지 또는 어떤 상태/불변식을 다루는지 한 줄로 설명한다.
- 주석 문체는 개조식으로 작성한다. `사용된다`, `처리한다`, `전달한다`처럼 서술형 종결 대신 `용도: ...`, `상태: ...`, `목적: ...`, `흐름: ...` 형태의 명사형/구문형 표현을 우선한다.
- 특히 C# class의 private/protected/public field, property, constructor, method에는 역할을 설명하는 `//` 주석을 적극적으로 추가한다.
- 복잡한 로직은 단계별로 `//` 주석을 나눠서 읽는 사람이 흐름을 따라갈 수 있게 한다.
- 주석은 실제 코드 동작과 일치해야 하며, 변경 시 코드와 함께 갱신한다.
- 의미 없는 반복 설명이나 코드와 모순되는 주석은 작성하지 않는다.
