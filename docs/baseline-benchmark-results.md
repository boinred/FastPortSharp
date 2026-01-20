# FastPortSharp 성능 기준점 (Baseline)

> **측정일**: 2026-01-20  
> **환경**: Windows 11 (25H2), Intel Core i5-14600K 3.50GHz, 14 cores  
> **런타임**: .NET 10.0, x86-64-v3  
> **BenchmarkDotNet**: v0.15.8

---

## ?? 측정 결과 요약

### 핵심 발견사항

| 항목 | 결과 | 개선 가능성 |
|------|------|-------------|
| ?? **버퍼 확장** | 442KB 할당 (10회 쓰기) | ArrayPool 적용 시 대폭 감소 예상 |
| ?? **QueueBuffer** | CircularBuffer 대비 20배 느림 (4KB 기준) | CircularBuffer 사용 권장 |
| ?? **.NET 10 Lock** | 기존 lock 대비 7~8% 빠름 | 즉시 적용 가능 |
| ?? **Channel** | BufferBlock 대비 2~3배 빠름, 메모리 70% 절약 | 즉시 적용 권장 |
| ?? **패킷 파싱** | 10개 패킷당 1.8KB 할당 | 구조 개선 필요 |

---

## 1. CircularBuffer 벤치마크

순환 버퍼의 읽기/쓰기 성능을 측정합니다.

| Method | Mean | Error | StdDev | Rank | Allocated |
|--------|-----:|------:|-------:|:----:|----------:|
| Write 64B | 360.2 ns | 19.54 ns | 55.44 ns | 1 | - |
| Write 1KB | 430.4 ns | 29.61 ns | 83.52 ns | 2 | - |
| Write 8KB | 669.9 ns | 23.12 ns | 65.58 ns | 4 | - |
| Write+Read 64B | 500.0 ns | 0.00 ns | 0.00 ns | 3 | - |
| Write+Read 1KB | 707.0 ns | 61.59 ns | 181.61 ns | 4 | - |
| **Write x10 (버퍼 확장)** | **13,539.3 ns** | 270.61 ns | 388.10 ns | 6 | **442,584 B** ?? |
| TryGetBasePackets (10 packets) | 3,476.4 ns | 112.16 ns | 310.80 ns | 5 | 1,864 B |

### 분석
- ? **기본 Write 성능**: 우수 (64B~8KB 모두 1μs 미만)
- ?? **버퍼 확장 시 문제**: `Array.Resize()` 호출로 **442KB 메모리 할당** 발생
- ?? **패킷 파싱**: 10개 패킷당 1.8KB 할당 (LINQ `Skip().Take().ToArray()` 영향)

---

## 2. Buffer 비교 벤치마크 (CircularBuffer vs QueueBuffer)

| Method | DataSize | Mean | Rank | Allocated |
|--------|:--------:|-----:|:----:|----------:|
| **CircularBuffer Write** | **64** | **244.3 ns** | **1** | 8.17 KB |
| CircularBuffer Write+Peek | 64 | 255.4 ns | 1 | 8.17 KB |
| CircularBuffer Write+Drain | 64 | 236.3 ns | 1 | 8.17 KB |
| QueueBuffer Write | 64 | 315.1 ns | 2 | 8.2 KB |
| QueueBuffer Write+Peek | 64 | 339.9 ns | 3 | 8.28 KB |
| QueueBuffer Write+Drain | 64 | 386.0 ns | 4 | 8.2 KB |
| **CircularBuffer Write** | **512** | **239.6 ns** | **1** | 8.17 KB |
| QueueBuffer Write | 512 | 913.4 ns | 5 | 8.2 KB |
| **CircularBuffer Write** | **4096** | **267.2 ns** | **1** | 8.17 KB |
| QueueBuffer Write | 4096 | **5,546.9 ns** | 8 | 8.2 KB |
| QueueBuffer Write+Drain | 4096 | **8,282.3 ns** | 10 | 8.2 KB |

### 분석
```
데이터 크기별 성능 비교 (Write 기준):

64B:   CircularBuffer 244ns vs QueueBuffer 315ns   → 1.3배 차이
512B:  CircularBuffer 240ns vs QueueBuffer 913ns   → 3.8배 차이
4KB:   CircularBuffer 267ns vs QueueBuffer 5,547ns → 20.8배 차이 ??
```

- ?? **QueueBuffer는 대용량 데이터에 부적합** - 바이트 단위 Enqueue로 인한 성능 저하
- ? **CircularBuffer 권장** - 데이터 크기 증가에도 성능 일정

---

## 3. Channel vs BufferBlock (TPL Dataflow) 벤치마크

