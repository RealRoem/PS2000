using System.Globalization;
using System.Text.Json;

namespace PS2000Test.PowerSupply;

/// <summary>
/// <see cref="IPowerSupply"/> for a PSU that sits behind an MQTT broker (see the PSU agent).
/// Getters return the last retained <c>info</c>/<c>status</c> message and setters publish a
/// command, so no request/response correlation is needed.
/// </summary>
internal sealed class MqttPowerSupply : IPowerSupply
{
    private static readonly TimeSpan FirstMessageTimeout = TimeSpan.FromSeconds(5);

    private readonly PsuMqttTopics _topics;
    private readonly PsuMqttConnection _mqtt;
    private readonly TaskCompletionSource<PsuInfo> _info = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<DeviceStatus> _firstStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private DeviceStatus _status;
    private bool _online;

    public MqttPowerSupply(string brokerHost, string psuId)
    {
        _topics = new PsuMqttTopics(psuId);
        _mqtt = new PsuMqttConnection(brokerHost);
        _mqtt.MessageReceived += OnMessage;
        _mqtt.Connect();
        _mqtt.Subscribe(_topics.Info);
        _mqtt.Subscribe(_topics.Status);
        _mqtt.Subscribe(_topics.Online);
    }

    public async Task<string> GetDeviceTypeAsync() => (await WaitForAsync(_info.Task, "info")).DeviceType;

    public async Task<string> GetSerialNumberAsync() => (await WaitForAsync(_info.Task, "info")).SerialNumber;

    public async Task<string> GetArticleNumberAsync() => (await WaitForAsync(_info.Task, "info")).ArticleNumber;

    public async Task<double> GetNominalVoltageAsync() => (await WaitForAsync(_info.Task, "info")).NominalVoltage;

    public async Task<DeviceStatus> GetStatusAsync()
    {
        await WaitForAsync(_firstStatus.Task, "status");
        lock (_gate)
        {
            // A retained status outlives a dead agent; the agent's last will flips this flag.
            return _online ? _status : throw new InvalidOperationException("The PSU agent is offline.");
        }
    }

    public Task SetVoltageAsync(double volts) =>
        Command(_topics.CommandVoltage, volts.ToString("R", CultureInfo.InvariantCulture));

    public Task SetRemoteControlAsync(bool enabled) => Command(_topics.CommandRemote, enabled ? "1" : "0");

    public Task SetPowerOutputAsync(bool enabled) => Command(_topics.CommandOutput, enabled ? "1" : "0");

    public void Dispose() => _mqtt.Dispose();

    private Task Command(string topic, string payload)
    {
        _mqtt.Publish(topic, payload, retain: false);
        return Task.CompletedTask;
    }

    private void OnMessage(string topic, string payload)
    {
        try
        {
            if (topic == _topics.Info)
            {
                _info.TrySetResult(JsonSerializer.Deserialize<PsuInfo>(payload)!);
            }
            else if (topic == _topics.Status)
            {
                var status = JsonSerializer.Deserialize<DeviceStatus>(payload);
                lock (_gate)
                {
                    _status = status;
                }

                _firstStatus.TrySetResult(status);
            }
            else if (topic == _topics.Online)
            {
                lock (_gate)
                {
                    _online = payload == "1";
                }
            }
        }
        catch (JsonException)
        {
            // Anyone on the broker can publish to these topics (e.g. by hand from MQTT Explorer);
            // a bad payload must not take down the client's receive thread.
        }
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task, string what)
    {
        try
        {
            return await task.WaitAsync(FirstMessageTimeout);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"No {what} from the PSU agent - is it running, and is the PSU id correct?");
        }
    }
}
