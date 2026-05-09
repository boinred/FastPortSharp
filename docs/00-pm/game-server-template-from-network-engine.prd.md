# PRD: Game Server Template from Network Engine

> Feature: `game-server-template-from-network-engine`
> 작성일: 2026-05-09
> 작성: PM Agent Team (pm-lead orchestration)
> 상태: Draft v1
> 다음 단계: `/pdca plan game-server-template-from-network-engine`

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | C# 게임 서버를 처음부터 만드는 인디/스튜디오는 TCP accept/session/packet 송수신 같은 저수준 네트워크 코드를 매번 다시 작성하거나 검증되지 않은 라이브러리를 조합한다. FastPortSharp는 이미 `LibCommons` + `LibNetworks` + `FastPortServer` 형태로 검증된 엔진/서버 구조를 갖고 있지만, 외부(또는 내부 신규 프로젝트)가 재사용할 수 있는 "엔진 패키지 + 게임 서버 스타터 템플릿" 경계가 없다. |
| **Solution** | `LibCommons` + `LibNetworks`를 **재사용 가능한 네트워크 엔진**으로 패키지화하고, `FastPortServer`를 **게임 서버 스타터 템플릿**으로 분리한다. 엔진과 템플릿은 *별도 릴리스 케이던스*를 갖는다. v1은 TCP accept/session/packet send-receive + 패킷 스키마/프로토콜 생성기까지로 한정한다. |
| **Function/UX Effect** | (1) 본인/내부 팀이 새 게임 서버 프로젝트를 시작할 때 5분 내 부트스트랩. (2) C# 인디/스튜디오가 "검증된 .NET 10 TCP 엔진"을 NuGet 또는 Template로 받아 게임 로직만 작성. (3) 엔진 개선이 템플릿에 자동 전파되거나 명시적 버전으로 고정. |
| **Core Value** | "검증된 .NET 10 TCP 엔진 + 게임 서버 부트스트랩" 한 묶음. 10K 동시 세션을 검증한 IOCP/Channel/CircularBuffer 스택을 OSS 카탈로그에 노출하여, "C# 게임 서버 = Photon/Mirror/MagicOnion" 외 third option을 만든다. |

---

## 1. PM Discovery (Opportunity Solution Tree)

### 1.1 Outcome (북극성)

> **"C# 게임 서버 신규 프로젝트의 0→1 시간 단축"**
> 측정: 새 게임 서버 프로젝트를 만들고 echo 패킷이 왕복하기까지의 wall-clock 시간 (목표 ≤ 10분).

### 1.2 5-Step Discovery Chain

#### Step 1 — Brainstorm (가능성 발산)

- C# 인디 개발자가 .NET 게임 서버를 만들 때 라이브러리 선택지가 적다 (Photon = 유료/closed, Mirror = Unity 종속, MagicOnion = gRPC 기반).
- FastPortSharp는 이미 10K 세션 검증을 했지만 "엔진 패키지"가 없어 외부에서 재사용 불가능.
- `FastPortServer`는 echo 검증용 코드와 게임 서버 출발점이 섞여 있다 (HANDOFF.md 참고: smoke/load test는 이미 `FastPortTestSmokeServer`로 분리됨).
- `LibNetworks`는 이미 protocol-neutral하게 유지되고 있음 (HANDOFF.md L278).
- 텔레메트리는 최근 분리됨 (`LibTestTelemetry`, "extract-telemetry-contracts-from-network-core") → 엔진 패키지화 사전 조건이 충족됨.

#### Step 2 — Assumptions (가정 추출)

| 가정 ID | 내용 | Impact | Risk | 우선순위 |
|---------|------|:------:|:----:|:------:|
| A1 | 본인/내부 팀이 새 게임 서버를 1개 이상 만들 의도가 실제로 있다 | High | Low | P0 |
| A2 | C# 인디/스튜디오 시장에 "Photon/Mirror가 아닌 third option" 수요가 있다 | High | High | P0 |
| A3 | NuGet + dotnet new template 패키징이 GitHub Template보다 외부 채택률이 높다 | Med | High | P1 |
| A4 | TCP-only (UDP 없음) v1 스코프가 인디 게임 서버에 충분하다 | High | Med | P0 |
| A5 | Protobuf 스키마 생성기가 의미 있는 차별점이다 | Med | Med | P2 |
| A6 | 엔진과 템플릿을 분리해도 사용자가 둘을 혼동하지 않는다 | Med | Low | P2 |

#### Step 3 — Prioritize (Impact × Risk)

P0: A1, A2, A4 — 사용자/시장/스코프의 진실 검증이 가장 시급
P1: A3 — 패키징 형태 결정이 늦으면 v1 출시가 막힘
P2: A5, A6 — v1 후 검증 가능

#### Step 4 — Experiments (가정별 검증 계획)

