# FastPortSharp 성능 기준점 (Baseline)

> **측정일**: 2026-01-20  
> **환경**: Windows 11 (25H2), Intel Core i5-14600K 3.50GHz, 14 cores  
> **런타임**: .NET 10.0.1 (10.0.125.57005), x86-64-v3  
> **BenchmarkDotNet**: v0.15.8

---

## 📊 측정 결과 요약


## 1. CircularBuffer 벤치마크

순환 버퍼의 읽기/쓰기 성능을 측정합니다.

| Method | Mean | Error | StdDev | Rank | Allocated |
|--------|-----:|------:|-------:|:----:|----------:|
| Write 64B | 560.0 ns | 65.48 ns | 193.1 ns | 1 | - |
| Write 1KB | 753.0 ns | 55.48 ns | 163.6 ns | 2 | - |
| Write 8KB | 1,099.0 ns | 75.38 ns | 221.1 ns | 3 | - |
| Write+Read 64B | 701.0 ns | 61.44 ns | 178.2 ns | 2 | - |
| Write+Read 1KB | 718.2 ns | 102.45 ns | 300.5 ns | 2 | - |
| **Write x10 (버퍼 확장)** | **15,659.0 ns** | 675.91 ns | 1,992.9 ns | 5 | **442,584 B** 🔴 |
| TryGetBasePackets (10 packets) | 3,009.5 ns | 147.84 ns | 397.2 ns | 4 | 1,864 B |

### 분석
- ✅ **기본 Write 성능**: 우수 (64B~8KB 모두 약 1μs)
- 🔴 **버퍼 확장 시 문제**: `Array.Resize()` 호출로 **442KB 메모리 할당** 발생
- 🟡 **패킷 파싱**: 10개 패킷당 1.8KB 할당 (LINQ `Skip().Take().ToArray()` 영향)

---

## 2. Buffer 비교 벤치마크 (CircularBuffer vs QueueBuffer)

| Method | DataSize | Mean | Rank | Allocated |
|--------|:--------:|-----:|:----:|----------:|
| **CircularBuffer Write** | **64** | **230.0 ns** | **1** | 8.17 KB |
| CircularBuffer Write+Peek | 64 | 250.7 ns | 2 | 8.17 KB |
| CircularBuffer Write+Drain | 64 | 228.1 ns | 1 | 8.17 KB |
| QueueBuffer Write | 64 | 311.9 ns | 3 | 8.2 KB |
| QueueBuffer Write+Peek | 64 | 317.4 ns | 3 | 8.28 KB |
| QueueBuffer Write+Drain | 64 | 392.0 ns | 4 | 8.2 KB |
| **CircularBuffer Write** | **512** | **230.5 ns** | **1** | 8.17 KB |
| QueueBuffer Write | 512 | 907.3 ns | 5 | 8.2 KB |
| QueueBuffer Write+Drain | 512 | 1,394.7 ns | 6 | 8.2 KB |
| **CircularBuffer Write** | **4096** | **261.2 ns** | **2** | 8.17 KB |
| QueueBuffer Write | 4096 | **9,195.6 ns** | 7 | 8.2 KB |
| QueueBuffer Write+Drain | 4096 | **16,077.1 ns** | 8 | 8.2 KB |

### 분석
```
데이터 크기별 성능 비교 (Write 기준):

64B:   CircularBuffer 230ns vs QueueBuffer 312ns    → 1.4배 차이
512B:  CircularBuffer 231ns vs QueueBuffer 907ns    → 3.9배 차이
4KB:   CircularBuffer 261ns vs QueueBuffer 9,196ns  → 35.2배 차이 🔴
```

- 🔴 **QueueBuffer는 대용량 데이터에 부적합** - 바이트 단위 Enqueue로 인한 성능 저하
- ✅ **CircularBuffer 권장** - 데이터 크기 증가에도 성능 일정

---

## 3. Channel vs BufferBlock (TPL Dataflow) 벤치마크

