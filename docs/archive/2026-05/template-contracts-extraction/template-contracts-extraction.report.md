# template-contracts-extraction Completion Report

> **Cycle**: template-contracts-extraction
> **Date**: 2026-05-12
> **Author**: das_young
> **Match Rate**: 95% (build + round-trip + diff-zero 검증 통과, 정식 gap-detector 미적용)
> **Commits**: `b3e4e2c`, `ff105ea`

---

## Executive Summary

### 1.3 Value Delivered

| Perspective | Planned | Delivered |
|-------------|---------|-----------|
| **Problem** | `FastPortGameServerTemplate` (Exe) 안에 proto + PacketIds 가둠 → SampleClient Exe→Exe ProjectReference 안티패턴, Dashboard 추가 차단 | ✅ 해소. Contracts (net10.0 Class Lib) 분리로 다중 소비자 재사용 가능. |
| **Solution** | `FastPortGameServerTemplate.Contracts` 신규 + `template-projects/` 그룹화 (2-Phase Pragmatic) | ✅ 2 commits, 13 files, 폴더 이동 3 + Contracts 신설 1. cs/proto byte diff = 0. |
| **Function/UX Effect** | echo round-trip 동작 동일, SampleClient bin 어셈블리 감소 | ✅ RTT 16.174ms (Phase 1) / 17.056ms (Phase 2), SampleClient bin에서 `FastPortGameServerTemplate.dll/exe` 부재. |
| **Core Value** | `dashboard-echo-client-tab` unblock + 안티패턴 해소 + `tests-projects/` ↔ `template-projects/` 대칭 | ✅ Dashboard Echo Client 탭은 Contracts ProjectReference 한 줄로 unblock. sln Solution Folder `template-projects` 자동 생성 (bonus). |

---

## Decision Record Chain

| Phase | Decision | Outcome |
|-------|----------|---------|
| Plan | Architecture Option A: Contracts lib 신설 (vs Compile Include Link / redefine in Dashboard) | ✅ 채택, 의도대로 leaf node 분리 |
| Plan | 폴더 그룹화: `template-projects/` (vs flat) | ✅ `tests-projects/` 대칭, `git mv` history 100% 보존 |
| Design | Execution Option C: 2-Phase Pragmatic (vs single atomic / per-module) | ✅ Phase 1 (folder) + Phase 2 (Contracts) 각각 build+round-trip 검증 통과 |
| Design | Namespace 보존 (`FastPortGameServerTemplate.Protocols/Handlers`) | ✅ downstream cs 파일 변경 0줄 |
| Design | `Google.Protobuf` Template 유지, `Grpc.Tools`만 Contracts로 | ✅ Template Application/Handlers의 `new EchoResponse {...}` 직접 인스턴스화 유지 |

---

## Success Criteria Final Status

| ID | Status | Evidence |
|----|:--:|----------|
| FR-01 Contracts net10.0 Class Lib sln 포함 + 단독 빌드 | ✅ | `dotnet build` 11,776 bytes dll |
| FR-02 `EchoRequest/Response` namespace 보존 | ✅ | proto `option csharp_namespace="FastPortGameServerTemplate.Protocols"` 무변경 |
| FR-03 `PacketIds.EchoRequest=1001/EchoResponse=1002` namespace 보존 | ✅ | `PacketIds.cs` 무변경 |
| FR-04 Template csproj `<Protobuf>` 제거 + Contracts 참조 | ✅ | csproj diff 확인 |
| FR-05 SampleClient Template 참조 제거 + Contracts 참조 | ✅ | csproj diff 확인 |
| FR-06 전체 sln `dotnet build` 성공 | ✅ | 0 error / 0 new warning |
| FR-07 Template + SampleClient echo round-trip 정상 | ✅ | RTT 16.174 / 17.056ms |
| FR-08 3개 프로젝트 모두 `template-projects/` 하위 | ✅ | filesystem 확인 |
| FR-09 scaffold-game-server `.sh` / `.ps1` dry-run | ✅ | exit 0 + TEMPLATE_SRC 새 경로 인식 |
| FR-10 workflow `paths:` filter 갱신 | ✅ | `.github/workflows/scaffold.yml:13,22` |

**Overall: 10/10 Met**

---

## Key Decisions & Outcomes

1. **2-Phase 분리는 위험 격리에 효과적**: Phase 1 후 build/round-trip 검증으로 폴더 이동만의 영향을 격리 → Phase 2 Contracts 추출 시 새 변수만 고려 가능했음
2. **Namespace 보존 정책**: cs 코드 0 변경으로 review 시 csproj/sln 영역에만 집중 가능
3. **`dotnet sln add` 보너스**: Contracts 등록 시 Solution Folder `template-projects` 자동 생성 (IDE 그룹화도 무상 획득)
4. **Known follow-up 사전 식별**: scaffold script + fixture는 본 cycle scope 외라고 Plan §2.2에서 명시 → 후속 `template-contracts-scaffold-fix` cycle로 완료

---

## Lessons

- **Pure refactor도 검증 매트릭스 필요**: build + round-trip + diff-zero 3종 셋트가 namespace 보존 검증의 핵심
- **Exe 프로젝트 ProjectReference 안티패턴 식별 가치**: SampleClient bin 청소 확인이 정량적 증거
- **`git mv` history 보존**: rename 표시 100%로 유지 — `git log --follow` 추적 가능
- **PDCA Plan §2.2 (out-of-scope) 명시의 가치**: scaffold regression을 사전 식별하여 follow-up cycle 깔끔히 분리