- **A1**: 본인이 직접 v0 템플릿으로 신규 toy 게임 서버(ex: tic-tac-toe lobby)를 만들어 본다. 5분 내 echo 동작이 가능한가? (dogfooding)
- **A2**: GitHub README + Reddit r/csharp + r/gamedev에 v0.1 소개 → 첫 30일 stars/issue/clone 수 측정. baseline ≥ 50 stars / 5 external issues.
- **A4**: 인디 게임 서버 사례(예: 턴제, 로비, 채팅 서버)에서 TCP-only가 충분한지 페르소나 인터뷰 또는 issue tracker 조사. (UDP/실시간 액션은 v1 비범위로 명시)
- **A3**: Option A/B/C를 동시에 제공하기보다 단일 옵션 선택 후 측정. 본 PRD §6.3에서 추천안 결정.

#### Step 5 — Opportunity Solution Tree

```
Outcome: C# 게임 서버 0→1 시간 단축
│
├── Opportunity 1: "TCP 네트워크 엔진을 매번 다시 짜야 한다"
│   ├── Solution 1.1: LibCommons+LibNetworks를 NuGet 패키지로 분리
│   ├── Solution 1.2: GitHub Template Repository
│   └── Solution 1.3: Monorepo internal template
│
├── Opportunity 2: "프로젝트 부트스트랩이 너무 길다 (csproj/DI/config 설정)"
│   ├── Solution 2.1: dotnet new template (단일 명령으로 생성)
│   ├── Solution 2.2: 샘플 game session/handler/Program.cs 포함
│   └── Solution 2.3: appsettings/logging/telemetry 디폴트 제공
│
├── Opportunity 3: "패킷 정의/직렬화 보일러플레이트가 많다"
│   ├── Solution 3.1: Protobuf .proto → C# 생성 스크립트 포함
│   ├── Solution 3.2: 패킷 ID ↔ 핸들러 매핑 헬퍼
│   └── Solution 3.3: Protocols/ 프로젝트를 템플릿이 참조하도록
│
├── Opportunity 4: "엔진을 업데이트해도 기존 프로젝트가 깨질까 두렵다"
│   ├── Solution 4.1: SemVer + CHANGELOG 정책
│   ├── Solution 4.2: 엔진/템플릿 별도 릴리스 케이던스
│   └── Solution 4.3: NuGet 버전 핀 안내
│
└── Opportunity 5: "C# 게임 서버 = Photon/Mirror" 인식 깨기
    ├── Solution 5.1: 10K 세션 벤치 결과를 README/landing에 노출
    ├── Solution 5.2: 비교표 (Photon/Mirror/MagicOnion vs FastPortSharp)
    └── Solution 5.3: blog/devlog 포스팅으로 SEO 확보 (v1 후)
```

> v1 권장 솔루션 묶음: **1.1 + 2.1 + 2.2 + 2.3 + 3.1 + 3.2 + 4.1 + 4.2 + 5.1 + 5.2**

---

## 2. PM Strategy

### 2.1 JTBD 6-Part Value Proposition

| 파트 | 내용 |
|------|------|
| **For** (대상) | C# .NET 10 기반 게임 서버를 만들고자 하는 본인/내부 팀, 그리고 Unity/.NET 환경에 익숙한 인디 게임 개발자(1-5명 팀) |
| **Who** (상황) | 새 게임 프로젝트의 서버 측을 0부터 만들어야 하고, Photon은 비용/락인이 부담스럽고, Mirror는 Unity 클라이언트와 결합되어 별도 dedicated server에 부적합하며, MagicOnion은 gRPC/HTTP2 의존이 무거운 상황 |
| **The** (제품 카테고리) | 검증된 .NET 10 TCP 네트워크 엔진 + 게임 서버 스타터 템플릿 |
| **That** (혜택) | 0→1 부트스트랩을 5-10분으로 줄이고, 검증된 IOCP/Channel/CircularBuffer 스택을 그대로 사용하며, 게임 로직 코드에만 집중 가능 |
| **Unlike** (차별점) | Photon: closed/유료 / Mirror: Unity 종속 / MagicOnion: gRPC 의존 / LiteNetLib: UDP만 / Nakama·Colyseus: 별도 런타임 필요 |
| **Our product** | FastPortSharp는 .NET 10 native TCP, 10K 동시 세션 실측 검증, MIT 오픈소스, NuGet/Template 듀얼 배포 |

### 2.2 Lean Canvas (9 sections)

