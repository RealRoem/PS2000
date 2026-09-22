using System.IO.Ports;
using System.Text;

namespace PS2000Test.Ui.Services;

/// <summary>Live status snapshot read from the device (OBJ 71 / STATUS_ACTUAL_VALUES).</summary>
public readonly record struct DeviceStatus(bool RemoteControlActive, bool OutputActive, double VoltagePercent);

/// <summary>
/// Binary telegram client for the PS2000B bench power supply.
///
/// Telegram shape: SD, DN, OBJ, [DATA...], checksumHigh, checksumLow (checksum = sum of every
/// preceding byte). The SD (start delimiter) byte packs: bits 0-3 a length nibble, bit 4
/// direction, bit 5 cast type, bits 6-7 transmission type (01 = query, 11 = send/control) -
/// see BuildQueryTelegram/BuildSendTelegram.
///
/// Object numbers, control bit positions, serial settings (115200 8-O-1) and the telegram
/// framing below are taken from the official "PS 2000B Programming Guide" / "PS 2000B object
/// list" (via the reference implementation at github.com/ssproessig/Python-PS2000B, which
/// cites both documents directly) - not guessed. The one exception is the SD byte used for
/// SetVoltageAsync, which is ported from the original exploratory console app rather than
/// independently confirmed against the object list; flagged below.
/// </summary>
public sealed class Ps2000Client : IDisposable
{
    private readonly string _portName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SerialPort? _port;

    private const byte DeviceNode = 0x00;

    private const byte ObjDeviceType = 0;
    private const byte ObjSerialNumber = 1;
    private const byte ObjNominalVoltage = 2;
    private const byte ObjArticleNumber = 6;
    private const byte ObjControl = 54; // 0x36
    private const byte ObjStatus = 71;  // 0x47
    private const byte ObjSetVoltage = 0x32; // ported from the original app - not independently confirmed

    private const byte ControlModeParam = 0x10;  // p1: "remote control" switch
    private const byte ControlModeRemote = 0x10; // p2: remote control on
    private const byte ControlModeManual = 0x00; // p2: remote control off

    private const byte ControlOutputParam = 0x01; // p1: "power output" switch
    private const byte ControlOutputOn = 0x01;    // p2: output on
    private const byte ControlOutputOff = 0x00;   // p2: output off

    public Ps2000Client(IConfiguration configuration)
    {
        _portName = configuration["Ps2000:PortName"] ?? "/dev/tty.usbmodem26865804071";
    }

    public Task<string> GetDeviceTypeAsync() => GetIdentityStringAsync(ObjDeviceType);

    public Task<string> GetSerialNumberAsync() => GetIdentityStringAsync(ObjSerialNumber);

    public Task<string> GetArticleNumberAsync() => GetIdentityStringAsync(ObjArticleNumber);

    public async Task<double> GetNominalVoltageAsync()
    {
        var response = await SendAsync(BuildQueryTelegram(ObjNominalVoltage, expectedLength: 4));
        return ParseFloat(response);
    }

    public async Task<DeviceStatus> GetStatusAsync()
    {
        var response = await SendAsync(BuildQueryTelegram(ObjStatus, expectedLength: 6));
        byte[] data = GetData(response);

        bool remoteActive = (data[0] & 0b1) != 0;
        bool outputActive = (data[1] & 0b1) != 0;
        int word = (data[2] << 8) | data[3];
        return new DeviceStatus(remoteActive, outputActive, word / 256.0);
    }

    public async Task<double> GetVoltageAsync()
    {
        double nominal = await GetNominalVoltageAsync();
        var status = await GetStatusAsync();
        return nominal * status.VoltagePercent / 100.0;
    }

    public async Task SetVoltageAsync(double volts)
    {
        double nominal = await GetNominalVoltageAsync();
        int percent = (int)Math.Round(25600 * volts / nominal);
        byte[] data = { DeviceNode, ObjSetVoltage, (byte)(percent >> 8), (byte)(percent & 0xFF) };
        var response = await SendAsync(BuildSendTelegram(data, payloadLength: 3));
        ThrowIfError(response);
    }

    public Task SetRemoteControlAsync(bool enabled) =>
        SendControlAsync(ControlModeParam, enabled ? ControlModeRemote : ControlModeManual);

    public Task SetPowerOutputAsync(bool enabled) =>
        SendControlAsync(ControlOutputParam, enabled ? ControlOutputOn : ControlOutputOff);

    private async Task SendControlAsync(byte p1, byte p2)
    {
        byte[] data = { DeviceNode, ObjControl, p1, p2 };
        var response = await SendAsync(BuildSendTelegram(data, payloadLength: 2));
        ThrowIfError(response);
    }

