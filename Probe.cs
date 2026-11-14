using static SonyMute.Native;

namespace SonyMute;

/// <summary>
/// Diagnostic mode: logs every frame the headset sends and the named events inside them, and marks the
/// event that toggles the mic. Used to confirm a model is supported and to find its button event name.
/// </summary>
static class Probe
{
    const int StdOutput = -11;

    public static int Run(Settings settings, int seconds)
    {
        if (GetStdHandle(StdOutput) == 0)
            AllocConsole();

        Line($"SonyMute probe. Press and double-press the headset buttons. Events named \"{settings.ButtonEvent}\" toggle the mic. Close the window to quit.");
        using var link = new SonyLink(settings.Device) { Trace = Line };
        link.Status += status => Line("status: " + status);
        link.HeadsetEvent += name =>
            Line(name == settings.ButtonEvent ? $">>> event {name}  <-- toggles the mic" : $">>> event {name}");
        link.Enabled = true;
        link.Start();
        Thread.Sleep(seconds > 0 ? seconds * 1000 : Timeout.Infinite);
        return 0;
    }

    static void Line(string text) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {text}");
}