| 섹션 | 내용 |
|------|------|
| **1. Problem** | (a) C# 게임 서버 부트스트랩 비용 (b) 검증된 TCP 엔진 부재 (c) Photon/Mirror/MagicOnion 외 third option 부재 |
| **2. Customer Segments** | Primary: 본인/내부 팀 / Secondary: C# 인디 게임 개발자 / Tertiary: 소규모 스튜디오 (5-20명) |
| **3. Unique Value Proposition** | "10K 세션 검증된 .NET 10 TCP 엔진을 dotnet new 한 줄로 시작" |
| **4. Solution** | (a) NuGet `*.Networks` 패키지 (b) `dotnet new` 템플릿 (c) 샘플 game session/handler (d) Protobuf gen 스크립트 (e) 벤치/텔레메트리 기본 내장 |
| **5. Channels** | GitHub README, NuGet.org 카탈로그, Reddit r/csharp·r/gamedev, dev.to/blog, .NET 게임 개발 커뮤니티 |
| **6. Revenue Streams** | v1: 없음 (OSS / MIT). 가능 확장: 유료 컨설팅, 호스팅 솔루션, Pro 모듈 (room/match/auth) |
| **7. Cost Structure** | 본인 시간(주된 비용), GitHub Actions CI 무료 한도, NuGet.org 무료 |
| **8. Key Metrics** | NuGet 다운로드/월, GitHub stars, dotnet new template 설치 수, 외부 issue/PR 수, 벤치 재현 사례 수 |
| **9. Unfair Advantage** | 이미 10K 세션을 실측 검증한 RTT/TPS 데이터셋, .NET 10 최신 Lock/Channel 적용, telemetry 분리 완료 |

### 2.3 SWOT

| | 내용 |
|---|------|
| **Strengths** | 10K 세션 검증, .NET 10 최신, telemetry 이미 분리, MIT 라이선스, 작은 코드베이스 |
| **Weaknesses** | UDP 미지원, room/match/auth 부재, 단일 메인테이너, 인지도 0, 외부 contributor 0 |
| **Opportunities** | C# 게임 서버 OSS 갈증, .NET 10 GA 시점 호재, Photon 가격 부담 트렌드, dotnet template 생태계 성숙 |
| **Threats** | Mirror/MagicOnion이 dedicated server 모드 강화, Microsoft가 자체 게임 서버 SDK 출시, 단일 메인테이너 번아웃, OSS issue 대응 부담 |

**SO 전략**: 10K 검증 + .NET 10 → "검증된 .NET 10 TCP 엔진" 마케팅
**WT 전략**: UDP·room·match는 명시적 비범위 / 로드맵으로 공개 → 기대치 관리, 단일 메인테이너 부담 축소

### 2.4 Strategic Frameworks (적용)

- **Innovator's Dilemma 체크**: 진입 시장이 작고(인디), 기존 강자(Photon)가 잘 안 가는 영역 → low-end disruption 패턴 적합.
- **Crossing the Chasm**: 본인/내부 = early adopter, 인디 = early majority 진입 시 chasm 발생 가능 → §4 Beachhead로 대응.

---

## 3. PM Research

### 3.1 Personas (3종)

#### Persona 1 — "Solo Soyoung" (본인/내부 팀 대표)

- **Role**: 1인 개발자, 본 프로젝트 메인테이너 (= 본인)
- **JTBD**: "다음 toy game server 프로젝트를 시작할 때, 검증된 엔진을 한 줄 명령으로 가져오고 싶다."
- **Pain Points**: 매번 csproj/DI/logging 설정 반복, 엔진 코드와 게임 코드 결합으로 재사용 어려움
- **Tools**: Visual Studio / VS Code, GitHub, NuGet
- **Success Metric**: `dotnet new fastport-game-server` → echo 5분 내 동작

#### Persona 2 — "Indie Ian" (C# 인디 게임 개발자)

- **Role**: 1-3인 인디 게임 스튜디오, Unity/C# 능숙, 서버는 처음
- **JTBD**: "Unity 클라이언트와 어울리는 dedicated 서버를 만들고 싶지만, Photon은 비싸고 Mirror는 Unity headless 부담"
- **Pain Points**: 서버 인프라 학습 곡선, TCP/패킷 설계 경험 부족, 비용 민감
- **Tools**: Unity, Visual Studio, GitHub free tier
- **Success Metric**: 한 주말에 toy 멀티플레이어 서버 동작

#### Persona 3 — "Studio Sora" (소규모 스튜디오 backend lead)

- **Role**: 5-20명 스튜디오에서 backend/server 담당
- **JTBD**: "신규 게임 프로토타입 서버를 빠르게 세팅하고, 검증된 코드 위에서 게임 로직만 추가하고 싶다."
- **Pain Points**: 기존 사내 코드 노후화, MagicOnion gRPC 학습 비용, 팀 단위 컨벤션 필요
- **Tools**: Rider, Azure/AWS, CI/CD
- **Success Metric**: 사내 새 프로젝트 서버 셋업이 1일 → 1시간

### 3.2 Competitive Analysis (5개+)

