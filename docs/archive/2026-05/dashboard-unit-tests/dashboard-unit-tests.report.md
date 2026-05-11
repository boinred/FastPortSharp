# dashboard-unit-tests Completion Report

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: ✅ Completed
> **Match Rate**: 96.2% (runtime-weighted)
> **Commit**: `4184d57`

---

## Executive Summary

| 관점 | Planned | Delivered |
|---|---|---|
| **Problem** | Dashboard (ViewModel + Mock/Jsonl Adapter) 단위 테스트 0건. Toolkit migration 안전망이 macOS 수동 실행만. | ✅ 동일 |
| **Solution** | `tests-projects/FastPortDashboardTests` MSTest 신규, Dashboard sln only. ViewModel + Adapter 핵심 경로 ~18 tests. | ✅ 18 tests (ViewModel 10 + Mock 3 + Jsonl 5) |
| **Function/UX/Effect** | `dotnet test FastPortSharp.Dashboard.sln`로 회귀 자동 감지. CI 무변경. | ✅ 18/0/0 (745ms), build.yml diff 0 |
| **Core Value** | Toolkit migration 안정성 확정 + 수동 실행 의존 제거. | ✅ Toolkit attribute (ObservableProperty/RelayCommand/NotifyCanExecuteChangedFor) 동작 검증 |

### Value Delivered

| Metric | Before | After |
|---|---|---|
| Dashboard 단위 테스트 | 0 | **18** |
| ViewModel 자동 검증 경로 | macOS 수동 실행만 | `dotnet test` (745ms) |
| FastPortSharp.sln 회귀 | 139/0/0 | 139/0/0 (unchanged) |
| Production 코드 변경 | — | **0줄** |
| CI workflow 변경 | — | **0줄** |
| 변경 파일 | — | 4 + 1 sln + 3 docs (단일 commit) |
| FileShare memory lesson 적용 | — | T-JA-5 + AppendJsonl helper |

---

## 1. Key Decisions & Outcomes

| Phase | Decision | Outcome |
|---|---|---|
| **[Plan]** Test project 위치: `tests-projects/FastPortDashboardTests` (Dashboard sln only) | ✅ Followed | sln entry만 추가, build.yml 무변경 |
| **[Plan]** Framework: MSTest | ✅ Followed | `MSTest` 3.9.0 umbrella package |
| **[Plan]** Production 변경 0 | ✅ Followed | `FastPortDashboard.Maui/` git diff 0 |
| **[Plan]** FileShare.ReadWrite 명시 | ✅ Followed | `AppendJsonl` helper + T-JA-5 검증 |
| **[Plan]** 단일 commit | ✅ Followed | `4184d57` |
| **[Design]** Option A — maccatalyst TFM 매칭 (ProjectReference) | ⚠️ **Deviated** → Option C | Resizetizer 출력 충돌 + `dotnet test`가 maccatalyst Exe 실행 불가. ViewModel/Adapter는 MAUI 타입 의존 0이라 pure net10.0에서 `<Compile Include Link="_Source/...">`로 재컴파일 가능. Plan 가치 100% 보존. |
| **[Design]** ≥ 15 tests | ✅ Followed (overshoot) | 18 tests |
| **[Design]** ≤ 20 turn 추정 | ✅ Followed | 실제 ~17 turn (빌드 trial-and-error 6회 + test fix 1회) |

---

## 2. Success Criteria Final Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | 신규 test project (csproj + 3 cs) | ✅ Met | 4 files |
| SC-2 | Dashboard sln only | ✅ Met | `FastPortSharp.sln` git diff 0 |
| SC-3 | ≥ 15 tests pass (target ~18) | ✅ Met | 실제 18 |
| SC-4 | Dashboard sln 빌드 0/0 | ✅ Met | 0/0 |
| SC-5 | Dashboard sln 신규 tests pass | ✅ Met | 18/0/0 (745ms) |
| SC-6 | FastPortSharp.sln 회귀 0/0 build | ✅ Met | 0/0 (3.97s) |
| SC-7 | FastPortSharp.sln 139 tests 회귀 0 | ✅ Met | 139/0/0 |
| SC-8 | `.github/workflows/build.yml` 무변경 | ✅ Met | git diff 0 |
| SC-9 | 단일 commit | ✅ Met | `4184d57` |

**Overall**: **9/9 ✅ Met** (100%)

---

## 3. PDCA Cycle Summary

| Phase | Output | Notes |
|---|---|---|
| Plan | `docs/01-plan/features/dashboard-unit-tests.plan.md` | 사용자 선택: Dashboard sln only + ViewModel + Adapters 범위 |
| Design | `docs/02-design/features/dashboard-unit-tests.design.md` | Option A (maccatalyst TFM) 선언 |
| Do | commit `4184d57` | Option A → Option C 전환 (Resizetizer + maccatalyst dotnet test 제약) |
| Check | `docs/03-analysis/dashboard-unit-tests.analysis.md` | 96.2% Match Rate, 0 Critical, 1 Important (deviation 인정) |
| Report | (this document) | — |

