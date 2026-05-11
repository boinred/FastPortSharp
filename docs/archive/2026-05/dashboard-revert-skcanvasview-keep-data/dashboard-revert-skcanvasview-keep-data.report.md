# dashboard-revert-skcanvasview-keep-data Completion Report

> **Project**: FastPortSharp · **Date**: 2026-05-11 · **Commit**: `667ca17` · **Match Rate**: 100% (structural), Critical SC-7 Not Met

## Executive Summary

| 관점 | Planned | Delivered |
|---|---|---|
| Problem | SKCanvasView가 macOS 26 SwiftUI crash trigger 가정 | ❌ 가설 반증 (crash는 baseline) |
| Solution | UI를 ChartView로 revert, data layer 보존 | ✅ 코드 revert 정확히 수행 |
| Function/UX/Effect | Debug crash 해결 + P95 chart 갱신 | ❌ Debug crash 그대로 |
| Core Value | Crash 해결 시도 + 데이터 진화 보존 | ⚠️ 데이터 보존만 달성 |

## Value Delivered

| Metric | Value |
|---|---|
| Plan SC met | 7/8 |
| Critical Not Met | 1 (SC-7 Debug crash 미해결) |
| Lesson 확정 | macOS 26 SwiftUI crash는 SKCanvasView/특정 UI lib 무관, baseline UIKit-on-Catalyst 문제 |
| Data layer 보존 | TimedRttPoint + 3-percentile collection 그대로 |
| Test 회귀 | 0 (20/0/0) |

## Key Decisions

| Decision | Outcome |
|---|---|
| UI revert only (Option A) | ✅ data layer 손실 0 |
| Plan 가설 (SKCanvasView = crash trigger) | ❌ 반증 |
| Memory `maccatalyst-26-swiftui-observation-release-crash` 정정 | ✅ Debug도 영향 / baseline 이슈로 업데이트 |

## Lessons Learned

1. **가설 검증의 가치**: 본 cycle은 "SKCanvasView가 crash 원인" 가설을 명시적으로 검증해 반증 — 정확한 원인 (baseline macOS 26 SwiftUI Observation framework)에 도달.
2. **"Debug 정상" 관찰의 한계**: 짧은 timeout 1회 관찰은 SwiftUI ViewGraph render 도달 전 종료될 수 있음. 신뢰성 ↓.
3. **Verification 경로 전환 필요**: UI 실행 검증이 막힌 환경에선 **headless E2E test**가 유일한 신뢰 verification 경로.

## Follow-up

- **즉시**: `dashboard-e2e-mock-tests` cycle (UI 없이 ViewModel + Adapter end-to-end 검증)
- **장기**: Microsoft `dotnet/macios` SR 업데이트 또는 macOS update 대기
