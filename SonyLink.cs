using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using static SonyMute.Native;

namespace SonyMute;

// Sony MDR control channel over RFCOMM. Framing and ids from mos9527/SonyHeadphonesClient (libmdr).
// Never changes headset settings. Sends only the protocol info request, the log notifier enable and ACKs.
sealed unsafe class SonyLink : IDisposable
{
    static readonly (Guid Uuid, string Version)[] Services =
    [
        (new("956C7B26-D49A-4BA8-B03F-B17D393CB6E2"), "V2"),
        (new("96CC203E-5068-46AD-B32D-E316F5E069BA"), "V1"),
    ];

    const byte FrameStart = 0x3E, FrameEnd = 0x3C, FrameEscape = 0x3D;
    const byte TypeAck = 0x01, TypeMdr = 0x0C, TypeMdr2 = 0x0E;
    const byte ConnectGetProtocolInfo = 0x00, ConnectRetProtocolInfo = 0x01, LogSetStatus = 0xC4, LogNotifyParam = 0xC9;
    const byte LogTimeSeriesOperation = 0x01, Enable = 0x00;
    const uint MajorClassAudioVideo = 0x04;

    readonly string _nameFilter;
    readonly AutoResetEvent _wake = new(false);
    readonly Lock _gate = new();
    Socket? _socket;
    volatile bool _enabled, _disposed;
    int _lastRxSeq = -1;
    byte _txSeq;
    byte[] _lastRxPayload = [];

    // Both raised on the link thread.
    public event Action<string>? Status;
    public event Action<string>? HeadsetEvent;

    public Action<string>? Trace;

