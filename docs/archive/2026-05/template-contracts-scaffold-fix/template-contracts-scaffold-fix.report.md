# template-contracts-scaffold-fix Completion Report

> **Cycle**: template-contracts-scaffold-fix
> **Predecessor**: template-contracts-extraction (`b3e4e2c`, `ff105ea`)
> **Date**: 2026-05-12
> **Author**: das_young
> **Match Rate**: 100% (sh 7/7 + ps1 7/7 + dotnet build 0/0 + cross-script identical)
> **Commit**: `241cef2`

---

## Executive Summary

### 1.3 Value Delivered

| Perspective | Planned | Delivered |
|-------------|---------|-----------|
| **Problem** | 직전 cycle이 Contracts lib 도입하면서 scaffold script 미갱신 → 외부 사용자 scaffold 출력 컴파일 불가, CI scaffold workflow PR push 시 fail 예정 | ✅ 완전 해소. scaffold 출력이 4-project buildable 상태 회복. |
| **Solution** | sh/ps1 양쪽에 `CONTRACTS_SRC` + `copy_contracts` + `replace_tokens` subtree 확장 + sln 4-project. fixture 4개 case 갱신 (case-01 auto, case-05/07 manual) | ✅ Option C (Pragmatic) 채택. 단일 atomic commit, sh +35 lines / ps1 +40 lines / fixture 4 case. |
| **Function/UX Effect** | step counter [1/12]~[12/12] 보존, cross-OS byte-identical 유지 | ✅ 모든 기존 fixture stdout-contains 어서션 통과. sh + ps1 동일 결과 (7/7 PASS 양쪽). |
| **Core Value** | regression 폐쇄 + CI scaffold matrix PASS 회복 + push 전 깨끗한 작업 상태 | ✅ push 완료 (`241cef2`). 추가로 source/scaffold output depth 불일치 path-fix (`..\..\Lib...` → `..\Lib...`) 발견 + 처리. |

---

## Decision Record Chain

| Phase | Decision | Outcome |
|-------|----------|---------|
| Plan | scope: scaffold script + 4 fixture case (case-01 auto, 05/07 manual, 02/03/04/06 unchanged) | ✅ 의도대로 4 case만 갱신, 나머지 3 case fixture 영향 0 확인 |
| Plan | step counter [1/12]~[12/12] 유지 (vs 14 확장) | ✅ 기존 stdout-contains fixture 4개 case 깨지지 않음 |
| Design | Architecture Option C: 별도 `copy_contracts()` 함수 + 기존 패턴 답습 (vs minimal inline / clean array refactor) | ✅ sh/ps1 대칭 명확 + byte-identical 유지 |
| Design | Token subtree 처리: Template/Contracts 각각 별도 find (vs 통합) | ✅ LibCommons/LibNetworks 침투 0 검증 |
| Design | Commit 전략: 1 atomic commit (vs 3 분할) | ✅ script ↔ fixture 동기 보장, review 단순성 |
| **Do (계획 외 발견)** | **path depth 자동 조정 (`..\..\LibCommons` → `..\LibCommons`)** | ✅ scaffold 출력 flat 구조에서 manual build 0/0 성공 |

---

## Success Criteria Final Status

| ID | Status | Evidence |
|----|:--:|----------|
| FR-01 sh Contracts dir 생성 | ✅ | scaffold-real-test/TestFoo.Contracts/ 확인 |
| FR-02 ps1 동등 출력 | ✅ | scaffold-ps-test 동일 구조 |
| FR-03 sln 4 projects | ✅ | grep Project sln → 4 entries |
| FR-04 Token replace Contracts subtree 처리 | ✅ | Sample.proto namespace + PacketIds.cs namespace 치환 확인 |
| FR-05 `dotnet build <DEST>/<NewName>.sln` 성공 | ✅ | 0 error / 0 warning (Debug + scaffold 내부 [12/12] Release) |
| FR-06 `tests/scaffold/run.sh` 7 case PASS | ✅ | sh 7/7 + ps1 7/7 |
| FR-07 step counter [1/12]~[12/12] 보존 | ✅ | stdout-contains case 모두 통과 |
| FR-08 case-01 sha256 + tree + files-present 갱신 | ✅ | `--update-golden` auto + manual edit |
| FR-09 case-05/07 files-present +3 lines | ✅ | manual edit |

**Overall: 9/9 Met**

---

## Key Decisions & Outcomes

1. **Option C (Pragmatic) 효율**: 별도 함수 추가 + 기존 패턴 답습이 ~35 lines (sh) / 40 lines (ps1)로 끝남. Clean array refactor (~80 lines)보다 byte-identical 위험 낮음
2. **계획 외 발견의 처리**: source가 `template-projects/` 깊이 2로 이동했지만 scaffold output flat 구조는 변경 없음 → csproj relative path 불일치 자동 검출. Do 단계에서 즉시 path-fix 추가 (sed 양쪽 + PowerShell Replace)
3. **`--update-golden` 자동화 활용**: case-01의 sha256.txt + tree.txt 재생성은 1 명령으로 처리. 7 case 중 4 case만 영향 받았음 (case-02/03/04/06 영향 0)
4. **사전 분석 정확성**: Plan §2.2에서 "case-06은 generic phrase only → 영향 없음 가능성 높음"이 실제 검증에서 그대로 확인됨

---

## Lessons

- **Refactor에는 검증 매트릭스가 필수**: scaffold 영역에서 build (smoke) + golden file diff + cross-script (sh/ps1) + cross-fixture (case-01~07)의 4축 검증이 모두 통과해야 안심
- **계획 외 발견을 즉시 흡수**: path depth 불일치는 Plan에 없었지만 빌드 검증 단계(M6)에서 즉시 발견 + 처리. 별도 follow-up cycle로 미루지 않음
- **atomic commit의 가치**: scaffold script ↔ fixture는 서로 의존 — 분리 시 중간 commit이 깨지므로 한 묶음이 자연스러움
- **regression 폐쇄 cycle 패턴**: 직전 cycle이 도입한 known-issue를 follow-up cycle로 폐쇄하는 PDCA 워크플로 검증
- **Cross-OS byte-identical 정책 보존**: HANDOFF.md:282의 정책을 [1/12]~[12/12] step counter 유지 + sh/ps1 1:1 mirror로 자연스럽게 충족