| 경쟁자 | 카테고리 | 강점 | 약점 | FastPortSharp 차별 |
|---|---|---|---|---|
| **Photon (PUN/Fusion/Quantum)** | 상용 GaaS | 글로벌 인프라, 클라이언트 SDK | 유료, closed-source, vendor lock-in | OSS, self-host, MIT |
| **Mirror Networking** | Unity OSS | Unity 내 통합, 튜토리얼 풍부 | Unity 종속, dedicated headless 비효율 | Unity 비종속 dedicated, .NET 10 native |
| **FishNet** | Unity OSS | Mirror 대비 성능, modern API | Unity 종속, dedicated 한계 | dedicated only, 엔진/템플릿 분리 |
| **MagicOnion** | .NET RPC | gRPC/MessagePack, 비동기 streaming, Cysharp 운영 | gRPC/HTTP2 의존, 게임 외 일반 RPC | TCP raw, lightweight, 게임 특화 |
| **LiteNetLib** | .NET UDP | 가벼운 UDP/reliable layer | UDP only, 서버 부트스트랩 미지원 | TCP, 부트스트랩 템플릿 포함 |
| **Nakama (Heroic Labs)** | 풀스택 게임 BaaS | matchmaking/auth/leaderboard 내장 | Go 기반, 무겁고 복잡, 별도 런타임 | .NET native, 작고 단순, embed 가능 |
| **Colyseus** | TS/Node.js | room state sync, schema | Node.js, JS/TS 생태계 | C#/.NET, native perf |
| **ENet (ENet-CSharp)** | C/C# UDP | 검증된 UDP reliable | UDP, 서버 골격 없음 | TCP, 서버 골격 포함 |

### 3.3 Market Sizing (TAM/SAM/SOM, 거친 추정)

> 2026 시점 기준, 외부 reference 없이 보수적으로 추정 — v1 출시 후 보정 필요.

**Top-down 방법**:
- Global game dev studios (steam/itch/mobile) ≈ 100,000+ active teams (Itch.io 60K+ free dev, Steam ~50K publishers).
- C#/Unity 점유율 ≈ 30-40% → ~30,000-40,000 C# 팀.
- 서버형 게임 (멀티/온라인) 비율 ≈ 20% → **TAM ≈ 6,000-8,000 팀**.
- 그 중 self-host TCP 라우트를 진지하게 고려 ≈ 10% → **SAM ≈ 600-800 팀**.
- v1+v2 (1-2년) 현실적 캡처 ≈ 1-3% → **SOM ≈ 6-25 팀** (NuGet 다운로드는 2-3 자리수 가능, 활성 사용 팀 기준).

**Bottom-up 방법**:
- NuGet `Mirror`-style 게임 네트워킹 패키지 다운로드 ≈ 월 1K-10K (참고치). 그 중 dedicated TCP에 진지한 사용자 비율 ~10% → 월 100-1K 잠재 lead.
- v1 첫 12개월 stars 30-150, NuGet 누적 다운로드 500-5,000, 실제 게임 출시 사용 사례 1-5건이 현실 목표.

**메모**:
- 본 추정은 *order-of-magnitude*로만 사용. 실제 캡처는 §4 Beachhead 첫 6개월 데이터로 보정.
- 본인/내부 팀 사용은 SOM과 별개로 가치 인정 (§4.1).

### 3.4 Customer Journey Map (Indie Ian 기준)

| 단계 | 행동 | 감정 | Pain | FastPort 개입 |
|------|------|------|------|---------------|
| Awareness | reddit/r/gamedev에서 "C# dedicated server" 검색 | 막막 | 옵션이 적음 | README + 비교표 |
| Consideration | Photon 가격 보고 충격, Mirror dedicated 어려움 인지 | 불안 | 비용/난이도 | 10K 벤치, OSS 강조 |
| Trial | `dotnet new fastport-game-server` 실행 | 긴장 | 동작할까? | 5분 내 echo 성공 |
| Adoption | toy 게임에 통합, 패킷 정의 추가 | 자신감 | 패킷 직렬화 보일러플레이트 | Protobuf gen 스크립트 |
| Retention | 트래픽 늘어나며 텔레메트리 봄 | 안도 | 모니터링 부재였으면 어쩔뻔 | 텔레메트리 디폴트 |
| Advocacy | 블로그/유튜브에 후기 | 뿌듯 | 메인테이너 대응 느릴까 | issue triage SLA, contributor 가이드 |

---

## 4. Beachhead, ICP, GTM (Execution)

### 4.1 ICP (Ideal Customer Profile)

- **Firmographics**: 1-5인 인디 / 솔로 메인테이너, 영리/비영리 무관
- **Tech**: C# .NET 6+ (이상적으로 .NET 10), Unity client OR 풀-.NET 서버, Visual Studio/Rider/VS Code
- **Game type**: 턴제, 로비 기반, 카드/보드, 채팅, MMO 백엔드 prototyping (실시간 액션·FPS는 ICP 외)
- **Server**: dedicated server self-host, OCI/Azure/AWS 무관, Linux/Windows 모두
- **Buying motion**: OSS 자체 도입 (구매 의사결정 없음), 메인테이너 신뢰가 핵심

### 4.2 Beachhead Segment (Geoffrey Moore 4기준)

| 기준 | 평가 | 점수(5) |
|------|------|---------|
| **Identifiable** | 본인 + r/csharp/r/gamedev에서 "C# dedicated TCP" 검색 가능 | 4 |
| **Reachable** | GitHub/Reddit/dev.to로 도달 가능, 광고비 0 가능 | 4 |
| **Compelling reason to buy** | Photon 비용·Mirror Unity 종속의 명확한 pain | 4 |
| **Whole product** | v1: TCP+template+protobuf로 minimum viable, room/match/auth는 v2 | 3 |

