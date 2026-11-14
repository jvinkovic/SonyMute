# SonyMute

Windows tray app. Double-press the NC/AMB button on a Sony WH-1000XM5 to mute or unmute all microphones. Plays a short tone so you know which state you are in.

## How it works

The NC/AMB button is not visible to Windows as a key, so hotkey tools can't catch it. The headset does report it over Sony's own control channel (RFCOMM, the same one the Sony app uses) as a log event:

| Press | Event |
| --- | --- |
| single | `keyCustomBtn` (also switches NC/Ambient/Off) |
| double | `keyCustomBtnTwo` (does nothing on the headset) |
| triple | `keyCustomBtnThree` (does nothing on the headset) |

SonyMute listens for `keyCustomBtnTwo` and toggles the mute of every active capture device through Core Audio, so it works in Teams, Zoom, Meet, Discord, anything. It never changes headset settings.

Other Sony models may use different event names. Run `SonyMute.exe --probe`, press the buttons and set `button_event` to what shows up.

Protocol framing taken from [mos9527/SonyHeadphonesClient](https://github.com/mos9527/SonyHeadphonesClient).

## Notes

- Mute is undone when the app exits, crashes or Windows shuts down. A hard kill (Task Manager) leaves the mics muted.
- Mute changes made elsewhere (Windows settings, keyboard mic key) are picked up.
- Some mic drivers (e.g. Intel Smart Sound mic arrays) can't be muted through Windows at all. These are listed in the tray menu.
- During a Bluetooth call the tone is played on the communications device, otherwise on the default output.

## Tray

Left click toggles mute. Right click for the menu: status, button on/off, sounds, notifications, start with Windows.

## Settings

`%APPDATA%\SonyMute\settings.ini`. Restart the app after editing.

| Key | Default | |
| --- | --- | --- |
| `button` | `true` | headset button toggles mute |
| `button_event` | `keyCustomBtnTwo` | event name that toggles mute |
| `cooldown_ms` | `500` | ignore the event this long after a toggle |
| `sounds` | `true` | confirmation tones |
| `notifications` | `false` | Windows notifications |
| `mute_hz` | `300` | mute tone, comma-separated notes |
| `unmute_hz` | `600,700` | unmute tone, comma-separated notes |
| `tone_ms` | `150` | length of each note |
| `tone_volume` | `0.5` | 0.0 to 1.0 |
| `tone_lead_ms` | `200` | silence before the tone, so Bluetooth output wakes up in time |
| `device` | | only connect to a headset whose name contains this |

## Build

Needs .NET 10.

```powershell
dotnet publish -c Release -r win-x64 -o publish
```
