using System.Globalization;
using System.Text.Json;
using PS2000Test.PowerSupply;

// The only process that talks to the PSU hardware: mirrors its state to MQTT and applies
// commands arriving from the UI. Usage: PsuAgent <broker-host> (--dummy | <serial-port>)
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PS2000Test.PsuAgent <broker-host> (--dummy | <serial-port>)");
    return 1;
}

using IPowerSupply device = args[1] == "--dummy"
    ? PowerSupplyFactory.CreateDummy()
    : PowerSupplyFactory.Create(args[1]);

var info = new PsuInfo(
    await device.GetDeviceTypeAsync(),
    await device.GetSerialNumberAsync(),
    await device.GetArticleNumberAsync(),
    await device.GetNominalVoltageAsync());
var topics = new PsuMqttTopics(info.SerialNumber);

using var mqtt = new PsuMqttConnection(args[0]);
mqtt.MessageReceived += (topic, payload) => _ = HandleCommandAsync(topic, payload);
mqtt.Connect(willTopic: topics.Online, willPayload: "0");
mqtt.Subscribe(topics.CommandVoltage);
mqtt.Subscribe(topics.CommandOutput);
mqtt.Subscribe(topics.CommandRemote);

mqtt.Publish(topics.Info, JsonSerializer.Serialize(info), retain: true);
mqtt.Publish(topics.Online, "1", retain: true);
Console.WriteLine($"Publishing {info.DeviceType} (S/N {info.SerialNumber}) to {args[0]}. Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Same cadence as the UI's own polling loop. Errors are retried next tick (flaky USB).
while (!cts.IsCancellationRequested)
{
    try
    {
        mqtt.Publish(topics.Status, JsonSerializer.Serialize(await device.GetStatusAsync()), retain: true);
        await Task.Delay(300, cts.Token);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Status read failed: {ex.Message}");
    }
}

// A clean disconnect does not trigger the last will, so say goodbye explicitly.
mqtt.Publish(topics.Online, "0", retain: true);
return 0;

async Task HandleCommandAsync(string topic, string payload)
{
    try
    {
        if (topic == topics.CommandVoltage)
        {
            await device.SetVoltageAsync(double.Parse(payload, CultureInfo.InvariantCulture));
        }
        else if (topic == topics.CommandOutput)
        {
            await device.SetPowerOutputAsync(payload == "1");
        }
        else if (topic == topics.CommandRemote)
        {
            await device.SetRemoteControlAsync(payload == "1");
        }

        Console.WriteLine($"{topic} = {payload}");
    }
    catch (Exception ex)
    {
        // Payloads come from the network and the device from hardware - neither may kill the agent.
        Console.Error.WriteLine($"Command {topic} = {payload} failed: {ex.Message}");
    }
}