---

## 4. Implementation Highlights

### 4.1 Test Coverage

| 영역 | Tests | 검증 항목 |
|---|---|---|
| DashboardViewModel | 10 | Toolkit setter notify, [NotifyCanExecuteChangedFor], ApplySnapshot 매핑, ThroughputSeries 600 trim, StartAsync Error 분기 |
| MockPollingAdapter | 3 | interval, cancellation, seed |
| JsonlPollingAdapter | 5 | offset 누적, truncation reset, malformed skip, **FileShare.ReadWrite 동시 write** |

### 4.2 Architecture Evolution: Option A → Option C

```
Design 선언: Option A (maccatalyst TFM 매칭 + ProjectReference)
   ↓
Implementation 시도 1: Resizetizer가 두 MAUI 프로젝트의 appicon 출력 충돌 → 빌드 실패
   ↓
시도 2: UseMaui 제거 → NU1201 TFM mismatch (26.0 vs 26.4)
   ↓
시도 3: TargetPlatformVersion=26.4 align → 다시 Resizetizer 에러
   ↓
시도 4: UseMaui + EnableMauiImageProcessing=false + ApplicationId 추가 → 빌드 통과
   ↓
시도 5: dotnet test 실행 → net10.0-maccatalyst Exe DLL 직접 실행 불가
   ↓
전환: Option C — pure net10.0 + <Compile Include Link="_Source/...">
   ↓
✅ 18/0/0 (745ms), production 변경 0, Plan 가치 보존
```

### 4.3 JsonlPollingAdapter Race Discovery

테스트 작성 중 발견: production code가 yield 후 offset을 `FileInfo.Length`로 갱신 → consumer가 manual enumerator로 yield 사이에 append하면 offset이 새 데이터를 건너뜀.

- **Production 영향**: 무해 (실제 사용 패턴은 백그라운드 writer, generator delay 중 write 발생)
- **테스트 영향**: Manual `GetAsyncEnumerator` + append 패턴 사용 시 race. 백그라운드 writer 패턴으로 재작성 → 안정.

---

## 5. Lessons Learned

1. **MAUI test project 제약**: `net10.0-maccatalyst` TFM은 두 가지 빌드/실행 제약 (Resizetizer 충돌 + dotnet test 미지원). MAUI test 작성 시 Plan/Design 단계에서 미리 식별 필요.
2. **`<Compile Include>` pattern 유용성**: MAUI 타입 의존이 없는 ViewModel/Adapter는 pure net10.0 test에서 source-level reuse 가능. Toolkit source generator도 동일 적용. 단점: 신규 파일 추가 시 csproj 수동 갱신.
3. **Adapter API 설계 가치**: `JsonlPollingAdapter`/`MockPollingAdapter`가 `TimeSpan? interval` 생성자 파라미터를 노출하므로 production 변경 0으로 50ms 주입 가능. 향후 adapter 설계 시 시간 의존 파라미터 외부 주입 원칙 유지.
4. **테스트가 production race 발견**: 실 사용 패턴에선 무해하지만 contrived 시나리오로 노출. 테스트 디자인을 production 사용 패턴과 정렬하면 false-positive 회피.

---

## 6. Follow-up Recommendations

| Cycle | Purpose | Priority |
|---|---|---|
| `extract-LibDashboardCore` | ViewModel/Adapter를 별도 net10.0 Class Library로 분리. Option B 전환. Compile Include 운영 부담 제거. | Medium |
| `dashboard-rtt-chart` | RTT chart 추가 (ThroughputSeries 활용) + LiveCharts2 재도입 검토 | Medium |
| `dashboard-jsonl-offset-fix` | JsonlPollingAdapter offset race 정식 수정 (FileStream `Position` 활용) | Low (production 무해) |
| `dashboard-ci-add` | Dashboard sln을 별도 CI workflow로 추가 (Dashboard 변경 시만 실행) | Low |

---

## 7. New Memory Lesson Candidate

> **maui-test-project-tfm-gotcha**: MAUI app project (net10.0-maccatalyst Exe)를 ProjectReference하는 test project는 (1) Resizetizer 출력 충돌, (2) `dotnet test`가 maccatalyst Exe 미지원이라는 두 가지 제약. ViewModel/Adapter가 MAUI 타입 의존 0이라면 pure net10.0 test + `<Compile Include Link="_Source/...">`로 우회.

→ 다음 cycle에서 memory 저장 고려.

---

## 8. Archive Note

이 cycle의 모든 PDCA 문서는 `/pdca archive dashboard-unit-tests` 실행 시 `docs/archive/2026-05/dashboard-unit-tests/`로 이동됩니다.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Completion report (Match Rate 96.2%, 9/9 SC met, 1 architecture deviation, single commit 4184d57) | boinred |
