# DyIsd

A dynamic island for Windows 11, modeled on the iPhone's. A small black pill at the top of your
screen that appears when something is running in the background and hides when nothing is.
Anything that belongs to the app you're already in stays hidden: YouTube playing in Chrome
doesn't show while you're in Chrome.

| Feature | What it shows |
|---|---|
| Now playing | Song, artist, album art. Click to open the player: seek, shuffle, repeat, volume, open the app. Only while it's playing: pause from Spotify and it's gone; pause from the island and it shrinks to a tiny dot you can click to resume. |
| Calls | WhatsApp, Discord, Teams, Zoom, Telegram: green pill with the call timer and your voice level. Click for who you're talking to, Mute and Go to call. Your Discord mute shortcut shows up on the island too. |
| Incoming calls | WhatsApp / Discord / Teams / Telegram ringing: caller, a red Decline and a green Answer button, like iPhone. Best effort: DyIsd presses the app's own buttons (see "Honest limits"). |
| Jarvis | Voice control. Hold **Ctrl + Space** and talk, or just say "Jarvis, …". Opens apps and sites, searches, types, presses shortcuts, scrolls, controls music, volume, brightness, Wi-Fi, Bluetooth, dark mode, timers and reminders, answers calls. No AI, nothing leaves your laptop. |
| Silent switch | Muting shows a red bell that wiggles, like flipping the iPhone's switch. |
| Windows Clock | The Clock app's running timer (orange) or stopwatch, while Clock is open (minimized is fine). |
| File transfers | Bluetooth and Nearby Share sends and receives with progress. |
| Volume | Replaces the Windows volume pop-up. |
| Brightness | Pops up when laptop brightness changes. |
| Battery | Charger plugged in or out, warnings at 15% and 5%. |
| Camera | Green dot while an app uses your camera, like the iPhone. |
| Earbuds | A card when Bluetooth earbuds or headphones connect, with battery level when Windows knows it. |
| Focus timer | Focus sessions of 15–60 min. **Ctrl + Alt + F** or the Control Center starts one. |
| Clipboard | What you just copied. Password-manager copies are never shown. |
| Classes & deadlines | Reminders from your Google Calendar link and deadlines you add. For each deadline you pick when to be reminded: 1 week / 1 day / 2 hours / 30 min before or when due, daily or weekly until it's due, and extra dates. |
| Downloads | While a browser downloads, then "Download complete" with Open / Folder. |

## Using it

The most important activity fills the island: call → ringing call / Clock timer → music →
stopwatch / focus timer → downloads / transfers. The next one sits in a small circle beside it;
click the circle to swap them. Nothing shows while you're in the app it belongs to: YouTube in
Chrome shows once you switch away from Chrome.

| | |
|---|---|
| Click | Open (the player, the call controls…); click again to close |
| Right-click | Jump to the app (Spotify, Chrome, WhatsApp…) |
| Drag sideways | Move it to the left, center or right |
| Drag up (flick) | Hide what's showing until it changes (next song, next call…) |
| Scroll | Volume (Shift too for brightness) |

**Control Center:** click the DyIsd icon in the taskbar corner. Turn features on and off, start
a focus session, add deadlines, paste your calendar link, pick the position.
Windows 11 may hide new tray icons under the `^` arrow; drag DyIsd out onto the taskbar to keep it visible.

## Jarvis

Hold **Ctrl + Space**, say the command, let go. Or, with "Listen for Jarvis" on, just say
"Jarvis, open Spotify". The first time, it downloads its speech model once (470 MB) and then
works offline. It runs on your graphics card when it can.

Things you can say (and many ways to say each):

| | |
|---|---|
| Apps & sites | "open chrome", "launch spotify", "switch to vs code", "open leetcode", "open github.com", "open wifi settings", "open downloads", "close discord" |
| Search | "search for cats", "search youtube for lofi", "look up recursion on wikipedia", "images of red pandas", "directions to SRM Ramapuram", "weather in Chennai", "translate good morning to Tamil", "who is…/how to…" |
| Typing & keys | "type hello and press enter", "new tab", "close this tab", "reopen the last tab", "go to tab 3", "copy", "paste", "undo", "select all", "save", "press control shift t", "press page down 3 times", "take a screenshot", "show desktop", "minimize this", "snap left" |
| Scrolling | "scroll down", "scroll up a lot", "scroll down 3", "scroll to the top" |
| Music & YouTube | "play", "pause", "next song", "previous", "restart the song", "shuffle", "what's playing", "play Believer" (YouTube search) |
| Sound & screen | "volume 40", "volume up a bit", "louder", "mute", "brightness 70", "dimmer" |
| PC | "turn off wifi", "bluetooth on", "airplane mode", "dark mode", "lock the computer", "shut down / restart" (asks first), "empty the recycle bin" (asks first) |
| Time & reminders | "what time is it", "what's the date", "how much battery", "set a timer for 5 minutes", "start focus for 40 minutes", "remind me to submit DBMS tomorrow at 5 pm", "I have an OS lab record due Friday", "what's due" |
| Maths | "what's 15 percent of 2400", "25 times 4", "square root of 144" |
| Calls | "answer", "decline", "mute me" |
| DyIsd | "hide the island", "move the island left", "open control center", "go to sleep" (stops listening for its name), "help" |

Coming next: playing a song by name in Apple Music, WhatsApp messages (with a confirm step),
Discord deafen/join, and typing prompts into Claude.

## Honest limits

- **Ringing calls, Windows Clock and file transfers** are read off those apps' windows with
  UI Automation (what screen readers use), because Windows has no API for them. They depend on
  how each app labels its buttons and may need tuning on your PC: `log.txt` records what DyIsd
  saw (lines starting `ring sees`, `clock sees`, `transfer sees`). Send those if one doesn't show.
- **Listen for "Jarvis"** keeps the microphone open, so Windows shows its mic icon. Every phrase
  it hears is turned into text on your laptop to check for the name; nothing is sent anywhere.
  It pauses during calls. Turn it off and Ctrl + Space still works.
- **Volume pop-up:** Windows 11's own pop-up can't be turned off, so the island only shows
  changes DyIsd made (keys, scroll, Jarvis); headphone buttons show only Windows' pop-up.

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
  Services/              one file per Windows feature (media, volume, battery, ringing, Clock, transfers...)
  Voice/                 Jarvis: microphone, speech-to-text (Whisper), the command list, and what each command does
  Controls/              hand-drawn progress ring, progress bar, equalizer bars
  Settings/              settings saved as JSON
  ControlCenter.xaml     the black panel with toggles, deadlines and options
packaging/               MSIX installer manifest, build and install scripts
prototype/island.html    the clickable design prototype (open in any browser)
.github/workflows/       GitHub Actions: builds the installer on every push
```

Built with C#, .NET 10 and WPF. Libraries: NAudio (volume, microphone), Ical.Net (calendar),
System.Management (brightness), Whisper.net (speech to text, on the GPU through Vulkan).

## Build it yourself

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/DyIsd
```

See `LEARN-LATER.md` for the learning checklist.