    public SonyLink(string nameFilter) => _nameFilter = nameFilter;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                lock (_gate) _socket?.Dispose();
            _wake.Set();
        }
    }

    public void Start() => new Thread(Loop) { IsBackground = true, Name = "SonyLink" }.Start();

    public void Dispose()
    {
        _disposed = true;
        Enabled = false;
    }

    void Loop()
    {
        int retryMs = 5000;
        while (!_disposed)
        {
            if (!_enabled)
            {
                Status?.Invoke("off");
                _wake.WaitOne();
                continue;
            }

            bool linked = false;
            try
            {
                linked = LinkAny();
            }
            catch (Exception e)
            {
                Trace?.Invoke("error: " + e.Message);
            }
            if (!linked && _enabled)
                Status?.Invoke("no Sony headset connected");

            _wake.WaitOne(linked ? 2000 : retryMs);
            retryMs = linked ? 5000 : Math.Min(retryMs * 2, 60000);
        }
    }

    bool LinkAny()
    {
        foreach (var (address, name) in ConnectedAudioDevices())
        {
            if (_nameFilter.Length > 0 && !name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var (uuid, version) in Services)
            {
                if (!_enabled)
                    return true;
                var socket = TryConnect(address, uuid);
                if (socket is null)
                    continue;
                Status?.Invoke($"connecting to {name}");
                Run(socket, name, version);
                return true;
            }
        }
        return false;
    }

    Socket? TryConnect(ulong address, Guid uuid)
    {
        var socket = new Socket((AddressFamily)AF_BTH, SocketType.Stream, (ProtocolType)BTHPROTO_RFCOMM);
        try
        {
            socket.Connect(new BluetoothEndPoint(address, uuid));
            return socket;
        }
        catch (SocketException e)
        {
            Trace?.Invoke($"{address:X12} {uuid}: {e.SocketErrorCode}");
            socket.Dispose();
            return null;
        }
    }

    void Run(Socket socket, string name, string version)
    {
        lock (_gate) _socket = socket;
        _lastRxSeq = -1;
        _txSeq = 0;
        try
        {
            SendCommand(socket, [ConnectGetProtocolInfo, 0x00]);
            var buffer = new byte[2048];
            var frame = new List<byte>(64);
            bool inFrame = false;
            int received;
            while ((received = socket.Receive(buffer)) > 0)
            {
                for (int i = 0; i < received; i++)
                {
                    byte b = buffer[i];
                    if (b == FrameStart) { frame.Clear(); inFrame = true; }
                    else if (!inFrame) continue;
                    else if (b == FrameEnd) { inFrame = false; Handle(socket, frame, name, version); }
                    else frame.Add(b);
                }
            }
            Trace?.Invoke("link closed by headset");
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Trace?.Invoke("link closed: " + e.Message);
        }
        finally
        {
            lock (_gate) _socket = null;
            socket.Dispose();
        }
    }

    void Handle(Socket socket, List<byte> escaped, string name, string version)
    {
        var data = Unescape(escaped);
        if (data is null || data.Length < 7
            || BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(2)) != data.Length - 7
            || Checksum(data.AsSpan(0, data.Length - 1)) != data[^1])
        {
            Trace?.Invoke("RX malformed frame " + Hex([.. escaped]));
            return;
        }

        byte type = data[0], seq = data[1];
        byte[] payload = data[6..^1];
        Trace?.Invoke($"RX type {type:X2} seq {seq}  {Hex(payload)}");

        if (type is TypeMdr or TypeMdr2)
        {
            Send(socket, TypeAck, (byte)(1 - seq), []);
            // same seq + payload again = resend (headset didn't see our ACK in time)
            bool resend = seq == _lastRxSeq && payload.AsSpan().SequenceEqual(_lastRxPayload);
            _lastRxSeq = seq;
            _lastRxPayload = payload;
            if (resend)
                return;
        }
        if (type != TypeMdr || payload.Length == 0)
            return;

        if (payload[0] == ConnectRetProtocolInfo)
        {
            // Button events only arrive while this notifier is on. It is off after some reconnects, e.g. around calls.
            SendCommand(socket, [LogSetStatus, LogTimeSeriesOperation, Enable]);
            Status?.Invoke($"linked to {name} ({version})");
        }
        else if (payload[0] == LogNotifyParam)
            foreach (var eventName in LogEventNames(payload))
                HeadsetEvent?.Invoke(eventName);
    }

    // C9, type, then length-prefixed strings: "C9 01 0F keyCustomBtnTwo 00 00", "C9 01 00 04 opNC 00"
    static List<string> LogEventNames(byte[] payload)
    {
        var names = new List<string>(2);
        int i = 2;
        while (i < payload.Length)
        {
            int length = payload[i++];
            if (i + length > payload.Length)
                break;
            if (length > 0)
                names.Add(System.Text.Encoding.ASCII.GetString(payload, i, length));
            i += length;
        }
        return names;
    }

    void SendCommand(Socket socket, ReadOnlySpan<byte> payload)
    {
        Send(socket, TypeMdr, _txSeq, payload);
        _txSeq ^= 1;
    }

    void Send(Socket socket, byte type, byte seq, ReadOnlySpan<byte> payload)
    {
        var raw = new byte[payload.Length + 7];
        raw[0] = type;
        raw[1] = seq;
        BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(2), payload.Length);
        payload.CopyTo(raw.AsSpan(6));
        raw[^1] = Checksum(raw.AsSpan(0, raw.Length - 1));

        var frame = new List<byte>(raw.Length * 2 + 2) { FrameStart };
        foreach (byte b in raw)
        {
            if (b is FrameStart or FrameEnd or FrameEscape)
            {
                frame.Add(FrameEscape);
                frame.Add((byte)(b - 0x10));
            }
            else
                frame.Add(b);
        }
        frame.Add(FrameEnd);

        Trace?.Invoke($"TX type {type:X2} seq {seq}  {Hex(payload)}");
        socket.Send([.. frame]);
    }

    static byte[]? Unescape(List<byte> escaped)
    {
        var data = new byte[escaped.Count];
        int length = 0;
        for (int i = 0; i < escaped.Count; i++)
        {
            byte b = escaped[i];
            if (b == FrameEscape)
            {
                if (++i == escaped.Count)
                    return null;
                b = (byte)(escaped[i] + 0x10);
                if (b is not (FrameStart or FrameEnd or FrameEscape))
                    return null;
            }
            data[length++] = b;
        }
        return data[..length];
    }

    static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte sum = 0;
        foreach (byte b in data)
            sum += b;
        return sum;
    }

    static string Hex(ReadOnlySpan<byte> data) => data.IsEmpty ? "-" : BitConverter.ToString(data.ToArray()).Replace('-', ' ');

    static List<(ulong Address, string Name)> ConnectedAudioDevices()
    {
        var devices = new List<(ulong, string)>();
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS { dwSize = (uint)sizeof(BLUETOOTH_DEVICE_SEARCH_PARAMS), fReturnConnected = 1 };
        var info = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)sizeof(BLUETOOTH_DEVICE_INFO) };
        nint find = BluetoothFindFirstDevice(search, ref info);
        if (find == 0)
            return devices;
        do
        {
            if (info.fConnected != 0 && ((info.ulClassofDevice >> 8) & 0x1F) == MajorClassAudioVideo)
                devices.Add((info.Address, new string(info.szName)));
        }
        while (BluetoothFindNextDevice(find, ref info));
        BluetoothFindDeviceClose(find);
        return devices;
    }

    // SOCKADDR_BTH, packed: family(2) address(8) service(16) port(4)
    sealed class BluetoothEndPoint(ulong address, Guid service) : EndPoint
    {
        public override AddressFamily AddressFamily => (AddressFamily)AF_BTH;

        public override SocketAddress Serialize()
        {
            var socketAddress = new SocketAddress(AddressFamily, 30);
            Span<byte> body = stackalloc byte[28];
            body.Clear();
            BinaryPrimitives.WriteUInt64LittleEndian(body, address);
            service.TryWriteBytes(body[8..]);
            for (int i = 0; i < body.Length; i++)
                socketAddress[2 + i] = body[i];
            return socketAddress;
        }

        public override EndPoint Create(SocketAddress socketAddress) => this;
    }
}