    private async Task<string> GetIdentityStringAsync(byte obj)
    {
        var response = await SendAsync(BuildQueryTelegram(obj, expectedLength: 16));
        byte[] data = GetData(response);
        return Encoding.ASCII.GetString(data).TrimEnd('\0', ' ');
    }

    private static void ThrowIfError(List<byte> response)
    {
        byte[] data = GetData(response);
        if (data.Length > 0 && data[0] != 0)
        {
            throw new InvalidOperationException($"Device reported error code {data[0]}.");
        }
    }

    /// <summary>Builds a query telegram (transmission = 01). The length nibble encodes the
    /// expected response length, not the outgoing payload (queries only ever send DN+OBJ).</summary>
    private static byte[] BuildQueryTelegram(byte obj, int expectedLength)
    {
        byte sd = (byte)(0x70 | ((expectedLength - 1) & 0x0F));
        byte[] telegram = { sd, DeviceNode, obj, 0x00, 0x00 };
        AppendChecksum(telegram, headerLength: 3);
        return telegram;
    }

    /// <summary>Builds a send/control telegram (transmission = 11). <paramref name="data"/> is
    /// [DeviceNode, OBJ, ...parameters]; the length nibble encodes the parameter byte count.</summary>
    private static byte[] BuildSendTelegram(byte[] data, int payloadLength)
    {
        byte sd = (byte)(0xF0 | ((payloadLength - 1) & 0x0F));
        var telegram = new byte[1 + data.Length + 2];
        telegram[0] = sd;
        Array.Copy(data, 0, telegram, 1, data.Length);
        AppendChecksum(telegram, headerLength: 1 + data.Length);
        return telegram;
    }

    private static void AppendChecksum(byte[] telegram, int headerLength)
    {
        int sum = 0;
        for (int i = 0; i < headerLength; i++)
        {
            sum += telegram[i];
        }

        telegram[^2] = (byte)(sum >> 8);
        telegram[^1] = (byte)(sum & 0xFF);
    }

    /// <summary>Strips SD, DN, OBJ and the trailing 2-byte checksum, returning whatever data
    /// bytes the device actually sent back (not assumed from the request).</summary>
    private static byte[] GetData(List<byte> response)
    {
        int dataLength = response.Count - 3 - 2;
        if (dataLength <= 0)
        {
            return Array.Empty<byte>();
        }

        return response.GetRange(3, dataLength).ToArray();
    }

    /// <summary>
    /// The port is opened once and kept open for the lifetime of this (singleton) client,
    /// rather than reopened per telegram - opening/closing 5-7 times in a row (once per
    /// startup query) was slow and, under Blazor Server's double render (a static prerender
    /// immediately followed by the real interactive render, each running its own
    /// initialization), raced for the handle and surfaced as "Access to the port is denied".
    /// The semaphore serializes all port access so concurrent calls queue instead of colliding.
    /// </summary>
    private async Task<List<byte>> SendAsync(byte[] telegram)
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var port = GetOpenPort();
                    port.DiscardInBuffer();
                    port.Write(telegram, 0, telegram.Length);
                    Thread.Sleep(100);

                    var response = new List<byte>();
                    int length = port.BytesToRead;
                    if (length > 0)
                    {
                        var buffer = new byte[length];
                        port.Read(buffer, 0, length);
                        response.AddRange(buffer);
                    }

                    return response;
                }
                catch
                {
                    // The port can end up in a faulted state after a failed read/write (e.g. a
                    // timeout on flaky USB hardware) without IsOpen reflecting it, so the next
                    // call would just fail again with "port is closed". Drop it so the next
                    // SendAsync opens a fresh one instead of reusing a dead handle.
                    _port?.Dispose();
                    _port = null;
                    throw;
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    private SerialPort GetOpenPort()
    {
        if (_port is { IsOpen: true })
        {
            return _port;
        }

        _port?.Dispose();
        // 115200 baud, odd parity, 8 data bits, 1 stop bit - per the PS2000B programming guide.
        _port = new SerialPort(_portName, 115200, Parity.Odd, 8, StopBits.One)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
        _port.Open();
        return _port;
    }

    public void Dispose()
    {
        _port?.Dispose();
        _lock.Dispose();
    }

    private static double ParseFloat(List<byte> response)
    {
        byte[] data = GetData(response);
        // Device sends big-endian; BitConverter wants little-endian on this platform.
        byte[] bytes = { data[3], data[2], data[1], data[0] };
        return BitConverter.ToSingle(bytes, 0);
    }
}
