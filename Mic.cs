using System.Runtime.InteropServices;

namespace SonyMute;

[ComVisible(true)]
public sealed class Mic : IMMNotificationClient, IAudioEndpointVolumeCallback, IDisposable
{
    const uint DeviceStateActive = 0x1, ClsCtxAll = 0x17;
    const int AudioSessionStateActive = 1;

    readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    readonly List<IAudioEndpointVolume> _volumes = [];
    readonly List<string> _unmutable = [];
    readonly Action _stateChanged, _devicesChanged;

    public Mic(Action stateChanged, Action devicesChanged)
    {
        _stateChanged = stateChanged;
        _devicesChanged = devicesChanged;
        _enumerator.RegisterEndpointNotificationCallback(this);
        Bind();
    }

    public int Count => _volumes.Count;

    public IReadOnlyList<string> Unmutable => _unmutable;

    // true only if all mics are muted, null if there are none
    public bool? Muted
    {
        get
        {
            bool any = false;
            foreach (var volume in _volumes)
            {
                if (volume.GetMute(out bool muted) != 0)
                    continue;
                if (!muted)
                    return false;
                any = true;
            }
            return any ? true : null;
        }
    }

    public bool SetAll(bool mute)
    {
        var context = Guid.Empty;
        foreach (var volume in _volumes)
            volume.SetMute(mute, ref context);
        return Muted == mute;
    }

    // Used from the crash handler, so it doesn't touch any existing state.
    public static void UnmuteAll()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out var devices);
            devices.GetCount(out uint count);
            var context = Guid.Empty;
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    devices.Item(i, out var device);
                    ((IAudioEndpointVolume)Activate(device, typeof(IAudioEndpointVolume).GUID)).SetMute(false, ref context);
                }
                catch (Exception e) when (e is COMException or InvalidCastException) { }
            }
        }
        catch (COMException) { }
    }

    public void Bind()
    {
        Release();
        _enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out var devices);
        devices.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            devices.Item(i, out var device);
            try
            {
                var volume = (IAudioEndpointVolume)Activate(device, typeof(IAudioEndpointVolume).GUID);
                volume.RegisterControlChangeNotify(this);
                _volumes.Add(volume);
            }
            catch (InvalidCastException)
            {
                // E_NOINTERFACE, e.g. Intel Smart Sound mic array
                _unmutable.Add(FriendlyName(device));
            }
            catch (COMException)
            {
                // device went away, next device event rebinds
            }
            Marshal.ReleaseComObject(device);
        }
        Marshal.ReleaseComObject(devices);
    }

    // During a BT call only the hands-free endpoint is audible, stereo output goes nowhere.
    public bool CallActive()
    {
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Communications, out var device);
            var manager = (IAudioSessionManager2)Activate(device, typeof(IAudioSessionManager2).GUID);
            manager.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out int count);
            bool active = false;
            for (int i = 0; i < count && !active; i++)
            {
                sessions.GetSession(i, out var session);
                session.GetState(out int state);
                active = state == AudioSessionStateActive;
                Marshal.ReleaseComObject(session);
            }
            Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(manager);
            Marshal.ReleaseComObject(device);
            return active;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Release();
        _enumerator.UnregisterEndpointNotificationCallback(this);
        Marshal.ReleaseComObject(_enumerator);
    }

    static object Activate(IMMDevice device, Guid iid)
    {
        device.Activate(ref iid, ClsCtxAll, 0, out object instance);
        return instance;
    }

    static string FriendlyName(IMMDevice device)
    {
        const ushort VtLpwstr = 31;
        var key = new PropertyKey { FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
        try
        {
            device.OpenPropertyStore(0, out var store);
            store.GetValue(ref key, out var value);
            string? name = value.VarType == VtLpwstr ? Marshal.PtrToStringUni(value.Value) : null;
            PropVariantClear(ref value);
            Marshal.ReleaseComObject(store);
            return name ?? "Unknown microphone";
        }
        catch (COMException)
        {
            return "Unknown microphone";
        }
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    void Release()
    {
        foreach (var volume in _volumes)
        {
            try { volume.UnregisterControlChangeNotify(this); } catch (COMException) { }
            Marshal.ReleaseComObject(volume);
        }
        _volumes.Clear();
        _unmutable.Clear();
    }

    // called on Core Audio threads, just post to the UI thread
    int IAudioEndpointVolumeCallback.OnNotify(nint data) { _stateChanged(); return 0; }
    int IMMNotificationClient.OnDeviceStateChanged(string id, uint state) { _devicesChanged(); return 0; }
    int IMMNotificationClient.OnDeviceAdded(string id) { _devicesChanged(); return 0; }
    int IMMNotificationClient.OnDeviceRemoved(string id) { _devicesChanged(); return 0; }
    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? id) { _devicesChanged(); return 0; }
    int IMMNotificationClient.OnPropertyValueChanged(string id, PropertyKey key) => 0;
}
