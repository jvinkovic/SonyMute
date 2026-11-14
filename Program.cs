using System.Diagnostics;
using System.Runtime.InteropServices;
using static SonyMute.Native;

namespace SonyMute;

static unsafe class Program
{
    const uint WM_TRAY = WM_APP + 1, WM_MIC_STATE = WM_APP + 2, WM_MIC_DEVICES = WM_APP + 3, WM_DOUBLE_PRESS = WM_APP + 4, WM_LINK = WM_APP + 5;
    const nuint RebindTimer = 1;
    const uint RebindDelayMs = 400;
    const string WindowClass = "SonyMuteWindow";

    enum Command : uint { Toggle = 1, Button, Sounds, Notifications, AutoStart, EditSettings, Exit }

    static Settings _settings = null!;
    static Tray _tray = null!;
    static Mic _mic = null!;
    static SonyLink _link = null!;
    static long _nextButtonMs;
    static byte[] _muteTone = null!, _unmuteTone = null!;
    static nint _hwnd, _iconLive, _iconMuted;
    static uint _taskbarCreated;
    static bool _muted;
    static string _note = "";
    static volatile string _linkStatus = "starting", _lastEvent = "none yet";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--probe")
            return Probe.Run(Settings.Load(), args.Length > 1 && int.TryParse(args[1], out int seconds) ? seconds : 0);

        using var single = new Mutex(true, @"Local\SonyMute", out bool first);
        if (!first)
            return 0;