→ **Beachhead = "Solo/Indie C# devs (Persona 1, 2) building turn-based / lobby-based / chat-style game servers on Linux dedicated host"**

근거: ICP 중 가장 좁고, v1 비범위(UDP/realtime action/room-match)에 영향 받지 않으며, 본인 dogfooding으로 즉시 검증 가능.

### 4.3 GTM Strategy

**Phase 0 — Internal dogfooding (M0-M1)**
- 본인이 새 toy 게임 서버를 v0 템플릿으로 생성, 5분 내 echo 검증.
- Pain points 정리, 외부 공개 전 v0.1 → v0.2 회전.

**Phase 1 — Soft launch (M1-M3)**
- GitHub Template Repository + NuGet preview 동시 제공.
- README 영문/한글, 비교표, 10K 벤치 강조.
- Reddit r/csharp + r/gamedev "Show & Tell" 1회 포스팅.

**Phase 2 — Public launch (M3-M6)**
- NuGet stable 1.0.0, dotnet new template 등록.
- dev.to/Medium 기술 블로그 시리즈 (3편: "Why TCP", "10K 세션 벤치 재현", "Protobuf 패킷 설계").
- 외부 issue/PR triage SLA: 7일 응답.

**Phase 3 — Roadmap expansion (M6+)**
- 첫 외부 사용 사례 1-3건 확보.
- v1.1: heartbeat/keep-alive 기본 헬퍼, room sample, auth 인터페이스 (스펙 정의만).

### 4.4 Channels & Metrics

| 채널 | 활동 | 6개월 목표 |
|------|------|-----------|
| GitHub | README + Template Repo + Issue triage | 50-150 stars |
| NuGet.org | 안정 1.0 + preview 10+회 | 누적 500-5K downloads |
| Reddit | r/csharp, r/gamedev "Show & Tell" 2회 | 100+ upvotes |
| dev.to/Medium | 기술 블로그 3편 | 1편당 1K+ views |
| 본인 블로그/SNS | 한국어 회고 1편 | — |

### 4.5 Battlecards (요약)

| 경쟁자 | 그들이 우리를 공격할 때 | 우리의 대응 |
|---|---|---|
| Photon | "글로벌 인프라/SLA 없잖아" | "self-host MIT, 비용 0, 검증된 10K, SLA 필요한 단계는 다른 옵션" |
| Mirror | "Unity 통합 풍부함" | "dedicated server 시 headless Unity 부담, 우리는 비-Unity dedicated 최적화" |
| MagicOnion | "Cysharp 신뢰, 풀스택 RPC" | "gRPC 의존 무거움, 게임 패킷 지향, 더 작은 surface" |
| Nakama | "matchmaking/auth 내장" | "v1 minimal, embed 쉬움, .NET native, v2 roadmap에 일부 포함" |
| LiteNetLib | "UDP가 게임에는 더 맞음" | "v1은 TCP-first (lobby/turn/chat), UDP는 v3 roadmap 후보" |

### 4.6 Growth Loops

1. **Dogfood loop**: 본인의 차기 게임 → 엔진 개선 피드백 → 템플릿 품질 향상
2. **Content loop**: 10K 벤치 데이터 → 블로그/SEO → GitHub stars → NuGet 다운로드
3. **OSS contributor loop**: clear contribution guide → 외부 PR → 신뢰도/커뮤니티

---

## 5. PRD Core (8 sections)

### 5.1 Problem Statement

C# .NET 게임 서버를 새로 만드는 개발자(본인 포함)는 매번 동일한 TCP accept/session/packet 처리 코드를 다시 작성하거나, Unity 종속(Mirror) 또는 유료(Photon) 솔루션을 쓰거나, 무거운 gRPC(MagicOnion)를 학습해야 한다. FastPortSharp는 이미 10K 세션 검증된 TCP 엔진을 보유하고 있지만, 외부/신규 프로젝트가 재사용할 수 있는 패키지 경계와 부트스트랩 템플릿이 없다.

### 5.2 Goals & Non-Goals

**Goals (v1)**:
- `LibCommons` + `LibNetworks`를 재사용 가능한 네트워크 엔진으로 패키지화
- `FastPortServer`를 (또는 별도 신규 프로젝트) 게임 서버 스타터 템플릿화
- 외부 사용자가 5-10분 내 새 게임 서버 부트스트랩 가능
- 패킷 스키마 / Protobuf 생성기 포함
- 엔진과 템플릿의 별도 릴리스 케이던스 명시
- 기존 `FastPortServer` (echo 검증용)과의 책임 분리 유지 (HANDOFF L278-281)

**Non-Goals (v1)**:
- room/matchmaking 시스템 (v1.1 roadmap)
- 인증(auth)/세션 토큰 (v1.1 roadmap)
- heartbeat/keep-alive 헬퍼 (현재 TCP keep-alive는 있음, 게임 레벨 ping은 v1.1)
- game loop / tick 시스템 (v2 roadmap)
- UDP 지원 (v3 roadmap 후보)
- MAUI dashboard 통합 (별도 feature)
- 다국어 SDK (Unity client 패키지 등은 별도 트랙)