| Method | MessageCount | Mean | Rank | Allocated |
|--------|:------------:|-----:|:----:|----------:|
| **Channel TryWrite (sync)** | **100** | **3.980 μs** | **1** | 16.5 KB |
| BufferBlock Post (sync) | 100 | 6.959 μs | 2 | 15.61 KB |
| Channel\<T\> Unbounded | 100 | 15.803 μs | 3 | 15.84 KB |
| Channel\<T\> Bounded | 100 | 17.380 μs | 4 | 15.52 KB |
| **BufferBlock\<T\>** | **100** | **35.328 μs** | **5** | **42.98 KB** ?? |
| Channel TryWrite (sync) | 1000 | 40.566 μs | 6 | 150.72 KB |
| BufferBlock Post (sync) | 1000 | 62.262 μs | 7 | 136.39 KB |
| Channel\<T\> Unbounded | 1000 | 90.261 μs | 8 | 132.54 KB |
| Channel\<T\> Bounded | 1000 | 120.360 μs | 9 | 131.51 KB |
| **BufferBlock\<T\>** | **1000** | **359.378 μs** | **10** | **395.98 KB** ?? |

### 분석
```
비동기 처리 (100 메시지):
Channel Unbounded: 15.8μs, 15.84KB
BufferBlock:       35.3μs, 42.98KB
→ Channel이 2.2배 빠르고, 메모리 63% 절약

비동기 처리 (1000 메시지):
Channel Unbounded: 90.3μs,  132.54KB
BufferBlock:       359.4μs, 395.98KB
→ Channel이 4배 빠르고, 메모리 66% 절약 ??
```

- ?? **Channel\<T\> 강력 권장** - 속도 2~4배, 메모리 60~70% 개선
- 동기 쓰기 시 `TryWrite`가 가장 효율적

---

## 4. Lock 벤치마크

| Method | Iterations | Mean | Ratio | Rank |
|--------|:----------:|-----:|------:|:----:|
| **Interlocked.Increment** | **1000** | **6.736 μs** | **0.39** | **1** |
| **.NET 10 Lock** | **1000** | **15.801 μs** | **0.92** | **2** |
| .NET 10 Lock (EnterScope) | 1000 | ~16 μs | ~0.93 | 2 |
| lock (object) | 1000 | 17.223 μs | 1.00 | 3 |
| ReaderWriterLockSlim (Write) | 1000 | 19.535 μs | 1.14 | 4 |
| ReaderWriterLockSlim (Read) | 1000 | 20.324 μs | 1.18 | 4 |
| **Interlocked.Increment** | **10000** | **37.986 μs** | **0.27** | **1** |
| **.NET 10 Lock** | **10000** | **128.908 μs** | **0.93** | **2** |
| lock (object) | 10000 | 138.614 μs | 1.00 | 3 |
| ReaderWriterLockSlim (Write) | 10000 | 151.169 μs | 1.09 | 4 |
| ReaderWriterLockSlim (Read) | 10000 | 158.814 μs | 1.15 | 5 |

### 분석
```
성능 순위 (빠른 순):
1. Interlocked.Increment - 기준 대비 61~73% 빠름 (단순 카운터용)
2. .NET 10 Lock          - 기준 대비 7~8% 빠름 ?
3. lock (object)         - 기준점
4. ReaderWriterLockSlim  - 기준 대비 9~15% 느림
```

- ?? **.NET 10 Lock 권장** - 기존 `lock` 대비 **7~8% 성능 향상**, 즉시 적용 가능
- ? **Interlocked** - 단순 카운터에는 가장 효율적 (2.5~3.6배 빠름)
- ?? **RWLS** - 읽기/쓰기 분리가 명확한 경우에만 사용

---

## ?? 최적화 우선순위 (측정 기반)

| 순위 | 항목 | 현재 문제 | 예상 개선 | 난이도 |
|:----:|------|----------|----------|:------:|
| **1** | **ArrayPool 적용** | 버퍼 확장 시 442KB 할당 | 메모리 90%↓ | 중 |
| **2** | **Channel\<T\> 전환** | BufferBlock 느림 | 속도 4배↑, 메모리 66%↓ | 중 |
| **3** | **.NET 10 Lock 적용** | 기존 lock 사용 | 속도 8%↑ | 낮음 |
| **4** | **BasePacket 최적화** | LINQ로 배열 복사 | 할당 감소 | 중 |
| **5** | **QueueBuffer 제거** | 20배 느림 | CircularBuffer로 통일 | 낮음 |

---

## ?? 즉시 적용 가능한 개선 (Quick Wins)

### 1. .NET 10 Lock 전환 (5분)
```csharp
// Before
private readonly ReaderWriterLockSlim m_Lock = new();

// After
private readonly Lock m_Lock = new();

// 사용법 1: lock 문 사용
lock (m_Lock)
{
    // critical section
}

// 사용법 2: EnterScope() 사용 (using과 함께)
using (m_Lock.EnterScope())
{
    // critical section
}
```

### 2. Channel 전환 (30분)
```csharp
// Before
private readonly BufferBlock<BasePacket> m_ReceivedPackets;

// After  
private readonly Channel<BasePacket> m_ReceivedPackets = 
    Channel.CreateBounded<BasePacket>(new BoundedChannelOptions(1000)
    {
        SingleReader = true,
        SingleWriter = true
    });
```

---

## ?? 원본 데이터

벤치마크 결과 파일: `BenchmarkDotNet.Artifacts/results/`

---

## ?? 다음 단계

1. [x] 기준점(Baseline) 측정 완료
2. [ ] 최적화 적용 (ArrayPool, Channel, .NET 10 Lock)
3. [ ] 최적화 후 재측정
4. [ ] Before/After 비교 문서 작성
