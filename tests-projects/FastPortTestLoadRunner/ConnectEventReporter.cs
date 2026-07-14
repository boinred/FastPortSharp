using System.Net.Sockets;
using System.Text.Json;

namespace FastPortTestLoadRunner;

// 용도: 세션별 connect 결과를 실행 환경에 맞는 출력 대상으로 전달
internal interface IConnectEventSink
{
    // 용도: 단일 세션의 connect 완료/취소/실패 이벤트 기록
    void Record(ConnectSessionEvent connectEvent);
}

// 용도: connect 시도 1건의 시작/완료 시간과 endpoint, 실패 원인 보존
internal sealed record ConnectSessionEvent(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int SessionId,
    string Host,
    int Port,
    string Status,
    double DurationMs,
    string? LocalEndPoint,
    string? RemoteEndPoint,
    string? ExceptionType,
    string? SocketErrorCode);

// 용도: connect event를 JSONL 파일로 저장해 세션별 연결 지연과 실패 원인 분석
internal sealed class JsonConnectEventReporter : IConnectEventSink, IDisposable
{
    // 직렬화: metrics 파일과 같은 camelCase JSON 형식
    private static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web);

    // 동기화: 여러 session task가 하나의 JSONL writer에 동시에 기록되는 상황 보호
    private readonly object _syncRoot = new();

    // 출력: 세션별 connect 결과 JSONL 파일
    private readonly StreamWriter _writer;

    public JsonConnectEventReporter(string outputPath)
    {
        // 준비: 상위 디렉터리가 있으면 먼저 생성
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 파일: 실행마다 새 connect event 로그 생성
        _writer = new StreamWriter(File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            // 운영성: 테스트 실행 중에도 tail/jq로 connect event 확인 가능
            AutoFlush = true
        };
    }

    // 용도: 세션별 connect event를 한 줄 JSON으로 append
    public void Record(ConnectSessionEvent connectEvent)
    {
        // 직렬화: metrics JSONL과 같은 camelCase JSON 형식
        string json = JsonSerializer.Serialize(connectEvent, s_JsonOptions);
        lock (_syncRoot)
        {
            _writer.WriteLine(json);
        }
    }

    // 용도: 파일 핸들 정리
    public void Dispose()
    {
        _writer.Dispose();
    }
}

// 용도: connect 실패 예외를 JSONL 필드로 안정적으로 분류
internal static class ConnectEventExceptionClassifier
{
    // 용도: 최상위 예외 타입 기록
    public static string? GetExceptionType(Exception? exception)
    {
        return exception?.GetType().Name;
    }

    // 용도: SocketException이 포함된 실패에서 SocketErrorCode 추출
    public static string? GetSocketErrorCode(Exception? exception)
    {
        SocketException? socketException = FindSocketException(exception);
        return socketException?.SocketErrorCode.ToString();
    }

    // 용도: wrapper exception 체인 내부의 SocketException 탐색
    private static SocketException? FindSocketException(Exception? exception)
    {
        // 분류: wrapper exception 안쪽의 SocketException까지 탐색
        Exception? current = exception;
        while (current is not null)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }

            current = current.InnerException;
        }

        return null;
    }
}
