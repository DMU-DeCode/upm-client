using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using UPM.Models;

namespace UPM.Core;

/// <summary>
/// 모바일 앱(UPM_Mobile) 제어 API를 수신하는 로컬 HTTP 서버입니다.
/// UPM_Server(8000)가 <c>/shutdown</c>, <c>/optimize</c> 요청을 이 포트로 전달합니다.
/// </summary>
public sealed class MobileControlHttpServer : IDisposable
{
    private HttpListener _listener;
    private readonly HardwareMonitor _monitor;
    private readonly int _port;
    private readonly Func<Task<int>>? _optimizeHandler;
    private readonly Action? _showWindowHandler;
    private readonly Func<SystemStatusModel?>? _metricsProvider;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    /// <param name="optimizeHandler">
    /// 모바일의 최적화 요청 때 실행할 동작. 주면 PC의 "빠른 최적화" 버튼과 같은 경로(서버 분석 → 종료)를 쓰고,
    /// 비워 두면 기존처럼 <see cref="HardwareMonitor.OptimizeSystem"/> 을 실행합니다.
    /// </param>
    /// <param name="showWindowHandler">UPM 메인 창을 화면 맨 앞으로 띄우는 액션.</param>
    /// <param name="metricsProvider">스트림덱 등 외부 연동에 제공할 최신 실시간 메트릭.</param>
    public MobileControlHttpServer(
        HardwareMonitor monitor,
        int port = UpmAppSettings.MobileControlPort,
        Func<Task<int>>? optimizeHandler = null,
        Action? showWindowHandler = null,
        Func<SystemStatusModel?>? metricsProvider = null)
    {
        _monitor = monitor;
        _port = port;
        _optimizeHandler = optimizeHandler;
        _showWindowHandler = showWindowHandler;
        _metricsProvider = metricsProvider;
        _listener = new HttpListener();
    }

    public void Start()
    {
        if (_listenTask != null)
            return;

        // 관리자 권한 실행 시 외부(라즈베리파이 UPM_Server 등)에서도 접속 가능하도록 http://+:port/ 바인딩 시도,
        // 권한 부족 등으로 실패하면 로컬(127.0.0.1) 전용으로 폴백합니다.
        try
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();
            Debug.WriteLine($"[UPM] 모바일 제어 서버 시작(전체 인터페이스): http://+:{_port}/");
        }
        catch (HttpListenerException)
        {
            try { _listener.Close(); } catch { /* ignore */ }
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            Debug.WriteLine($"[UPM] 모바일 제어 서버 시작(로컬 전용 폴백): http://127.0.0.1:{_port}/");
        }

        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPM] 제어 서버 수신 오류: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // 취소된 경우 응답 없이 조용히 종료
            if (cancellationToken.IsCancellationRequested)
            {
                response.Abort();
                return;
            }

            // 로컬 웹뷰(Ulanzi Studio / StreamDeck 등 file:// 또는 로컬 오리진)의 CORS 허용
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AppendHeader("Access-Control-Allow-Headers", "*");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                return;
            }

            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

            if (request.HttpMethod == "POST" &&
                path.Equals("/shutdown", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[UPM] 모바일 종료 신호 수신");
                SystemPowerController.InitiateShutdown();
                await WriteJsonAsync(response, HttpStatusCode.OK, new
                {
                    status = "success",
                    message = "PC 종료를 시작합니다.",
                });
                return;
            }

            if (request.HttpMethod == "POST" &&
                path.Equals("/restart", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[UPM] 모바일 재시작 신호 수신");
                SystemPowerController.InitiateRestart();
                await WriteJsonAsync(response, HttpStatusCode.OK, new
                {
                    status = "success",
                    message = "PC 재시작을 시작합니다.",
                });
                return;
            }

            if ((request.HttpMethod == "POST" || request.HttpMethod == "GET") &&
                path.Equals("/optimize", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[UPM] 최적화 신호 수신");
                var cleared = _optimizeHandler != null
                    ? await _optimizeHandler()
                    : _monitor.OptimizeSystem();
                await WriteJsonAsync(response, HttpStatusCode.OK, new
                {
                    status = "success",
                    clearedProcesses = cleared,
                });
                return;
            }

            if ((request.HttpMethod == "POST" || request.HttpMethod == "GET") &&
                path.Equals("/show", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[UPM] 창 팝업 신호 수신");
                _showWindowHandler?.Invoke();
                await WriteJsonAsync(response, HttpStatusCode.OK, new
                {
                    status = "success",
                    message = "UPM 창을 표시합니다.",
                });
                return;
            }

            if (request.HttpMethod == "GET" &&
                path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                var status = _metricsProvider?.Invoke();
                if (status != null)
                {
                    await WriteJsonAsync(response, HttpStatusCode.OK, new
                    {
                        status = "success",
                        cpu = status.CpuUsagePercent,
                        ram = status.RamUsagePercent,
                        gpu = status.GpuUsagePercent,
                        gpuTemp = status.GpuTemperatureCelsius
                    });
                }
                else
                {
                    await WriteJsonAsync(response, HttpStatusCode.ServiceUnavailable, new
                    {
                        status = "error",
                        message = "아직 메트릭이 수집되지 않았습니다."
                    });
                }
                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 제어 요청 처리 실패: {ex.Message}");
            try
            {
                await WriteJsonAsync(response, HttpStatusCode.InternalServerError, new
                {
                    status = "error",
                    message = ex.Message,
                });
            }
            catch
            {
                // 응답 전송 자체가 실패(스트림 파괴 등)한 경우 무시
            }
        }
        finally
        {
            try { response.Close(); } catch { /* 이미 닫혔거나 파괴된 경우 무시 */ }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode status, object body)
    {
        response.StatusCode = (int)status;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        // 1. 먼저 루프를 취소하여 새 요청 수락 중단
        _cts.Cancel();

        // 2. 블로킹된 GetContextAsync 를 해제하기 위해 Stop() 호출
        try
        {
            if (_listener.IsListening)
                _listener.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 서버 Stop 실패: {ex.Message}");
        }

        // 3. 리스닝 루프가 완전히 종료될 때까지 대기 (최대 2초)
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // AggregateException, TaskCanceledException 등 무시
        }

        // 4. 리소스 해제
        try { _listener.Close(); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