        AppDomain.CurrentDomain.UnhandledException += OnCrash;
        SetProcessDpiAwarenessContext(-4);
        Run();
        return 0;
    }

    static void Run()
    {
        var s = _settings = Settings.Load();
        _muteTone = Tones.Make(s.MuteHz, s.ToneMs, s.ToneLeadMs, s.ToneVolume);
        _unmuteTone = Tones.Make(s.UnmuteHz, s.ToneMs, s.ToneLeadMs, s.ToneVolume);

        nint instance = GetModuleHandleW(null);
        fixed (char* className = WindowClass)
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = instance, lpszClassName = className };
            RegisterClassExW(wc);
        }
        // not message-only, those don't get TaskbarCreated
        _hwnd = CreateWindowExW(0, WindowClass, "SonyMute", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        int iconSize = GetSystemMetrics(SM_CXSMICON);
        _iconLive = Tray.MicIcon(false, iconSize);
        _iconMuted = Tray.MicIcon(true, iconSize);
        _tray = new Tray(_hwnd, WM_TRAY);

        _mic = new Mic(() => Post(WM_MIC_STATE), () => Post(WM_MIC_DEVICES));
        _muted = _mic.Muted ?? false;
        Refresh();

        _link = new SonyLink(s.Device);
        _link.Status += status =>
        {
            _linkStatus = status;
            Post(WM_LINK);
        };
        _link.HeadsetEvent += name =>
        {
            _lastEvent = $"{name} at {DateTime.Now:HH:mm:ss}";
            long now = Environment.TickCount64;
            if (name == s.ButtonEvent && now >= _nextButtonMs)
            {
                _nextButtonMs = now + s.CooldownMs;
                Post(WM_DOUBLE_PRESS);
            }
        };
        _link.Enabled = s.ButtonEnabled;
        _link.Start();

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessageW(msg);
        }

        _link.Dispose();
        UnmuteOnExit(tone: true);
        _mic.Dispose();
        _tray.Remove();
        DestroyIcon(_iconLive);
        DestroyIcon(_iconMuted);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_TRAY:
                    if ((uint)lParam == WM_LBUTTONUP)
                        Toggle();
                    else if ((uint)lParam == WM_RBUTTONUP)
                        ShowMenu();
                    return 0;
                case WM_DOUBLE_PRESS:
                    Toggle();
                    return 0;
                case WM_MIC_STATE:
                    SyncFromSystem();
                    return 0;
                case WM_MIC_DEVICES:
                    // BT reconnect fires a burst of these
                    SetTimer(hwnd, RebindTimer, RebindDelayMs, 0);
                    return 0;
                case WM_TIMER when (nuint)wParam == RebindTimer:
                    KillTimer(hwnd, RebindTimer);
                    Rebind();
                    return 0;
                case WM_LINK:
                    Refresh();
                    return 0;
                case MM_WOM_DONE:
                    Tones.Done(wParam, lParam);
                    return 0;
                case WM_ENDSESSION when wParam != 0:
                    // shutdown/sign-out, process may be killed right after this
                    UnmuteOnExit(tone: false);
                    return 0;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0)
            {
                Refresh();
                return 0;
            }
        }
        catch (Exception e)
        {
            LogError(e);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    static void Toggle()
    {
        if (_mic.Muted is not bool current)
        {
            _note = "No microphone found";
            Refresh();
            return;
        }
        if (_mic.SetAll(!current))
        {
            _note = "";
            Changed(!current);
        }
        else
        {
            _note = "Mute change failed";
            _muted = _mic.Muted ?? _muted;
            Refresh();
        }
    }

    // Windows keeps endpoint mute across reboots, so undo ours.
    static void UnmuteOnExit(bool tone)
    {
        if (!_muted || !_mic.SetAll(false))
            return;
        _muted = false;
        if (tone && _settings.Sounds)
        {
            Tones.Play(_unmuteTone, _hwnd, _mic.CallActive());
            Thread.Sleep(Tones.DurationMs(_unmuteTone) + 100);
        }
    }

    static void OnCrash(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            LogError(exception);
        if (_muted)
            Mic.UnmuteAll();
    }

    static void Changed(bool muted)
    {
        _muted = muted;
        if (_settings.Sounds)
            Tones.Play(muted ? _muteTone : _unmuteTone, _hwnd, _mic.CallActive());
        if (_settings.Notifications)
            _tray.Balloon(muted ? "MIC MUTED" : "MIC ACTIVE");
        Refresh();
    }

    static void SyncFromSystem()
    {
        if (_mic.Muted is bool muted && muted != _muted)
            Changed(muted);
        else
            Refresh();
    }

    // a mic that shows up while muted gets muted too
    static void Rebind()
    {
        _mic.Bind();
        if (_muted)
            _mic.SetAll(true);
        SyncFromSystem();
    }

    static void Refresh()
    {
        string mic = _mic.Count == 0 ? "no microphone" : _muted ? "MUTED" : "live";
        string tip = $"SonyMute\nMic: {mic}\nButton: {_linkStatus}";
        if (_mic.Unmutable.Count > 0)
            tip += $"\n{_mic.Unmutable.Count} mic(s) cannot be muted";
        if (_note.Length > 0)
            tip += "\n" + _note;
        _tray.Show(_muted ? _iconMuted : _iconLive, tip);
    }

    static void ShowMenu()
    {
        var s = _settings;
        static uint Check(bool on) => on ? MF_CHECKED : MF_STRING;
        List<(uint, string?, uint)> items =
        [
            ((uint)Command.Toggle, _muted ? "Unmute microphones" : "Mute microphones", MF_STRING),
            (0, null, MF_SEPARATOR),
            (0, $"{_mic.Count} microphone(s), {(_muted ? "muted" : "live")}", MF_GRAYED),
            .. _mic.Unmutable.Select(name => (0u, (string?)$"Cannot be muted by Windows: {name}", MF_GRAYED)),
            (0, $"Headset: {_linkStatus}", MF_GRAYED),
            (0, $"Last headset event: {_lastEvent}", MF_GRAYED),
            (0, null, MF_SEPARATOR),
            ((uint)Command.Button, "NC/AMB double-press toggles mute", Check(s.ButtonEnabled)),
            ((uint)Command.Sounds, "Sound feedback", Check(s.Sounds)),
            ((uint)Command.Notifications, "Notifications", Check(s.Notifications)),
            ((uint)Command.AutoStart, "Start with Windows", Check(Settings.AutoStart)),
            ((uint)Command.EditSettings, "Edit settings file...", MF_STRING),
            (0, null, MF_SEPARATOR),
            ((uint)Command.Exit, "Exit", MF_STRING),
        ];

        switch ((Command)_tray.Menu(CollectionsMarshal.AsSpan(items), (uint)Command.Toggle))
        {
            case Command.Toggle:
                Toggle();
                return;
            case Command.Button:
                s.ButtonEnabled = !s.ButtonEnabled;
                _link.Enabled = s.ButtonEnabled;
                s.Save();
                break;
            case Command.Sounds:
                s.Sounds = !s.Sounds;
                s.Save();
                break;
            case Command.Notifications:
                s.Notifications = !s.Notifications;
                s.Save();
                break;
            case Command.AutoStart:
                Settings.AutoStart = !Settings.AutoStart;
                break;
            case Command.EditSettings:
                Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true })?.Dispose();
                break;
            case Command.Exit:
                PostQuitMessage(0);
                return;
        }
        Refresh();
    }

    static void Post(uint msg) => PostMessageW(_hwnd, msg, 0, 0);

    static void LogError(Exception e)
    {
        try { File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:s} {e}{Environment.NewLine}"); }
        catch (IOException) { }
    }
}