### 5.3 Requirements

#### Functional
- **F-1**: `LibCommons.Networks` (가칭) NuGet 패키지 빌드/발행 파이프라인 — `LibCommons` + `LibNetworks` 묶음 또는 분리 (§6.3 결정사항).
- **F-2**: `dotnet new` 템플릿 정의 (`*.template.config/template.json`) — 한 줄 명령으로 새 솔루션 생성.
- **F-3**: 템플릿 결과물에 게임 session/handler 샘플 포함 (echo + 1개 도메인 패킷 예시).
- **F-4**: Protobuf `.proto` → C# 생성 스크립트/MSBuild target 포함.
- **F-5**: appsettings.json/Serilog/DI 디폴트 구성 포함.
- **F-6**: 기본 텔레메트리 hook (LibTestTelemetry 또는 OpenTelemetry-friendly 추상화).
- **F-7**: README/QUICKSTART (영문 + 한국어), 5분 내 echo 동작 보장.
- **F-8**: 엔진 패키지 SemVer 정책, 템플릿과 엔진의 호환 매트릭스 문서.

#### Non-Functional
- **NF-1**: 빌드: .NET 10 SDK, Release 빌드 0 warning/error.
- **NF-2**: 성능 회귀 없음: 10K 세션 벤치(`s5-random-10k`) 동등성 유지 (HANDOFF L70-90 baseline).
- **NF-3**: 라이선스: MIT (현재와 동일).
- **NF-4**: CI: GitHub Actions에서 빌드/테스트/패키지 검증, NuGet publish는 수동 트리거(보안).
- **NF-5**: 외부 사용자가 README만 보고 부트스트랩 가능 (zero implicit knowledge).

### 5.4 Architecture Boundary (HANDOFF 일관성)

```
┌─────────────────────────────────────────┐
│  Engine (separate release cadence)      │
│  ├─ LibCommons (buffers, packet, IDs)   │
│  └─ LibNetworks (listener, session)     │
│  Distributed as: NuGet packages         │
└─────────────────────────────────────────┘
                    ▲
                    │ depends on
                    │
┌─────────────────────────────────────────┐
│  Game Server Template (separate cadence)│
│  ├─ Program.cs (Generic Host)            │
│  ├─ {Game}Server.cs / Sessions/          │
│  ├─ Protocols/ (.proto + gen)            │
│  └─ appsettings/Serilog/Telemetry         │
│  Distributed as: dotnet new template     │
└─────────────────────────────────────────┘

(Out of template scope, 본 repo 안에 유지)
- FastPortServer       : 기본 엔진 host/sample (HANDOFF L281)
- FastPortTestSmokeServer / TestLoadRunner / TestLoadValidation : test 인프라 (HANDOFF L279)
- LibTestTelemetry     : test telemetry contracts
```

### 5.5 User Stories (INVEST)

| ID | Story | Priority |
|---|---|---|
| US-1 | As Indie Ian, `dotnet new fastport-game-server -n MyGame` 한 줄로 부트스트랩 가능 | P0 |
| US-2 | As Solo Soyoung, `MyGame` 솔루션이 echo 패킷을 client/server 간 5분 내 송수신 | P0 |
| US-3 | As Indie Ian, `Protocols/MyGame.proto` 추가 후 `dotnet build`만으로 C# 코드 생성 | P0 |
| US-4 | As Studio Sora, 엔진 NuGet 버전을 csproj에 핀할 수 있고 SemVer/CHANGELOG 보임 | P0 |
| US-5 | As Solo Soyoung, 템플릿 결과물의 appsettings에서 listen port/log level 변경 가능 | P1 |
| US-6 | As Indie Ian, README QUICKSTART 따라 RTT/TPS 텔레메트리 콘솔 출력 확인 | P1 |
| US-7 | As Studio Sora, 기존 telemetry 추상화에 OpenTelemetry exporter 부착 가능(스펙 명시) | P2 |
| US-8 | As Solo Soyoung, 신규 패킷 핸들러를 `[PacketHandler(id=1001)]` 패턴으로 등록 | P1 |

### 5.6 Test Scenarios

| ID | 시나리오 | 검증 |
|---|---|---|
| T-1 | `dotnet new fastport-game-server -n Sample` 실행 → 솔루션/csproj/Program.cs 생성 | 파일 존재 + Release build 통과 |
| T-2 | Sample 서버 실행 + `FastPortClient`로 echo 송수신 | RTT 로그 정상, 0 disconnect |
| T-3 | Sample에 `Sample.proto` 추가 → 빌드 → 생성 .cs 존재 | MSBuild target 통과 |
| T-4 | NuGet preview 패키지를 별도 솔루션에 설치 → 컴파일/실행 | 의존성 해소, 런타임 정상 |
| T-5 | 10K 벤치(`s5-random-10k`)를 템플릿 결과물에 적용 → baseline 동등 | RTT P95 ≤ baseline + 5% |
| T-6 | `LibNetworks` 단독 NuGet 사용 (템플릿 없이) → BaseSession 상속 가능 | API 표면 호환 |
| T-7 | README QUICKSTART를 따라 외부인 1명이 5분 내 echo 동작 (사용자 테스트) | 5분 ≤ wall-clock |
| T-8 | 엔진 패치 버전 올리고 템플릿은 그대로 → 호환 매트릭스 통과 | 빌드/테스트 OK |