| Method | MessageCount | Mean | Rank | Allocated |
|--------|:------------:|-----:|:----:|----------:|
| **Channel TryWrite (sync)** | **100** | **4.199 μs** | **1** | 16.45 KB |
| BufferBlock Post (sync) | 100 | 6.952 μs | 2 | 15.61 KB |
| Channel\<T\> Unbounded | 100 | 17.739 μs | 3 | 15.79 KB |
| Channel\<T\> Bounded | 100 | 20.523 μs | 4 | 15.37 KB |
| **BufferBlock\<T\>** | **100** | **44.354 μs** | **6** | **42.93 KB** 🔴 |
| Channel TryWrite (sync) | 1000 | 22.847 μs | 4 | 150.67 KB |
| BufferBlock Post (sync) | 1000 | 36.809 μs | 5 | 136.39 KB |
| Channel\<T\> Unbounded | 1000 | 75.606 μs | 7 | 122.81 KB |
| Channel\<T\> Bounded | 1000 | 101.744 μs | 8 | 128.2 KB |
| **BufferBlock\<T\>** | **1000** | **220.892 μs** | **9** | **396.51 KB** 🔴 |

### 분석
```
비동기 처리 (100 메시지):
Channel Unbounded: 17.7μs, 15.79KB
BufferBlock:       44.4μs, 42.93KB
→ Channel이 2.5배 빠르고, 메모리 63% 절약

비동기 처리 (1000 메시지):
Channel Unbounded: 75.6μs,  122.81KB
BufferBlock:       220.9μs, 396.51KB
→ Channel이 2.9배 빠르고, 메모리 69% 절약 🎯
```

- 🟢 **Channel\<T\> 강력 권장** - 속도 2.5~3배, 메모리 60~70% 개선
- 동기 쓰기 시 `TryWrite`가 가장 효율적

---

## 4. Lock 벤치마크

| Method | Iterations | Mean | Ratio | Rank |
|--------|:----------:|-----:|------:|:----:|
| **Interlocked.Increment** | **1000** | **7.775 μs** | **0.44** | **1** |
| **.NET 10 Lock (EnterScope)** | **1000** | **16.700 μs** | **0.94** | **2** |
| **.NET 10 Lock** | **1000** | **16.898 μs** | **0.96** | **2** |
| lock (object) | 1000 | 17.734 μs | 1.00 | 2 |
| ReaderWriterLockSlim (Write) | 1000 | 18.132 μs | 1.03 | 2 |
| ReaderWriterLockSlim (Read) | 1000 | 19.713 μs | 1.12 | 3 |
| **Interlocked.Increment** | **10000** | **39.193 μs** | **0.28** | **1** |
| **.NET 10 Lock** | **10000** | **125.085 μs** | **0.91** | **2** |
| **.NET 10 Lock (EnterScope)** | **10000** | **126.313 μs** | **0.92** | **2** |
| ReaderWriterLockSlim (Read) | 10000 | 121.107 μs | 0.88 | 2 |
| ReaderWriterLockSlim (Write) | 10000 | 136.654 μs | 0.99 | 3 |
| lock (object) | 10000 | 138.014 μs | 1.00 | 3 |

### 분석
```
성능 순위 (빠른 순, 10K iterations 기준):
1. Interlocked.Increment  - 기준 대비 72% 빠름 (단순 카운터용)
2. RWLS (Read)            - 기준 대비 12% 빠름
3. .NET 10 Lock           - 기준 대비 9% 빠름 ✅
4. .NET 10 Lock (EnterScope) - 기준 대비 8% 빠름
5. RWLS (Write)           - 기준 대비 1% 빠름
6. lock (object)          - 기준점
```

- 🟢 **.NET 10 Lock 권장** - 기존 `lock` 대비 **9% 성능 향상**, 즉시 적용 가능
- ⚡ **Interlocked** - 단순 카운터에는 가장 효율적 (3.5배 빠름)
- 🟢 **RWLS Read** - 읽기 위주 작업에서 의외로 빠름 (12% 향상)

---

## 5. 최적화 기법 벤치마크

### 배열 할당 비교

