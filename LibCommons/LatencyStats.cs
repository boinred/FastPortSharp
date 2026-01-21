using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LibCommons;

/// <summary>
/// Latency 통계 수집 및 분석 클래스
/// </summary>
public class LatencyStats
{
    private readonly ConcurrentQueue<LatencySample> _samples = new();
    private readonly int _maxSamples;
    private long _totalSamples;
    
    // 통계 출력 옵션
    private readonly LatencyStatsOptions _options;
    private readonly ILogger? _logger;
    private readonly string _filePath;
    private readonly DateTime _startTime;
    private DateTime _lastFileWrite = DateTime.MinValue;

    public LatencyStats(LatencyStatsOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new LatencyStatsOptions();
        _maxSamples = _options.MaxSamplesInMemory;
        _logger = logger;
        _startTime = DateTime.Now;
        
        // 파일 경로 생성: Stats/latency_stats_2026-01-20_15-30-45.log
        _filePath = GenerateFilePath(_options.OutputDirectory, _options.OutputFilePrefix, _startTime);
        
        // Stats 디렉토리 생성
        if (_options.EnableFileOutput)
        {
            EnsureDirectoryExists(_options.OutputDirectory);
        }
    }

    /// <summary>
    /// 파일 경로 생성 (Stats/prefix_yyyy-MM-dd_HH-mm-ss.log)
    /// </summary>
    private static string GenerateFilePath(string directory, string prefix, DateTime timestamp)
    {
        string fileName = $"{prefix}_{timestamp:yyyy-MM-dd_HH-mm-ss}.log";
        return Path.Combine(directory, fileName);
    }

    /// <summary>
    /// 디렉토리가 없으면 생성
    /// </summary>
    private static void EnsureDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 현재 출력 파일 경로 반환
    /// </summary>
    public string OutputFilePath => _filePath;

    /// <summary>
    /// 시작 시간 반환
    /// </summary>
    public DateTime StartTime => _startTime;

