# maui-telemetry-dashboard-foundation PRD

> **Lightweight PRD** (foundation cycle for MAUI Dashboard track —
> follows the same compact PRD shape as recent fix cycles).
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Trigger**: HANDOFF Roadmap §4 — MAUI Dashboard candidate
> `maui-telemetry-dashboard-foundation`. Server starter cycle (이번
> cycle 직전 4건) 완료 후 telemetry 시각화 surface 신설.

---

## 1. Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | FastPortServer / FastPortGameServerTemplate / FastPortTestSmokeServer 가 `ObservedMetricsSnapshot` JSONL을 출력하지만, 사람이 실시간으로 볼 수 있는 GUI가 없음. CLI/log tail 또는 별도 spreadsheet 분석으로 비효율. |
| **Solution** | .NET MAUI desktop app `FastPortDashboard.Maui` (macOS Catalyst + Windows desktop) — 기존 `LibTestTelemetry.ObservedMetricsSnapshot` 컨트랙트를 그대로 소비하고, JSONL 파일을 tail해서 실시간 chart 1-2개로 시각화. |
| **Function/UX/Effect** | Server 띄우고 → metrics file 경로 지정 → 곧바로 1초 단위 TPS/RTT P95 chart. 첫 사용자 onboarding ~30초. |
| **Core Value** | 실시간 가시성 확보 → load validation 분석/디버그 시간 ↓. 향후 cycle (run viewer / report export)의 토대. |

---

## 2. Problem Statement

### 2.1 Current Pain

- `FastPortTestLoadValidation`을 돌리면 `artifacts/load-validation/<run>/server.metrics.jsonl` 등 JSONL 누적
- 사람이 보려면 `tail -f | jq` 또는 별도 스크립트 작성 필요
- 분산이 큰 RTT P95 / pacing window 같은 지표는 시각적 차트 없이 의미 파악 어려움
- 여러 run을 비교하려면 spreadsheet에 수동 import

### 2.2 Why MAUI

- 기존 솔루션이 .NET 10 통일 → MAUI 도입 비용 ↓
- macOS (M-series) + Windows desktop 동시 지원
- iOS/Android 확장 옵션 (out-of-scope, 다음 cycle)
- C# 그대로 재사용 → `LibTestTelemetry` 컨트랙트 직접 참조 가능

---

## 3. Scope

### 3.1 In Scope (Foundation)

- 신규 프로젝트 `FastPortDashboard.Maui` (또는 `FastPortMauiDashboard`)
- TargetFramework: `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`
- **메인 화면 1개**:
  - 파일 picker (또는 textbox + browse) — `server.metrics.jsonl` path 지정
  - "Connect" 버튼 → polling 시작
  - 실시간 chart 1-2개:
    - TPS (Total send/recv per second)
    - RTT P95 (server-observed 또는 client-observed)
  - Numeric KPI 4-6개 (현재 sessions, total bytes, sentPackets, recvPackets 등)
- File polling 메커니즘 (직전 cycle의 `WaitForFileWithLinesAsync` 패턴 참고; FileShare.ReadWrite로 producer 동시 동작 허용)
- Mock data 모드 (실제 server 없이 sample JSONL로 UI 검증)

### 3.2 Out of Scope (다음 cycle 후보)

- `client.metrics.jsonl` + dual-pane 비교 (별도 cycle)
- 여러 run side-by-side 비교
- PDF/HTML report export
- iOS / Android 빌드
- 실시간 control (서버에 명령 전송)
- 인증 / multi-user

---

## 4. Constraints

- 기존 `LibTestTelemetry.ObservedMetricsSnapshot` 컨트랙트 그대로 사용. 새 데이터 모델 도입 금지.
- `dotnet build FastPortSharp.sln -c Release` 0 warning / 0 error 유지 (단, MAUI workload 미설치 환경에서는 dashboard 빌드만 skip 허용 — 별도 sln 구성 또는 conditional build).
- 139 기존 테스트 회귀 0.
- macOS Catalyst + Windows desktop 빌드 둘 다 통과 (CI matrix는 별도 cycle).

---

## 5. Success Criteria

- [ ] `FastPortDashboard.Maui` 신규 프로젝트가 macOS Catalyst에서 빌드 통과
- [ ] Sample JSONL (artifacts에서 1개 골라서) 지정 시 chart 1-2개가 1초 단위로 갱신
- [ ] 실제 `FastPortTestSmokeServer` 띄우고 metrics file 지정 시 live update
- [ ] 기존 139 tests 회귀 0
- [ ] dashboard 자체 unit test (chart data adapter 등) 1-2개
- [ ] README 또는 docs/에 사용법 1 페이지 (실행 명령 + 스크린샷 1장)

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| (R-1) MAUI workload 설치 부담 (대용량 SDK) | dashboard project를 main sln에 포함하지 않거나 별도 sln (FastPortSharp.Dashboard.sln) 구성. CI는 별도 workflow. |
| (R-2) macOS Catalyst chart library 호환성 | `.NET MAUI Community Toolkit` 또는 `LiveCharts2` 같은 검증된 라이브러리 사용. Foundation에서는 가장 단순한 것 1개 선택. |
| (R-3) JSONL polling이 직전 cycle의 windows file share gotcha 재발 | `FileShare.ReadWrite` 명시 (메모리 저장된 lesson 적용). |
| (R-4) 첫 MAUI 프로젝트라 학습 비용 | foundation scope 협소화. 단일 view + mock data로 시작. |

---

## 7. Stakeholders & Personas

| Persona | Pain Point | Foundation Outcome |
|---|---|---|
| boinred (load validation 분석) | tail/jq 노이즈 ↑, 시각화 ↓ | 실시간 chart로 즉시 추세 파악 |
| 미래 contributor / AI agent | 새 telemetry 지표 추가 시 dashboard 자동 반영 어려움 | 컨트랙트 단일 소스 (`ObservedMetricsSnapshot`) |
| MAUI/UI 학습자 (보너스) | FastPortSharp 도메인 지식 + MAUI 결합 예제 | foundation이 그 자체로 working sample |

---

## 8. Beachhead / GTM

해당 없음 — 내부 도구.

---

## 9. Next Steps

1. `/pdca plan maui-telemetry-dashboard-foundation`
   - sln 통합 vs 분리 결정
   - chart 라이브러리 후보 (LiveCharts2, Microsoft.Maui.Graphics 등) 비교
   - polling 빈도, mock data 형식 정함
   - Success Criteria 인계
2. `/pdca design ...` — 3 architecture options
3. Do (다중 세션 가능: project skeleton / data adapter / view / polish)
4. Check + Report + Archive

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 1.0 | 2026-05-10 | Initial lightweight PRD (foundation scope, JSONL polling, macOS+Win) | boinred |
