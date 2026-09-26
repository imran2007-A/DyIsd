# DyIsd

A dynamic island for Windows 11, modeled on the iPhone's. A small black pill at the top of your
screen that appears when something is running in the background and hides when nothing is.
Anything that belongs to the app you're already in stays hidden: YouTube playing in Chrome
doesn't show while you're in Chrome.

| Feature | What it shows |
|---|---|
| Now playing | Song, artist, album art and controls from Spotify, YouTube in any browser, Media Player, VLC… Only while it's playing: pause and it's gone. |
| Volume | Replaces the Windows volume pop-up. |
| Brightness | Pops up when laptop brightness changes. |
| Battery | Charger plugged in or out, warnings at 15% and 5%. |
| Mic & camera | Orange dot while an app uses your mic, green dot for the camera, like the iPhone. |
| Earbuds | A card when Bluetooth earbuds or headphones connect, with battery level when Windows knows it. |
| Focus timer | Focus sessions of 15–60 min. **Ctrl + Alt + F** or the Control Center starts one. |
| Clipboard | What you just copied. Password-manager copies are never shown. |
| Classes & deadlines | Reminders from your Google Calendar link and deadlines you add. For each deadline you pick when to be reminded: 1 week / 1 day / 2 hours / 30 min before or when due, daily or weekly until it's due, and extra dates. |
| Downloads | While a browser downloads, then "Download complete" with Open / Folder. |

Only the newest activity shows at a time.

## Using it

The island never gets in your way: move the mouse over it and it fades, and clicks go through
to whatever is underneath (like Chrome tabs). **Hold Alt** to use it:

| | |
|---|---|
| Alt + click | Jump to the app (Spotify, Chrome…) |
| Alt + right-click | Expand for controls; stays open until you move away |
| Alt + drag | Move it to the left, center or right |
| Alt + scroll | Volume (Shift too for brightness) |

**Control Center:** click the DyIsd icon in the taskbar corner. Turn features on and off, start
a focus session, add deadlines, paste your calendar link, pick the position.
Windows 11 may hide new tray icons under the `^` arrow; drag DyIsd out onto the taskbar to keep it visible.

## Install

1. Open the repo on GitHub → **Actions** → the latest green **Build DyIsd** run.
2. Under **Artifacts**, download **DyIsd-installer** and unzip it.
3. Double-click **Install.bat** and click **Yes** on the admin prompt.
   (It trusts DyIsd's certificate, installs the app and starts it.)
To update, do the same with a newer build. Your settings are kept.

**Portable version:** `DyIsd-portable` is a plain folder; run `DyIsd.exe`. Everything works the same.

## Where things are

| | |
|---|---|
| Settings file | `%LOCALAPPDATA%\DyIsd\settings.json` |
| Log (send this if something breaks) | `%LOCALAPPDATA%\DyIsd\log.txt` |

## Project layout

```
src/DyIsd/
  App.xaml.cs            starts everything and connects services to the island
  Island/                the island window, its views and the logic that picks what to show
  Services/              one file per Windows feature (media, volume, battery, ...)
  Controls/              hand-drawn progress ring, progress bar, equalizer bars
  Settings/              settings saved as JSON
  ControlCenter.xaml     the black panel with toggles, deadlines and options
packaging/               MSIX installer manifest, build and install scripts
prototype/island.html    the clickable design prototype (open in any browser)
.github/workflows/       GitHub Actions: builds the installer on every push
```

Built with C#, .NET 10 and WPF. Libraries: NAudio (volume), Ical.Net (calendar),
System.Management (brightness).

## Build it yourself

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/DyIsd
```

See `LEARN-LATER.md` for the learning checklist.
