using System.Globalization;
using Microsoft.Win32;

namespace SonyMute;

sealed class Settings
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SonyMute");
    public static readonly string FilePath = Path.Combine(Dir, "settings.ini");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "SonyMute";

    public bool ButtonEnabled = true, Sounds = true, Notifications;
    public int CooldownMs = 500, ToneMs = 150, ToneLeadMs = 200;
    public int[] MuteHz = [300], UnmuteHz = [600, 700];
    public double ToneVolume = 0.5;
    public string Device = "", ButtonEvent = "keyCustomBtnTwo";

    public static Settings Load()
    {
        var s = new Settings();
        if (File.Exists(FilePath))
        {
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith('#') || eq < 0)
                    continue;
                string key = line[..eq].Trim().ToLowerInvariant(), value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "button": s.ButtonEnabled = Bool(value, s.ButtonEnabled); break;
                    case "sounds": s.Sounds = Bool(value, s.Sounds); break;
                    case "notifications": s.Notifications = Bool(value, s.Notifications); break;
                    case "button_event": if (value.Length > 0) s.ButtonEvent = value; break;
                    case "cooldown_ms": s.CooldownMs = Int(value, s.CooldownMs, 0, 10000); break;
                    case "mute_hz": s.MuteHz = Notes(value, s.MuteHz); break;
                    case "unmute_hz": s.UnmuteHz = Notes(value, s.UnmuteHz); break;
                    case "tone_ms": s.ToneMs = Int(value, s.ToneMs, 30, 1000); break;
                    case "tone_lead_ms": s.ToneLeadMs = Int(value, s.ToneLeadMs, 0, 1000); break;
                    case "tone_volume":
                        if (double.TryParse(value, CultureInfo.InvariantCulture, out double v))
                            s.ToneVolume = Math.Clamp(v, 0, 1);
                        break;
                    case "device": s.Device = value; break;
                }
            }
        }
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, string.Create(CultureInfo.InvariantCulture, $"""
            # SonyMute settings. Restart SonyMute after editing this file.

            # NC/AMB double-press toggles the microphone mute
            button={B(ButtonEnabled)}
            # Confirmation tones and Windows notifications
            sounds={B(Sounds)}
            notifications={B(Notifications)}

            # Headset event that toggles the mute. "SonyMute.exe --probe" shows the names your headset sends.
            # WH-1000XM5: keyCustomBtn, keyCustomBtnTwo, keyCustomBtnThree = NC/AMB single, double, triple press.
            button_event={ButtonEvent}
            # Repeats of the event are ignored for this long after a toggle
            cooldown_ms={CooldownMs}

            # Tone pitches in Hz. A comma-separated list plays the notes one after another, each tone_ms long.
            mute_hz={string.Join(",", MuteHz)}
            unmute_hz={string.Join(",", UnmuteHz)}
            # Note length in ms and volume (0.0 to 1.0)
            tone_ms={ToneMs}
            tone_volume={ToneVolume}
            # Silence before each tone so a sleeping Bluetooth output does not swallow it
            tone_lead_ms={ToneLeadMs}

            # Only link to a headset whose name contains this text. Empty means any connected Sony audio device.
            device={Device}

            """));
    }

    public static bool AutoStart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValue, false);
        }
    }

    static string B(bool value) => value ? "true" : "false";
    static bool Bool(string value, bool fallback) => bool.TryParse(value, out bool b) ? b : fallback;
    static int[] Notes(string value, int[] fallback)
    {
        int[] notes = [.. value.Split(',')
            .Select(part => int.TryParse(part.Trim(), CultureInfo.InvariantCulture, out int hz) ? hz : 0)
            .Where(hz => hz > 0)
            .Select(hz => Math.Clamp(hz, 100, 5000))];
        return notes.Length > 0 ? notes : fallback;
    }

    static int Int(string value, int fallback, int min, int max) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out int i) ? Math.Clamp(i, min, max) : fallback;
}
