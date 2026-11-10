using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace UPM.Core;

/// <summary>스트림덱 MQTT 트리거 설정 (%LocalAppData%/UPM/mqtt.json, 없으면 기본값).</summary>
public sealed class MqttTriggerSettings
{
    public bool Enabled { get; set; } = true;
    public string BrokerHost { get; set; } = UpmAppSettings.MqttBrokerHost;
    public int BrokerPort { get; set; } = UpmAppSettings.MqttBrokerPort;
    public string Topic { get; set; } = UpmAppSettings.MqttBoostTopic;
    public string Payload { get; set; } = UpmAppSettings.MqttBoostPayload;
    public string? Username { get; set; }
    public string? Password { get; set; }

    public static MqttTriggerSettings Load()
    {
        try
        {
            if (File.Exists(UpmAppSettings.MqttSettingsFilePath))
            {
                var json = File.ReadAllText(UpmAppSettings.MqttSettingsFilePath);
                var loaded = JsonSerializer.Deserialize<MqttTriggerSettings>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null)
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] mqtt.json 로드 실패, 기본값 사용: {ex.Message}");
        }
        return new MqttTriggerSettings();
    }
}

/// <summary>
/// 라즈베리파이 MQTT 브로커의 <c>upm/control/boost</c> 토픽을 구독하여,
/// 스트림덱 버튼 신호가 오면 빠른 최적화(모바일 최적화와 같은 경로)를 실행합니다.
/// 브로커가 꺼져 있어도 앱 동작에는 영향이 없으며, 백그라운드에서 주기적으로 재연결합니다.
/// </summary>
public sealed class MqttBoostTrigger : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    private readonly MqttTriggerSettings _settings;
    private readonly Func<Task<int>> _optimizeHandler;
    private readonly IMqttClient _client;
    private readonly CancellationTokenSource _cts = new();
    private Task? _connectLoop;

    // 연타/QoS1 중복 수신 시 최적화가 겹쳐 실행되지 않도록 방지
    private int _isRunning;
    private DateTime _lastRunUtc = DateTime.MinValue;

    /// <summary>최적화가 끝났을 때 (종료한 프로세스 수). UI 알림용, 백그라운드 스레드에서 호출됩니다.</summary>
    public event Action<int>? BoostCompleted;

    public MqttBoostTrigger(Func<Task<int>> optimizeHandler, MqttTriggerSettings? settings = null)
    {
        _optimizeHandler = optimizeHandler ?? throw new ArgumentNullException(nameof(optimizeHandler));
        _settings = settings ?? MqttTriggerSettings.Load();
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    public void Start()
    {
        if (!_settings.Enabled || _connectLoop != null)
            return;
        _connectLoop = Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    /// <summary>연결이 끊기면 일정 간격으로 재연결 + 재구독합니다.</summary>
    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
            .WithClientId($"UPM_Desktop_{Environment.MachineName}")
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(5));
        if (!string.IsNullOrEmpty(_settings.Username))
            builder = builder.WithCredentials(_settings.Username, _settings.Password);
        var options = builder.Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(options, ct);
                    await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f
                            .WithTopic(_settings.Topic)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                        .Build(), ct);
                    Debug.WriteLine($"[UPM] MQTT 연결/구독 완료: {_settings.BrokerHost}:{_settings.BrokerPort} '{_settings.Topic}'");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPM] MQTT 연결 실패 ({ReconnectDelay.TotalSeconds}초 후 재시도): {ex.Message}");
            }

            try { await Task.Delay(ReconnectDelay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment).Trim();
        if (!string.Equals(payload, _settings.Payload, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[UPM] MQTT 알 수 없는 페이로드 무시: '{payload}'");
            return;
        }

        if (DateTime.UtcNow - _lastRunUtc < Cooldown)
        {
            Debug.WriteLine("[UPM] MQTT 부스트 쿨다운 중 - 무시");
            return;
        }
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            Debug.WriteLine("[UPM] MQTT 부스트 이미 실행 중 - 무시");
            return;
        }

        try
        {
            Debug.WriteLine("[UPM] 스트림덱 부스트 신호 수신 → 빠른 최적화 실행");
            var cleared = await _optimizeHandler();
            BoostCompleted?.Invoke(cleared);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] MQTT 부스트 실행 실패: {ex.Message}");
        }
        finally
        {
            _lastRunUtc = DateTime.UtcNow;
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_client.IsConnected)
                _client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* 종료 중 오류 무시 */ }
        try { _connectLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _client.Dispose();
        _cts.Dispose();
    }
}

