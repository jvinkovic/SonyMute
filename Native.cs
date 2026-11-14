using System.Runtime.InteropServices;

#pragma warning disable CS0649 // filled in by Windows

namespace SonyMute;

static unsafe partial class Native
{
    public const uint WM_NULL = 0x0000, WM_ENDSESSION = 0x0016, WM_TIMER = 0x0113, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_APP = 0x8000;
    public const uint MM_WOM_DONE = 0x03BD;
    public const int SM_CXSMICON = 49;
    public const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIIF_NOSOUND = 0x10;
    public const uint WAVE_MAPPER = 0xFFFFFFFF, CALLBACK_WINDOW = 0x00010000, WAVE_MAPPED_DEFAULT_COMMUNICATION_DEVICE = 0x0010;
    public const int AF_BTH = 32, BTHPROTO_RFCOMM = 3;

    public struct POINT { public int X, Y; }

    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    public struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
    }

    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    public struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    public struct WAVEHDR
    {
        public nint lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public nuint dwUser;
        public uint dwFlags, dwLoops;
        public nint lpNext;
        public nuint reserved;
    }

    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public int fReturnAuthenticated, fReturnRemembered, fReturnUnknown, fReturnConnected, fIssueInquiry;
        public byte cTimeoutMultiplier;
        public nint hRadio;
    }

    public struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected, fRemembered, fAuthenticated;
        public fixed ushort stLastSeen[8];
        public fixed ushort stLastUsed[8];
        public fixed char szName[248];
    }

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(in WNDCLASSEXW wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconIndirect(in ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint context);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, in NOTIFYICONDATAW data);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, in BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, void* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutOpen(out nint waveOut, uint deviceId, in WAVEFORMATEX format, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutPrepareHeader(nint waveOut, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutWrite(nint waveOut, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutUnprepareHeader(nint waveOut, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutClose(nint waveOut);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? name);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllocConsole();

    [LibraryImport("BluetoothApis.dll")]
    public static partial nint BluetoothFindFirstDevice(in BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);

    [LibraryImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BluetoothFindNextDevice(nint find, ref BLUETOOTH_DEVICE_INFO info);

    [LibraryImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BluetoothFindDeviceClose(nint find);
}