### 5.7 Pre-mortem (Top 3 Risks)

> "프로젝트가 6개월 후 실패했다면 그 이유는?"

| Risk | 발생 시 영향 | 완화 |
|------|------|------|
| **R-1: 본인 외 사용자가 0명** (A2 가정 실패) | OSS 채택 실패, 단일-유저 도구 | (a) 본인 dogfooding으로 가치 보장 (b) Reddit 1회 검증 후 channel 확장 결정 (c) NuGet 통계로 30일 결정 게이트 |
| **R-2: 엔진/템플릿 분리 후 둘 다 깨짐 / 호환 매트릭스 폭발** | 메인테이너 부담 폭증 | (a) 엔진 SemVer 엄격 (b) 템플릿은 엔진 minor만 lock (c) CI에 호환 매트릭스 자동 빌드 |
| **R-3: v1 스코프(TCP only)가 인디에게 부족** (A4 가정 실패) | 사용자가 LiteNetLib·Mirror로 회귀 | (a) lobby/turn/chat 사용 사례에 명확히 포지셔닝 (b) UDP는 roadmap에 명시 (c) 첫 외부 issue 3건이 UDP 요청이면 v2 우선순위 재평가 |

### 5.8 Stakeholder Map

| Stakeholder | 역할 | 관여도 |
|---|---|---|
| 본인 (boinred) | 메인테이너, primary user | Owner |
| 내부 팀 / 개인 동료 (있다면) | early dogfood | High |
| 외부 인디 개발자 | beachhead user | Medium (M3+) |
| GitHub Actions / NuGet.org | infrastructure | Low (지속 비용 0 가정) |
| (잠재) 외부 contributor | code/docs 기여 | Low → Medium (M6+) |

---

## 6. Special Decisions (사용자 요청 항목)

### 6.1 Distribution Form Recommendation

| 항목 | A: NuGet + dotnet new | B: GitHub Template Repo | C: Monorepo internal template |
|---|---|---|---|
| 외부 채택률 | High (`nuget.org` 검색 가능) | Medium (GitHub 검색 필요) | Very Low (외부 비공개에 가까움) |
| 엔진 업데이트 전파 | 명시적 (NuGet bump) | 수동 (sync fork 필요) | 자동 (monorepo refactor) |
| 격리 (브랜치/이슈) | High (별 repo 가능) | Medium | Low (test 코드와 섞임) |
| CI/Release 부담 | High (NuGet publish, 버저닝) | Low (tag만) | Very Low |
| Contributor onboarding | Medium (NuGet 신뢰 필요) | High (fork/clone 친숙) | Low (FastPortSharp 전체 이해 필요) |
| 본인/내부 부트스트랩 속도 | Medium-High | High | High |
| OSS 시그널 | Strong | Medium | Weak |

**추천 (Phased Path)**:

> **Phase 1 (M0-M2)**: **C → B 동시** — Monorepo 안에 `FastPortGameServerTemplate` 신규 프로젝트를 추가하여 dogfooding하고, GitHub Template Repo로도 동시 노출(빠른 외부 trial 채널 확보).
>
> **Phase 2 (M2-M4)**: **A 추가** — `LibCommons`/`LibNetworks` NuGet preview 발행, `dotnet new` 템플릿 등록.
>
> **Phase 3 (M4+)**: **A 메인 + B 보조** — NuGet + dotnet new를 1차 배포 채널로, GitHub Template Repo는 2차(low-trust trial용)로 유지. Monorepo 내부 template 프로젝트는 dogfood/CI 재현용으로 잔존.

근거:
- 사용자 인텐트가 "본인/내부 우선, 외부 OSS 미확정"이므로 Phase 1은 가장 빠르고 회수 가능한 형태(C+B) 선택.
- A는 외부 OSS 의지가 분명해진 시점에만 부담을 짊어진다 (NuGet publish는 비가역).
- B는 NuGet publish 부담 없이 외부 신호 측정 가능 (R-1 게이트와 정합).

### 6.2 Brand Name Candidates (2-3개)

| 후보 | 의미 | NuGet ID(예시) | NPM/도메인 충돌 체크 가이드 | 평가 |
|---|---|---|---|---|
| **A. FastPort.GameServer** | 기존 자산 유지(SEO/연속성), Engine은 `FastPort.Networks` | `FastPort.Networks`, `FastPort.GameServer.Template` | `nuget.org/packages?q=FastPort` 검색 / `fastport.dev` whois | 🟢 안전, 자산 재활용 |
| **B. Hangar (Hangar.Net 또는 Hangar.GameServer)** | 격납고 = "엔진을 격납해서 꺼내 쓴다" 비유, 영문 친숙 | `Hangar.Net`, `Hangar.GameServer.Template` | `nuget.org/packages?q=Hangar`, `hangar.dev/.io` 도메인 | 🟡 충돌 가능성 (PaperMC Hangar 존재 — 게임 도메인 겹침) |
| **C. Slipway (Slipway.Net)** | 조선소의 진수대 = "게임 서버를 진수한다" 비유, 미사용 가능성 높음 | `Slipway.Net`, `Slipway.GameServer.Template` | `nuget.org/packages?q=Slipway`, `slipway.dev` 도메인 | 🟢 충돌 적을 가능성, 영문 fresh |