| Method | BufferSize | Mean | Ratio | Allocated |
|--------|:----------:|-----:|------:|----------:|
| **new byte[] 할당** | **256** | **10.86 ns** | **1.00** | **280 B** |
| ArrayPool.Rent | 256 | 8.64 ns | 0.80 | 0 B |
| **new byte[] 할당** | **1024** | **33.21 ns** | **1.00** | **1,048 B** |
| ArrayPool.Rent | 1024 | 14.85 ns | 0.45 | 0 B |
| **new byte[] 할당** | **4096** | **128.44 ns** | **1.00** | **4,120 B** |
| ArrayPool.Rent | 4096 | 28.72 ns | 0.22 | 0 B |

### 복사 방식 비교 (4KB 기준)

| Method | Mean | Allocated |
|--------|-----:|----------:|
| Buffer.BlockCopy | 128.91 ns | 4,120 B |
| Array.Copy | 127.26 ns | 4,120 B |
| Span.CopyTo | 126.60 ns | 4,120 B |

### 정수 변환 비교

| Method | Mean | Allocated |
|--------|-----:|----------:|
| BitConverter.ToUInt16 | 0.11 ns | 0 B |
| **BinaryPrimitives (stackalloc)** | **0.30 ns** | **0 B** |
| BitConverter.GetBytes | 2.57 ns | 32 B |
| BinaryPrimitives.ReadUInt16 | 2.68 ns | 0 B |

### 분석
```
ArrayPool 효과 (vs new byte[]):
256B:  20% 빠름, 할당 제거
1KB:   55% 빠름, 할당 제거
4KB:   78% 빠름, 할당 제거 🎯

복사 방식: 성능 차이 거의 없음 (Span.CopyTo가 약간 빠름)

정수 변환:
- 읽기: BitConverter.ToUInt16 가장 빠름 (0.11ns)
- 쓰기: BinaryPrimitives + stackalloc 가장 빠름 (0.30ns, 할당 없음)
```

- 🟢 **ArrayPool 강력 권장** - 크기가 클수록 효과 큼 (4KB에서 78% 향상)
- 🟢 **BinaryPrimitives** - 정수 쓰기 시 할당 없이 처리 가능

---

## 6. Packet 벤치마크

| Method | Mean | Allocated |
|--------|-----:|----------:|
| Create Packet 64B | 15.91 ns | 216 B |
| Create Packet 256B | 21.86 ns | 408 B |
| Create Packet 1KB | 45.34 ns | 1,176 B |
| Create 100 Packets (64B each) | 1,497.48 ns | 21,600 B |
| Access Packet Data | 19.05 ns | 376 B |

### 분석
- 🟡 **100개 패킷 생성 시 21.6KB 할당** - LINQ 사용으로 인한 오버헤드
- 💡 **개선안**: `ArrayPool` + `Span` 슬라이싱으로 할당 최소화 가능

---

## 📈 최적화 우선순위 (측정 기반)

| 순위 | 항목 | 현재 문제 | 예상 개선 | 난이도 |
|:----:|------|----------|----------|:------:|
| **1** | **ArrayPool 적용** | 버퍼 확장 시 442KB 할당 | 메모리 90%↓, 속도 78%↑ | 중 |
| **2** | **Channel\<T\> 전환** | BufferBlock 느림 | 속도 3배↑, 메모리 69%↓ | 중 |
| **3** | **.NET 10 Lock 적용** | 기존 lock 사용 | 속도 9%↑ | 낮음 |
| **4** | **BasePacket 최적화** | LINQ로 배열 복사 | 할당 감소 | 중 |
| **5** | **QueueBuffer 제거** | 35배 느림 | CircularBuffer로 통일 | 낮음 |

---


## 📁 원본 데이터

벤치마크 결과 파일: `BenchmarkDotNet.Artifacts/results/`

---

## 🔄 다음 단계

1. [x] 기준점(Baseline) 측정 완료 ✅
2. [ ] 최적화 적용 (ArrayPool, Channel, .NET 10 Lock)
3. [ ] 최적화 후 재측정
4. [ ] Before/After 비교 문서 작성