    /// <summary>
    /// Latency 샘플 기록
    /// </summary>
    public void RecordSample(long requestId, long clientSendTs, long serverRecvTs, long serverSendTs, long clientRecvTs)
    {
        var sample = new LatencySample
        {
            RequestId = requestId,
            Timestamp = DateTime.UtcNow,
            ClientSendTs = clientSendTs,
            ServerRecvTs = serverRecvTs,
            ServerSendTs = serverSendTs,
            ClientRecvTs = clientRecvTs
        };

        _samples.Enqueue(sample);
        Interlocked.Increment(ref _totalSamples);

        // 최대 샘플 수 초과 시 오래된 것 제거
        while (_samples.Count > _maxSamples)
        {
            _samples.TryDequeue(out _);
        }

        // 콘솔 로그 출력
        if (_options.EnableConsoleOutput && _logger != null)
        {
            var stats = sample.Calculate();
            _logger.LogInformation(
                "Latency [ReqId:{RequestId}] RTT:{Rtt:F3}ms, ServerProc:{ServerProc:F3}ms, Network:{Network:F3}ms",
                requestId, stats.RttMs, stats.ServerProcessingMs, stats.NetworkLatencyMs);
        }

        // 파일 출력 (주기적)
        if (_options.EnableFileOutput)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastFileWrite).TotalSeconds >= _options.FileWriteIntervalSeconds)
            {
                _lastFileWrite = now;
                Task.Run(() => WriteToFileAsync());
            }
        }
    }

    /// <summary>
    /// 서버 측에서 사용: T2 (수신) 기록
    /// </summary>
    public static long RecordServerReceive() => Stopwatch.GetTimestamp();

    /// <summary>
    /// 서버 측에서 사용: T3 (송신) 기록
    /// </summary>
    public static long RecordServerSend() => Stopwatch.GetTimestamp();

    /// <summary>
    /// 현재 통계 요약 반환
    /// </summary>
    public LatencyStatsSummary GetSummary()
    {
        var samples = _samples.ToArray();
        if (samples.Length == 0)
        {
            return new LatencyStatsSummary
            {
                StartTime = _startTime,
                EndTime = DateTime.Now
            };
        }

        var calculations = samples.Select(s => s.Calculate()).ToArray();
        var rtts = calculations.Select(c => c.RttMs).OrderBy(x => x).ToArray();
        var serverProcs = calculations.Select(c => c.ServerProcessingMs).OrderBy(x => x).ToArray();
        var networks = calculations.Select(c => c.NetworkLatencyMs).OrderBy(x => x).ToArray();

        return new LatencyStatsSummary
        {
            StartTime = _startTime,
            EndTime = DateTime.Now,
            TotalSamples = _totalSamples,
            SamplesInMemory = samples.Length,
            
            // RTT 통계
            RttMin = rtts.First(),
            RttMax = rtts.Last(),
            RttAvg = rtts.Average(),
            RttP50 = GetPercentile(rtts, 50),
            RttP95 = GetPercentile(rtts, 95),
            RttP99 = GetPercentile(rtts, 99),
            
            // Server Processing 통계
            ServerProcMin = serverProcs.First(),
            ServerProcMax = serverProcs.Last(),
            ServerProcAvg = serverProcs.Average(),
            
            // Network Latency 통계
            NetworkMin = networks.First(),
            NetworkMax = networks.Last(),
            NetworkAvg = networks.Average()
        };
    }

    /// <summary>
    /// 통계 문자열 반환 (엔드포인트용)
    /// </summary>
    public string GetSummaryString()
    {
        var summary = GetSummary();
        var duration = summary.EndTime - summary.StartTime;
        var sb = new StringBuilder();
        
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("                    LATENCY STATISTICS                      ");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  Start Time: {summary.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  End Time:   {summary.EndTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Duration:   {duration:hh\\:mm\\:ss}");
        sb.AppendLine($"  Total Samples: {summary.TotalSamples:N0}");
        sb.AppendLine($"  Samples in Memory: {summary.SamplesInMemory:N0}");
        sb.AppendLine("───────────────────────────────────────────────────────────");
        sb.AppendLine("  RTT (Round-Trip Time):");
        sb.AppendLine($"    Min: {summary.RttMin:F3} ms");
        sb.AppendLine($"    Max: {summary.RttMax:F3} ms");
        sb.AppendLine($"    Avg: {summary.RttAvg:F3} ms");
        sb.AppendLine($"    P50: {summary.RttP50:F3} ms");
        sb.AppendLine($"    P95: {summary.RttP95:F3} ms");
        sb.AppendLine($"    P99: {summary.RttP99:F3} ms");
        sb.AppendLine("───────────────────────────────────────────────────────────");
        sb.AppendLine("  Server Processing Time:");
        sb.AppendLine($"    Min: {summary.ServerProcMin:F3} ms");
        sb.AppendLine($"    Max: {summary.ServerProcMax:F3} ms");
        sb.AppendLine($"    Avg: {summary.ServerProcAvg:F3} ms");
        sb.AppendLine("───────────────────────────────────────────────────────────");
        sb.AppendLine("  Network Latency (estimated):");
        sb.AppendLine($"    Min: {summary.NetworkMin:F3} ms");
        sb.AppendLine($"    Max: {summary.NetworkMax:F3} ms");
        sb.AppendLine($"    Avg: {summary.NetworkAvg:F3} ms");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        
        if (_options.EnableFileOutput)
        {
            sb.AppendLine($"  Output File: {_filePath}");
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// JSON 형식으로 통계 반환 (API 엔드포인트용)
    /// </summary>
    public string GetSummaryJson()
    {
        var summary = GetSummary();
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }

    /// <summary>
    /// 통계 초기화
    /// </summary>
    public void Clear()
    {
        while (_samples.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _totalSamples, 0);
    }

    /// <summary>
    /// 즉시 파일에 통계 저장
    /// </summary>
    public async Task SaveToFileAsync()
    {
        if (!_options.EnableFileOutput) return;
        
        EnsureDirectoryExists(_options.OutputDirectory);
        await WriteToFileAsync();
    }

    private async Task WriteToFileAsync()
    {
        try
        {
            var summary = GetSummaryString();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var content = $"[{timestamp}]\n{summary}\n\n";
            
            await File.AppendAllTextAsync(_filePath, content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write latency stats to file: {FilePath}", _filePath);
        }
    }

    private static double GetPercentile(double[] sortedData, int percentile)
    {
        if (sortedData.Length == 0) return 0;
        
        double index = (percentile / 100.0) * (sortedData.Length - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        
        if (lower == upper) return sortedData[lower];
        
        return sortedData[lower] + (sortedData[upper] - sortedData[lower]) * (index - lower);
    }
}

/// <summary>
/// Latency 통계 옵션
/// </summary>
public class LatencyStatsOptions
{
    /// <summary>
    /// 콘솔 로그 출력 여부
    /// </summary>
    public bool EnableConsoleOutput { get; set; } = true;
    
    /// <summary>
    /// 파일 출력 여부
    /// </summary>
    public bool EnableFileOutput { get; set; } = false;
    
    /// <summary>
    /// 출력 디렉토리 (기본: Stats)
    /// </summary>
    public string OutputDirectory { get; set; } = "Stats";
    
    /// <summary>
    /// 출력 파일 접두사 (기본: latency_stats)
    /// 최종 파일명: {prefix}_{yyyy-MM-dd_HH-mm-ss}.log
    /// </summary>
    public string OutputFilePrefix { get; set; } = "latency_stats";
    
    /// <summary>
    /// 파일 쓰기 간격 (초)
    /// </summary>
    public int FileWriteIntervalSeconds { get; set; } = 60;
    
    /// <summary>
    /// 메모리에 보관할 최대 샘플 수
    /// </summary>
    public int MaxSamplesInMemory { get; set; } = 10000;
}

/// <summary>
/// 개별 Latency 샘플
/// </summary>
public struct LatencySample
{
    public long RequestId { get; set; }
    public DateTime Timestamp { get; set; }
    public long ClientSendTs { get; set; }   // T1
    public long ServerRecvTs { get; set; }   // T2
    public long ServerSendTs { get; set; }   // T3
    public long ClientRecvTs { get; set; }   // T4

    public readonly LatencyCalculation Calculate()
    {
        // Stopwatch ticks to milliseconds
        double ticksPerMs = Stopwatch.Frequency / 1000.0;
        
        double rtt = (ClientRecvTs - ClientSendTs) / ticksPerMs;
        double serverProc = (ServerSendTs - ServerRecvTs) / ticksPerMs;
        double network = rtt - serverProc;

        return new LatencyCalculation
        {
            RttMs = rtt,
            ServerProcessingMs = serverProc,
            NetworkLatencyMs = Math.Max(0, network) // 음수 방지
        };
    }
}

/// <summary>
/// Latency 계산 결과
/// </summary>
public struct LatencyCalculation
{
    public double RttMs { get; set; }
    public double ServerProcessingMs { get; set; }
    public double NetworkLatencyMs { get; set; }
}

/// <summary>
/// Latency 통계 요약
/// </summary>
public class LatencyStatsSummary
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long TotalSamples { get; set; }
    public int SamplesInMemory { get; set; }
    
    // RTT
    public double RttMin { get; set; }
    public double RttMax { get; set; }
    public double RttAvg { get; set; }
    public double RttP50 { get; set; }
    public double RttP95 { get; set; }
    public double RttP99 { get; set; }
    
    // Server Processing
    public double ServerProcMin { get; set; }
    public double ServerProcMax { get; set; }
    public double ServerProcAvg { get; set; }
    
    // Network
    public double NetworkMin { get; set; }
    public double NetworkMax { get; set; }
    public double NetworkAvg { get; set; }
}
