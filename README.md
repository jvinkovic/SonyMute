# SonyMute

A tiny Windows 11 tray app that turns a double press of the NC/AMB button on Sony headphones into a system-wide microphone mute toggle, with a low tone for mute and a high tone for unmute.

Built for the WH-1000XM5. It works with any Sony headset that speaks Sony's headphone control protocol (the one the Sony app uses) and switches noise-cancelling or ambient mode with a button or touch gesture.

## How it works

Windows never sees the NC/AMB button as input. The XM5 registers audio (A2DP), media control (AVRCP), hands-free (HFP) and a Bluetooth HID service, but the HID service exposes no input collections, so keyboard hooks, AutoHotkey and media-key listeners cannot detect the press.

The headset does report it on Sony's own control channel (RFCOMM, the "MDR" protocol the Sony app uses). Its event-log notification (`LOG_NTFY_PARAM`, command `0xC9`) carries named button events. On the WH-1000XM5 the NC/AMB button sends:

| Gesture | Event | Headset behaviour |
| --- | --- | --- |
| Single press | `keyCustomBtn` | Switches NC / Ambient / Off as usual |
| Double press | `keyCustomBtnTwo` | Nothing, so it is free to use |
| Triple press | `keyCustomBtnThree` | Nothing |

SonyMute opens that channel and toggles the mute on `keyCustomBtnTwo`. A double press does not change the noise-cancelling mode. The app only sends the protocol-info request and the acknowledgements the protocol requires. It never changes headset settings. Resent frames (same sequence number and payload) are ignored, so one press never toggles twice.

Other Sony models send their own event names. Run the probe (below) to see them and put the one you want in `button_event`.

The protocol framing comes from [mos9527/SonyHeadphonesClient](https://github.com/mos9527/SonyHeadphonesClient), which also powers [soundconnect-desktop](https://github.com/B-Howell/soundconnect-desktop).

## Muting

- Mutes **every active microphone** through Windows Core Audio (`IAudioEndpointVolume`). Every app (Teams, Zoom, Meet, Discord) receives silence, whichever mic it picked.
- The state counts as muted only when all mics are muted. A mic that appears while muted (for example after a Bluetooth reconnect) is muted too.
- The mute lives only as long as SonyMute. Exiting from the tray unmutes (with the unmute tone), and so do a Windows shutdown or sign-out and an app crash. A hard kill (Task Manager "End task" on the process) cannot be intercepted and leaves the mics muted.
- Mute changes made elsewhere (Windows Settings, a keyboard mic key, another app) are picked up and confirmed with the same tones.
- Some drivers expose no endpoint volume, so Windows itself cannot mute them. The Intel Smart Sound Technology mic array is one example. SonyMute lists such mics in the tray menu as "Cannot be muted by Windows" instead of pretending.
- An app's own in-call mute button is separate and invisible to Windows. SonyMute's mute sits below it.

## Sound confirmation

- Mute: 300 Hz, 150 ms. Unmute: 600 Hz then 700 Hz, 150 ms each. Short fades, 50% volume. Everything is configurable.
- A tone only plays after the mute state really changed.
- When a call is using the Windows communications output (the Bluetooth hands-free endpoint during a call), the tone goes there. Otherwise it goes to the default output. Either way it plays in the headphones and is unaffected by the mic mute.
- 200 ms of leading silence keeps a sleeping Bluetooth output from swallowing the start of the tone.

## Tray

Left click toggles the mute. Right click opens the menu: mute/unmute, mic and headset status, last headset event, and toggles for the button integration, sounds, notifications and Start with Windows (a per-user `Run` entry, no admin rights needed).

Icon: green microphone = live, red microphone with slash = muted.

## Settings

`%APPDATA%\SonyMute\settings.ini`, also reachable from the tray menu. Restart SonyMute after editing it.

| Key | Default | Meaning |
| --- | --- | --- |
| `button` | `true` | The headset button event toggles the mute |
| `sounds` / `notifications` | `true` / `false` | Confirmation tones and Windows notifications |
| `button_event` | `keyCustomBtnTwo` | Headset event that toggles the mute |
| `cooldown_ms` | `500` | The event is ignored for this long after a toggle |
| `mute_hz` / `unmute_hz` | `300` / `600,700` | Tone notes in Hz, played in order |
| `tone_ms` / `tone_volume` | `150` / `0.5` | Length of each note, volume 0.0 to 1.0 |
| `tone_lead_ms` | `200` | Silence before each tone |
| `device` | empty | Only link to a headset whose name contains this text |

## Checking a headset (probe mode)

```powershell
SonyMute.exe --probe
```

This opens a console that logs every frame from the headset and each named event as `>>> event <name>`. The configured `button_event` is marked `<-- toggles the mic`. Press and double-press the buttons to see what your model sends. If no `key...` events appear, that model does not report its buttons, and only the tray click can toggle the mute. `SonyMute.exe --probe 30` stops after 30 seconds.

## Build

Requires the .NET 10 SDK to build and the .NET 10 runtime to run.

```powershell
dotnet publish -c Release -r win-x64 -o publish
```

The result is `publish\SonyMute.exe`, a single file of about 220 KB. At idle it uses roughly 7 MB of private memory and no CPU. There is no cloud connection and no audio recording or streaming, and it needs no administrator rights.
