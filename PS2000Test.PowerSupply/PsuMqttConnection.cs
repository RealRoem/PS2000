using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace PS2000Test.PowerSupply;

/// <summary>
/// Thin wrapper over the course's M2Mqtt example (connect with a unique client id, subscribe,
/// publish at QoS 2) shared by the PSU agent and the UI-side <c>MqttPowerSupply</c>.
/// </summary>
public sealed class PsuMqttConnection(string brokerHost) : IDisposable
{
    private readonly MqttClient _client = new(brokerHost);

    /// <summary>Raised on a background thread with (topic, UTF-8 payload).</summary>
    public event Action<string, string>? MessageReceived;

    /// <summary>
    /// Connects with a unique client id. When a will is given the broker publishes it
    /// (retained) if this client disappears without disconnecting.
    /// </summary>
    public void Connect(string? willTopic = null, string? willPayload = null)
    {
        _client.MqttMsgPublishReceived += (_, e) =>
            MessageReceived?.Invoke(e.Topic, Encoding.UTF8.GetString(e.Message));

        string clientId = Guid.NewGuid().ToString();
        if (willTopic is null)
        {
            _client.Connect(clientId);
            return;
        }

        _client.Connect(clientId, null, null, true, MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, true,
            willTopic, willPayload ?? "", true, 60);
    }

    public void Subscribe(string topicFilter) =>
        _client.Subscribe([topicFilter], [MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE]);

    public void Publish(string topic, string payload, bool retain) =>
        _client.Publish(topic, Encoding.UTF8.GetBytes(payload), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, retain);

    public void Dispose()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }
    }
}
