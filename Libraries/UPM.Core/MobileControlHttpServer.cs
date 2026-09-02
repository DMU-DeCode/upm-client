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
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
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

    private async Task HandleRequestAsync(HttpListenerContext context) {
        var request = context.Request;
        var response = context.Response;

        try {
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
            await WriteJsonAsync(response, HttpStatusCode.InternalServerError, new {
                status = "error",
                message = ex.Message,
            });
        } finally {
            response.Close();
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
        _cts.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
        _listener.Close();
        _cts.Dispose();
    }
}
