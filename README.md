# DyIsd

A dynamic island for Windows 11. A small pill at the top of your screen that appears when
something happens and hides when nothing is.

| Feature | What it shows |
|---|---|
| Now playing | Song, artist, album art, play/pause/skip from Spotify, YouTube in any browser, Media Player, VLC… |
| Volume | Replaces the Windows volume pop-up. Scroll over the island to change volume. |
| Brightness | Pops up when laptop brightness changes. Shift + scroll over the island to change it. |
| Battery | Charger plugged in or out, warnings at 15% and 5%. |
| Notifications | WhatsApp, Discord, Outlook and other app notifications. |
| Focus timer | 25-minute focus sessions. **Ctrl + Alt + F** or the tray icon starts one. |
| Clipboard | What you just copied. Password-manager copies are never shown. |
| Classes & deadlines | Reminders from your Google Calendar link and deadlines you add. |
| Downloads | While a browser downloads, then "Download complete" with Open / Folder. |

Every feature can be turned off in Settings. The island can sit top-left, top-center or
top-right: drag it, or pick in Settings. It follows your Windows light/dark theme and accent color.

## Install

1. Open the repo on GitHub → **Actions** → the latest green **Build DyIsd** run.
2. Under **Artifacts**, download **DyIsd-installer** and unzip it.
3. Double-click **Install.bat** and click **Yes** on the admin prompt.
   (It trusts DyIsd's certificate, installs the app and starts it.)
4. The first time, Windows asks whether DyIsd can read your notifications. Click **Allow**.

To update, do the same with a newer build. Your settings are kept.

**Portable version:** `DyIsd-portable` is a plain folder; run `DyIsd.exe`. Everything works
except notifications, because Windows only gives notification access to installed apps.

Right-click the DyIsd icon in the taskbar corner for: focus session, what's next, settings,
pause, quit.

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
  SettingsWindow.xaml    the settings screen
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
