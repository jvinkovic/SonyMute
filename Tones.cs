using System.Runtime.InteropServices;
using static SonyMute.Native;

namespace SonyMute;

static unsafe class Tones
{
    const int Rate = 44100;
    static readonly uint HeaderSize = (uint)sizeof(WAVEHDR);

    /// <summary>
    /// 16-bit mono PCM: one sine note per frequency, each with a short fade to avoid clicks. Leading silence
    /// gives a sleeping Bluetooth output time to wake up so the start of the tone is not cut off.
    /// </summary>
    public static byte[] Make(int[] notesHz, int noteMs, int leadMs, double volume)
    {
        int lead = Rate * leadMs / 1000, count = Rate * noteMs / 1000, fade = Rate / 200;
        var pcm = GC.AllocateArray<byte>((lead + count * notesHz.Length) * 2, pinned: true);
        var samples = MemoryMarshal.Cast<byte, short>(pcm.AsSpan(lead * 2));
        for (int note = 0; note < notesHz.Length; note++)
        {
            for (int i = 0; i < count; i++)
            {
                double envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fade);
                samples[note * count + i] = (short)(Math.Sin(2 * Math.PI * notesHz[note] * i / Rate) * envelope * volume * short.MaxValue);
            }
        }
        return pcm;
    }

    public static int DurationMs(byte[] pcm) => pcm.Length / 2 * 1000 / Rate;

    /// <summary>Plays a pinned buffer from <see cref="Make"/>. Completion arrives as MM_WOM_DONE on <paramref name="hwnd"/>.</summary>
    public static void Play(byte[] pcm, nint hwnd, bool communicationsDevice)
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = Rate,
            nAvgBytesPerSec = Rate * 2,
            nBlockAlign = 2,
            wBitsPerSample = 16,
        };
        uint flags = CALLBACK_WINDOW | (communicationsDevice ? WAVE_MAPPED_DEFAULT_COMMUNICATION_DEVICE : 0);
        if (waveOutOpen(out nint waveOut, WAVE_MAPPER, format, hwnd, 0, flags) != 0)
            return;

        var header = (WAVEHDR*)NativeMemory.AllocZeroed(HeaderSize);
        header->lpData = Marshal.UnsafeAddrOfPinnedArrayElement(pcm, 0);
        header->dwBufferLength = (uint)pcm.Length;
        if (waveOutPrepareHeader(waveOut, header, HeaderSize) != 0 || waveOutWrite(waveOut, header, HeaderSize) != 0)
            Done(waveOut, (nint)header);
    }

    public static void Done(nint waveOut, nint header)
    {
        waveOutUnprepareHeader(waveOut, (WAVEHDR*)header, HeaderSize);
        waveOutClose(waveOut);
        NativeMemory.Free((void*)header);
    }
}
