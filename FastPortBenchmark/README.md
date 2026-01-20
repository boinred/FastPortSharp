# FastPortBenchmark

FastPortSharp 프로젝트의 성능을 측정하기 위한 벤치마크 프로젝트입니다.

## 벤치마크 목록

| 파일 | 설명 |
|------|------|
| `CircularBufferBenchmarks` | 순환 버퍼 읽기/쓰기 성능 |
| `PacketBenchmarks` | 패킷 생성 및 처리 성능 |
| `BufferComparisonBenchmarks` | CircularBuffer vs QueueBuffer |
| `OptimizationTechniquesBenchmarks` | ArrayPool, Span, BinaryPrimitives 등 |
| `ChannelVsDataflowBenchmarks` | Channel vs BufferBlock |
| `LockBenchmarks` | lock vs .NET 9 Lock vs RWLS |

## 실행 방법

### 전체 벤치마크 실행
```bash
dotnet run -c Release
```

### 특정 벤치마크만 실행
```bash
# CircularBuffer 관련
dotnet run -c Release -- --filter *CircularBuffer*

# Packet 관련
dotnet run -c Release -- --filter *Packet*

# Lock 비교
dotnet run -c Release -- --filter *Lock*

# 최적화 기법 비교
dotnet run -c Release -- --filter *Optimization*

# Channel vs Dataflow
dotnet run -c Release -- --filter *Channel*
```

### 빠른 테스트 (Dry Run)
```bash
dotnet run -c Release -- --filter *CircularBuffer* --job Dry
```

### 결과 내보내기
```bash
# Markdown 형식
dotnet run -c Release -- --filter * --exporters md

# HTML 형식
dotnet run -c Release -- --filter * --exporters html
```

## 결과 해석

### 주요 지표
- **Mean**: 평균 실행 시간
- **Error**: 오차 범위 (신뢰구간 99.9%)
- **StdDev**: 표준 편차
- **Allocated**: 할당된 메모리

### 메모리 최적화 확인
```
| Method          | Mean     | Allocated |
|-----------------|----------|-----------|
| new byte[]      | 100 ns   | 1024 B    |  ← 매번 할당
| ArrayPool.Rent  | 50 ns    | 0 B       |  ← 재사용
```

## 결과 저장 위치

벤치마크 결과는 `BenchmarkDotNet.Artifacts/` 폴더에 저장됩니다:
- `results/` - 상세 결과 파일
- `*.md` - 마크다운 리포트
- `*.html` - HTML 리포트

## 주의사항

1. **반드시 Release 모드로 실행**
   ```bash
   dotnet run -c Release
   ```

2. **다른 프로그램 종료**: 정확한 측정을 위해 불필요한 프로그램을 종료하세요.

3. **전원 설정**: 노트북은 전원 연결 + 고성능 모드로 설정하세요.

4. **첫 실행 시 워밍업**: JIT 컴파일로 인해 첫 실행은 느릴 수 있습니다.