**추천**: **A (FastPort.* 유지)**

근거:
1. 본 repo의 README/벤치/HANDOFF 모든 자산이 "FastPort"로 축적됨 — 인지 자산 재활용.
2. SEO/검색 연속성 (보존된 dev history).
3. 엔진과 템플릿을 `FastPort.Networks` / `FastPort.GameServer.Template` 같은 `FastPort.*` namespace로 묶으면 "엔진=FastPort, 템플릿=FastPort.GameServer"의 정체성이 자연스러움.
4. B/C는 신규 브랜드 비용 + NuGet/도메인 사용성 검증 부담을 추가로 짊어짐.

**NuGet/도메인 사용 가능성 사전 체크 절차** (실행 권장):
```bash
# NuGet
curl -s "https://azuresearch-usnc.nuget.org/query?q=FastPort.Networks&take=5" | jq '.data[].id'
curl -s "https://azuresearch-usnc.nuget.org/query?q=FastPort.GameServer&take=5" | jq '.data[].id'
# Domain (선택)
whois fastport.dev || true
```

### 6.3 Engine vs Template — 별도 릴리스 케이던스 (명시)

> 본 PRD의 핵심 architectural decision.

- **엔진 (`LibCommons`/`LibNetworks` → `FastPort.Networks` 패키지)**: SemVer 엄격. 성능/API 변화는 minor/major bump.
- **템플릿 (`FastPort.GameServer.Template`)**: 엔진 minor 버전을 `^x.y` 범위로 의존. 자체 패치 릴리스 가능.
- 두 산출물은 **별도 GitHub release tag** 사용. 예: `engine-v1.0.0`, `template-v1.0.0`.
- CI 호환 매트릭스: `(engine x.y) × (template a.b)` 최근 3쌍 자동 빌드/테스트.

### 6.4 Out-of-Scope 명시 (재확인)

다음은 **v1 비범위**, **roadmap 후보**:

- room / matchmaking
- authentication / session token
- game-level heartbeat (TCP keep-alive와 별개)
- game loop / tick scheduler
- UDP 전송 (v3 후보)
- MAUI dashboard (별도 feature: `maui-telemetry-dashboard-foundation`)
- Unity client SDK 별도 패키지 (별도 트랙)

---

## 7. Roadmap (Indicative)

| 버전 | 시점 | 내용 |
|------|------|------|
| **v0.x** | M0-M2 | Monorepo internal template + GitHub Template Repo, dogfooding |
| **v1.0** | M2-M4 | NuGet `FastPort.Networks` stable, `dotnet new fastport-game-server` 등록 |
| **v1.1** | M4-M6 | heartbeat helper, room sample (no auth), telemetry OpenTelemetry adapter spec |
| **v2.0** | M6-M12 | matchmaking primitive, auth interface, tick/game-loop helper (선택) |
| **v3.0** | 12M+ | UDP transport (별도 가정 검증 후), Unity client SDK 검토 |

---

## 8. Success Criteria (v1)

- [ ] `dotnet new fastport-game-server -n X` 한 줄로 빌드 가능한 솔루션 생성
- [ ] 외부 1명이 README만 보고 5분 내 echo 동작 (사용자 테스트 1회 이상)
- [ ] 10K 세션 벤치 baseline 동등 (RTT P95 ≤ baseline + 5%)
- [ ] NuGet preview 1회 publish + smoke install 검증
- [ ] CHANGELOG + 호환 매트릭스 문서 존재
- [ ] HANDOFF.md "Important Architecture Decisions" 위반 없음 (LibNetworks protocol-neutral 유지, smoke 코드는 SmokeServer에 머무름)

---

## 9. Attribution

본 PRD는 [pm-skills](https://github.com/phuryn/pm-skills) (Pawel Huryn, MIT) 의 framework들을 차용:
- 5-Step Discovery Chain & Opportunity Solution Tree (Teresa Torres 영감)
- JTBD 6-Part Value Proposition
- Lean Canvas (Ash Maurya)
- Beachhead 4-criteria (Geoffrey Moore — Crossing the Chasm)
- Pre-mortem (Gary Klein)

프로젝트별 컨텍스트는 `README.md`, `HANDOFF.md`, `AGENTS.md`, 그리고 git history(`docs/archive/2026-04~05/*`)에서 합성됨.

---

> **다음 단계**: `/pdca plan game-server-template-from-network-engine`
> (본 PRD가 Plan 문서에 자동 참조됩니다.)
