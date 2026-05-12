# protos-shared-folder-revert-contracts Completion Report

> **Cycle**: protos-shared-folder-revert-contracts
> **Predecessors**: `template-contracts-extraction` (archived), `template-contracts-scaffold-fix` (archived)
> **Date**: 2026-05-12
> **Author**: das_young
> **Match Rate**: 100% (build + tests + scaffold + round-trip 모두 통과)
> **Commits**: `e59ec3b` (Phase 1), `b3cd4e2` (Phase 2)

---

## Executive Summary

### 1.3 Value Delivered

| Perspective | Planned | Delivered |
|-------------|---------|-----------|
| **Problem** | Contracts Class Library는 game proto sharing 단위로 과도. scaffold 출력 4 projects 복잡 | ✅ 해소. Contracts 제거, Protos 단순 폴더 |
| **Solution** | `template-projects/Protos/` (csproj 없는 단순 폴더) + 각 consumer 자체 `<Protobuf Include>` + PacketIds `<Compile Link>` | ✅ 2 commits, 13+ files, sln 13→12 projects |
| **Function/UX Effect** | scaffold 출력 4→3 projects + Protos 폴더 (LibCommons 패턴) | ✅ scaffold suite sh 7/7 + ps1 7/7 PASS, scaffold output build 0/0 |
| **Core Value** | game contract=data 인식 + scaffold 단순화 + 향후 consumer 추가 시 `<Protobuf>` 한 줄 통합 | ✅ Dashboard cycle은 ProjectReference 의존 0으로 진행 가능 |

---

## Decision Record Chain

| Phase | Decision | Outcome |
|-------|----------|---------|
| Plan | proto 위치: `template-projects/Protos/` 신규 폴더 (vs engine `Protocols/`) | ✅ engine 영역과 분리 유지 |
| Plan | PacketIds.cs 소유: Template + SampleClient `<Compile Link>` (vs duplicate / proto enum / thin lib) | ✅ Exe→Exe 안티패턴 회피 + 단일 source of truth |
| Design | Execution Option B: 2-Phase (source / scaffold+docs) (vs atomic / 3-phase) | ✅ 직전 패턴 답습, 각 phase 검증 끼움 |
| Design | scaffold Protos copy: LibCommons 패턴 (verbatim 위치) + .proto 파일 내용은 token replace (csharp_namespace) | ✅ 정확히 작동, `<Dest>/Protos/` 위치 + `<NewName>.Protocols` namespace |
| Design | path depth 조정 (`..\..\Lib*` → `..\Lib*`): Template csproj만 (Contracts 없음) | ✅ scaffold output flat에서 build 정상 |

---

## Success Criteria Final Status

| ID | Status | Evidence |
|----|:--:|----------|
| FR-01 Contracts/ 디렉터리 + csproj 제거 | ✅ | `find . -name "Contracts*"` 빈 출력 |
| FR-02 Protos/Sample.proto, csproj 없음 | ✅ | `template-projects/Protos/` 폴더 (csproj 부재) |
| FR-03 PacketIds.cs Template 소유 복귀 | ✅ | `template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs` |
| FR-04 Template csproj `<Protobuf Include>` + Grpc.Tools | ✅ | csproj 확인 |
| FR-05 SampleClient csproj `<Protobuf>` + `<Compile Link>` | ✅ | csproj 확인 |
| FR-06 SampleClient↔Template ProjectReference 없음 | ✅ | bin/Debug/net10.0/에 Template.dll/exe 부재 |
| FR-07 FastPortSharp.sln 12 projects | ✅ | `dotnet sln list` |
| FR-08 scaffold sh/ps1 Contracts 사라짐, Protos 등장 | ✅ | dry-run + real scaffold |
| FR-09 scaffold sln 3 projects | ✅ | `grep Project /tmp/scaffold-real/Foo.sln` |
| FR-10 scaffold csproj `<Protobuf Include="..\Protos\*.proto"/>` | ✅ | 생성된 csproj |
| FR-11 scaffold output `dotnet build` | ✅ | scaffold 내부 [12/12] smoke + manual build 0/0 |
| FR-12 run.sh sh+ps1 7/7 | ✅ | 양쪽 PASS |

**Overall: 12/12 Met**

---

## Phase별 검증 결과

### Phase 1 (commit `e59ec3b`)
- `dotnet build FastPortSharp.sln`: 0 error / 0 warning
- `dotnet test`: FastPortTests 139 PASS + FastPortDashboardTests 37 PASS = **176/176**
- Echo round-trip: **RTT=15.406ms** (이전 cycle 15.496ms 와 동등)
- SampleClient bin 안티패턴: Template.dll/exe **부재** ✓
- Contracts.dll repo 전체에서 **부재** ✓

### Phase 2 (commit `b3cd4e2`)
- scaffold sh suite: **7/7 PASS**
- scaffold ps1 suite: **7/7 PASS** (cross-script byte-identical 유지)
- scaffold output `dotnet build`: 0/0 (smoke + manual)
- Final Release build: 0/0
- Final tests: 139 + 37 = 176/176

---

## Key Decisions & Outcomes

1. **2-Phase split의 효과**: Phase 1 source-only commit + Phase 2 scaffold/docs commit 분리로 직전 cycle 안 패턴 활용. 각 phase별 검증 끼움으로 위험 격리.
2. **scaffold Protos 처리의 변형 패턴**: LibCommons처럼 verbatim 위치(폴더 이름 변경 안 함) + .proto 파일 내용은 token replace 대상에 포함. LibCommons와는 다른 새 패턴 정립.
3. **path depth 조정 단순화**: 직전 cycle (`template-contracts-scaffold-fix`)에서 도입한 `..\..\` → `..\` sed/Replace 로직, Contracts 제거로 Template csproj만 대상이 되어 더 단순해짐.
4. **fixture re-update 함정**: M10 docs 수정 후 case-01 sha256 재캡처가 필요했음 — Phase 2 commit 후 detect, amend로 해결.

---

## Lessons

- **다단계 design pivot 가능**: archived cycle을 revert하지 않고 새 cycle로 design pivot — git history는 완전히 보존하면서 코드만 새 방향으로 진화
- **Per-consumer protobuf 생성 패턴**: .NET 생태계 표준이며, 각 어셈블리가 자체 generated cs를 가져도 wire 호환성은 유지됨 (TCP 통신은 byte 수준)
- **`<Compile Include Link>` 패턴 검증**: source-level sharing으로 Exe→Exe ProjectReference 안티패턴 완벽 회피
- **Documentation regen은 fixture 변경에 영향 미침**: README/QUICKSTART가 token-replace 대상이라 sha256.txt에 hash 포함됨 → 문서 수정 후 항상 `--update-golden` 다시 돌리거나, 또는 docs 변경을 fixture regen 전에 완료
- **Predecessor cycle history 가치**: 두 archived cycle을 거치며 학습한 점 (안티패턴 인식, path depth fix, Protos token replace 필요성)이 본 cycle 빠른 진행을 가능케 함
