using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace UPM.Core;

/// <summary>
/// 모바일 앱(UPM_Mobile) 제어 API를 수신하는 로컬 HTTP 서버입니다.
/// UPM_Server(8000)가 <c>/shutdown</c>, <c>/optimize</c> 요청을 이 포트로 전달합니다.
/// </summary>
public sealed class MobileControlHttpServer : IDisposable {
    private readonly HttpListener _listener;
    private readonly HardwareMonitor _monitor;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public MobileControlHttpServer(HardwareMonitor monitor, int port = UpmAppSettings.MobileControlPort) {
        _monitor = monitor;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start() {
        if (_listenTask != null)
            return;

        _listener.Start();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        Debug.WriteLine($"[UPM] 모바일 제어 서버 시작: http://127.0.0.1:{_port}/");
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            } catch (OperationCanceledException) {
                break;
            } catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) {
                break;
            } catch (ObjectDisposedException) {
                break;
            } catch (Exception ex) {
                Debug.WriteLine($"[UPM] 제어 서버 수신 오류: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var request = context.Request;
        var response = context.Response;

        try {
            // 취소된 경우 응답 없이 조용히 종료
            if (cancellationToken.IsCancellationRequested) {
                response.Abort();
                return;
            }

            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

            if (request.HttpMethod == "POST" &&
                path.Equals("/shutdown", StringComparison.OrdinalIgnoreCase)) {
                Debug.WriteLine("[UPM] 모바일 종료 신호 수신");
                SystemPowerController.InitiateShutdown();
                await WriteJsonAsync(response, HttpStatusCode.OK, new {
                    status = "success",
                    message = "PC 종료를 시작합니다.",
                });
                return;
            }

            if (request.HttpMethod == "POST" &&
                path.Equals("/optimize", StringComparison.OrdinalIgnoreCase)) {
                Debug.WriteLine("[UPM] 모바일 최적화 신호 수신");
                var cleared = _monitor.OptimizeSystem();
                await WriteJsonAsync(response, HttpStatusCode.OK, new {
                    status = "success",
                    clearedProcesses = cleared,
                });
                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] 제어 요청 처리 실패: {ex.Message}");
            try {
                await WriteJsonAsync(response, HttpStatusCode.InternalServerError, new {
                    status = "error",
                    message = ex.Message,
                });
            } catch {
                // 응답 전송 자체가 실패(스트림 파괴 등)한 경우 무시
            }
        } finally {
            try { response.Close(); } catch { /* 이미 닫혔거나 파괴된 경우 무시 */ }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode status, object body) {
        response.StatusCode = (int)status;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose() {
        // 1. 먼저 루프를 취소하여 새 요청 수락 중단
        _cts.Cancel();

        // 2. 블로킹된 GetContextAsync 를 해제하기 위해 Stop() 호출
        try {
            if (_listener.IsListening)
                _listener.Stop();
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] 서버 Stop 실패: {ex.Message}");
        }

        // 3. 리스닝 루프가 완전히 종료될 때까지 대기 (최대 2초)
        try {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        } catch {
            // AggregateException, TaskCanceledException 등 무시
        }

        // 4. 리소스 해제
        try { _listener.Close(); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
