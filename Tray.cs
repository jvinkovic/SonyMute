using static SonyMute.Native;

namespace SonyMute;

sealed unsafe class Tray(nint hwnd, uint callbackMessage)
{
    const uint IconId = 1;

    public void Show(nint icon, string tip)
    {
        var data = New(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = callbackMessage;
        data.hIcon = icon;
        Copy(tip, data.szTip, 128);
        if (!Shell_NotifyIconW(NIM_MODIFY, data))
            Shell_NotifyIconW(NIM_ADD, data);
    }

    public void Balloon(string text)
    {
        var data = New(NIF_INFO);
        data.dwInfoFlags = NIIF_NOSOUND;
        Copy("SonyMute", data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        Shell_NotifyIconW(NIM_MODIFY, data);
    }

    public void Remove()
    {
        var data = New(0);
        Shell_NotifyIconW(NIM_DELETE, data);
    }

    public uint Menu(ReadOnlySpan<(uint Id, string? Text, uint Flags)> items, uint defaultId)
    {
        nint menu = CreatePopupMenu();
        foreach (var (id, text, flags) in items)
            AppendMenuW(menu, flags, id, text);
        SetMenuDefaultItem(menu, defaultId, 0);
        GetCursorPos(out var p);
        SetForegroundWindow(hwnd);
        int chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, hwnd, 0);
        PostMessageW(hwnd, WM_NULL, 0, 0);
        DestroyMenu(menu);
        return (uint)chosen;
    }

    public static nint MicIcon(bool muted, int size)
    {
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        nint color = CreateDIBSection(0, header, 0, out nint bits, 0, 0);
        var pixels = new Span<uint>((void*)bits, size * size);
        uint rgb = muted ? 0xE53935u : 0x2EB84Fu;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = MicShape((x + 0.5f) / size, (y + 0.5f) / size, muted);
                float alpha = Math.Clamp(0.5f - distance * size, 0f, 1f);
                pixels[y * size + x] = (uint)(alpha * 255) << 24 | rgb;
            }
        }

        var maskBits = new byte[(size + 15) / 16 * 2 * size];
        nint mask;
        fixed (byte* m = maskBits)
            mask = CreateBitmap(size, size, 1, 1, m);

        nint icon = CreateIconIndirect(new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color });
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    // signed distance, negative inside
    static float MicShape(float x, float y, bool muted)
    {
        float d = Segment(x, y, .5f, .22f, .5f, .40f) - .15f;
        if (y > .40f)
        {
            float r = MathF.Sqrt((x - .5f) * (x - .5f) + (y - .40f) * (y - .40f));
            d = Math.Min(d, MathF.Abs(r - .27f) - .05f);
        }
        d = Math.Min(d, Segment(x, y, .5f, .67f, .5f, .86f) - .05f);
        d = Math.Min(d, Segment(x, y, .32f, .88f, .68f, .88f) - .05f);
        if (muted)
        {
            float slash = Segment(x, y, .12f, .08f, .88f, .92f);
            d = Math.Min(Math.Max(d, .11f - slash), slash - .055f);
        }
        return d;
    }

    static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0f, 1f);
        float ex = px - ax - t * dx, ey = py - ay - t * dy;
        return MathF.Sqrt(ex * ex + ey * ey);
    }

    NOTIFYICONDATAW New(uint flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = hwnd,
        uID = IconId,
        uFlags = flags,
    };

    static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }
}
