# DyIsd

A dynamic island for Windows 11, modeled on the iPhone's. A small black pill at the top of your
screen that appears when something is running in the background and hides when nothing is.
Anything that belongs to the app you're already in stays hidden: YouTube playing in Chrome
doesn't show while you're in Chrome.

| Feature | What it shows |
|---|---|
| Now playing | Song, artist, album art. Light in the cover's colour spills out from under the island and breathes slowly (switch it off in Options). Click to open the player: seek, shuffle, repeat, volume, **Play on…** (switch between speakers, headphones and earbuds, like AirPlay), open the app. Only while it's playing: pause from Spotify and it's gone; pause from the island and it shrinks to a tiny dot you can click to resume. |
| Calls | WhatsApp, Discord, Teams, Zoom, Telegram: green pill with the call timer and your voice level. Click for who you're talking to, Mute and Go to call. Your Discord mute shortcut shows up on the island too. |
| Incoming calls | WhatsApp / Discord / Teams / Telegram ringing: caller, a red Decline and a green Answer button, like iPhone. Best effort: DyIsd presses the app's own buttons (see "Honest limits"). |
| Jarvis | Voice control. Hold **Ctrl + Space** and talk, or just say "Jarvis, …". Opens apps and sites, searches, types, presses shortcuts, scrolls, controls music, volume, brightness, Wi-Fi, Bluetooth, dark mode, timers and reminders, answers calls. No AI, nothing leaves your laptop. |
| Silent switch | Muting shows a red bell that wiggles, like flipping the iPhone's switch. |
| Windows Clock | The Clock app's running timer (orange) or stopwatch, while Clock is open (minimized is fine). |
| File transfers | Bluetooth and Nearby Share sends and receives with progress. |
| Volume | Replaces the Windows volume pop-up. |
| Brightness | Pops up when laptop brightness changes. |
| Battery | Plug in: a ring fills up to your battery level, like the iPhone. Unplugged, and warnings at 15% and 5%. |
| Camera | Green dot while an app uses your camera, like the iPhone. |
| Earbuds | A card when Bluetooth earbuds or headphones connect, with battery level when Windows knows it. |
| Focus timer | Focus sessions of 15–60 min. **Win + Shift + F** or the Control Center starts one. |
| Clipboard | What you just copied. Password-manager copies are never shown. |
| Classes & deadlines | Reminders from your Google Calendar link and deadlines you add. For each deadline you pick when to be reminded: 1 week / 1 day / 2 hours / 30 min before or when due, daily or weekly until it's due, and extra dates. |
| Downloads | While a browser downloads, then "Download complete" with Open / Folder. |
| Claude Code | **Thinking → Editing → Running → Done** with a timer, the file it's editing or the command it's running, and the task you gave it. "Needs you" when it's waiting for your permission. Hidden while you're in the app it runs in (terminal, VS Code, Claude); right-click jumps there. Turn on with Control Center → Options → **Claude Code status → Connect**. |
| Face | Two little eyes when nothing else is on. They blink, look busy (orange) while Claude Code works, smile (green ^ ^) when it's done, and turn into hearts during focus. Click the face for Home. |
| Home | **Win + Shift + H** (or click the face): today's date, cards for Claude Code, music and reminders, and tabs for Claude, the music player, **reminders**, notifications, focus timers, a **file tray** (drop files on the island, drag them out anywhere later), **clipboard history** (click to copy again) and **notes**. Esc or moving away closes it. |

## Using it

The most important activity fills the island: call → ringing call / Clock timer → music →
stopwatch / focus timer → downloads / transfers. The next one sits in a small circle beside it;
click the circle (or swipe sideways with two fingers on the touchpad) to swap them. Nothing shows while you're in the app it belongs to: YouTube in
Chrome shows once you switch away from Chrome.

| | |
|---|---|
| Click | Open (the player, the call controls…); click again to close |
| Right-click | Jump to the app (Spotify, Chrome, WhatsApp…) |
| Drag sideways | Move it to the left, center or right |
| Drag up (flick) | Hide what's showing until it changes (next song, next call…) |
| Scroll | Volume (Shift too for brightness) |
| Two-finger swipe sideways | Switch to the activity in the circle |
| Drop files on it | Keeps them in Home's tray |
| Win + Shift + H | Home |

**More than one monitor:** the island follows you to whichever screen the app you're using is on.

**Not colour-only:** states that colour shows are shown by shape too: a dot under Shuffle and
Repeat when they're on, a tick next to the speaker that's playing, an arrow in the transfer
ring so it isn't confused with the timer ring, the bolt and the % number while charging.

**Control Center:** click the DyIsd icon in the taskbar corner. Turn features on and off, start
a focus session, add deadlines, paste your calendar link, pick the position.
Windows 11 may hide new tray icons under the `^` arrow; drag DyIsd out onto the taskbar to keep it visible.

**Always on:** DyIsd starts with Windows and looks after itself: if it ever crashes or gets
closed by something else, it's back within a few seconds. Only **Quit** (tray icon) stops it.

## Jarvis

Hold **Ctrl + Space**, say the command, let go. Or, with "Listen for Jarvis" on, just say
"Jarvis, open Spotify". The first time, it downloads its speech model once (470 MB) and then
works offline. It runs on your graphics card when it can.

**Sharper hearing:** with a Groq key (see "AI brain" below) and Options → **Sharper hearing** on,
what you say to Jarvis is turned into text by Groq's Whisper large-v3-turbo, the same idea as Wispr Flow.
It's far more accurate than the laptop's own model and takes about half a second. Always-listening
audio is still checked for "Jarvis" on the laptop first, so only commands are ever sent. The laptop
then only needs a small wake-word model (140 MB instead of 470 MB, downloaded quietly once), which
also cuts DyIsd's memory to about a quarter. Without a key everything stays on the laptop as before.
Whisper's habit of inventing sentences out of background noise ("Subs by…", "Thanks for watching")
is filtered out either way.

Things you can say (and many ways to say each):

| | |
|---|---|
| Apps & sites | "open chrome", "launch spotify", "switch to vs code", "open leetcode", "open github.com", "open wifi settings", "open downloads", "close discord" |
| Search | "search for cats", "search youtube for lofi", "look up recursion on wikipedia", "images of red pandas", "directions to SRM Ramapuram", "weather in Chennai", "translate good morning to Tamil", "who is…/how to…" |
| Typing & keys | "type hello and press enter", "new tab", "close this tab", "reopen the last tab", "go to tab 3", "copy", "paste", "undo", "select all", "save", "press control shift t", "press page down 3 times", "take a screenshot" (the window you're in, saved to Pictures\Screenshots and copied; "snip" opens the snipping tool), "show desktop", "minimize this", "snap left" |
| Scrolling | "scroll down", "scroll up a lot", "scroll down 3", "scroll to the top" |
| Music & YouTube | "play", "pause", "next song", "previous", "restart the song", "shuffle", "what's playing", "play Believer" (plays the top YouTube video), "search youtube for lofi" (just the search page), "play Apple Music", "play from my playlist" / "play my songs" (your Library → Songs list from the top), "shuffle" / "shuffle my songs" (Shuffle on, from a random song), "play Believer on Apple Music" (searches Apple Music and plays the first match), "watch Friends on Prime Video", "turn off YouTube" |
| Sound & screen | "volume 40", "volume up a bit", "louder", "mute", "brightness 70", "dimmer" |
| PC | "turn off wifi", "bluetooth on", "airplane mode", "dark mode", "lock the computer", "shut down / restart" (asks first), "empty the recycle bin" (asks first) |
| Reminders list | "remind me to buy milk", "remind me to call mom at 6 pm", "add eggs to my list", "tick off buy milk", "I did the DBMS assignment", "untick buy milk", "delete the milk reminder", "what are my reminders", "what's left to do" |
| Time & reminders | "what time is it", "what's the date", "how much battery", "set a timer for 5 minutes", "start focus for 40 minutes", "remind me to submit DBMS tomorrow at 5 pm", "I have an OS lab record due Friday", "what's due" |
| Maths | "what's 15 percent of 2400", "25 times 4", "square root of 144" |
| Calls | "answer", "decline", "mute me", "hang up" (Discord, WhatsApp, Teams, Zoom) |
| WhatsApp | "message Rahul saying I'm late", "text Amma that I'll be home by 8", "type hi to Rahul on WhatsApp", "call Amma", "video call Rahul". Save each person once in Control Center → **WhatsApp contacts** (name + number): Jarvis opens their chat with WhatsApp's own link, so it can't land in the wrong chat, then clicks into the message box and types (messages) (messages), or opens their chat for a call and leaves the call button to you (calls). Always asks "Send to Rahul?" first; nothing is sent until you say yes or click Yes. Save the people you message: the newer WhatsApp app hides its chat list from Windows, so unsaved names can't be found |
| Discord | "message general on Discord saying I'm on in 5", "send gg to MRGorilla on Discord", "message announcements in the Game Verse server on Discord saying hi". Channels go to your usual server (Locked in, `DiscordServer` in settings.json) unless you name one; otherwise it's a DM. Jarvis opens the chat with Discord's quick switcher, checks the title shows what you asked for, asks "Send to #general in Locked in?", and only types and sends on yes. "deafen" / "undeafen", "join general voice", "join voice chat", "leave the voice channel", "call MRGorilla on Discord" (asks first). The island's Mute button and mute state now come from Discord's own button |
| Claude | "ask Claude to explain recursion" (types it, doesn't send), "… and send it" (sends), "new Claude chat", "start a new chat in Claude and ask it to summarise my notes" |
| Modes | Say "Claude mode" / "start Claude mode" to run one of your modes, "end Claude mode" to stop it (closes the apps it opened, pauses the music). "Play my Chill Vibes playlist" plays an Apple Music playlist |
| DyIsd | "hide the island", "move the island left", "open control center", "go to sleep" (stops listening for its name), "help" |

**Jarvis key:** Ctrl + Space by default. Change it in Control Center → Options → **Jarvis key**
(Ctrl + Space blocks VS Code's autocomplete; Right Alt on its own is a good swap).

**Any installed app or game works with "open / launch / start …"**: everything in your Start
menu, plus desktop shortcuts, Epic Games titles (Fortnite…) and Steam games. The list refreshes
every 30 minutes, so newly installed apps show up on their own. If Jarvis keeps mishearing an
unusual name, make a mode with that spoken name and one "Open app" step.

A single word on its own ("Discord") only opens apps that are already open, have a nickname
DyIsd knows, or are known websites, so a misheard word never launches a random app. Say
"open …" for anything else. On a call, "mute" / "unmute" means your microphone.

## Modes

Control Center → **Modes** → **+ New mode**. Give it a name, then add steps in any order and
any combination:

| Step | Example |
|---|---|
| Open app / site | Claude, VS Code, leetcode.com |
| Apple Music playlist / song | Chill Vibes / Believer |
| Play on YouTube | lofi hip hop (plays the top video) |
| Volume / Brightness | 30 / 70 |
| Focus timer | 50 minutes |
| Dark / light, Wi-Fi, Bluetooth | dark, off, on |
| Wait | 3 seconds (let a slow app load before the next step) |
| Type text | exactly what to type |
| Close app | Discord |
| Any Jarvis command | anything you'd say: "ask claude to plan my day", "message Rahul saying I'm studying" |

Run it by saying its name ("Claude mode", "start Claude mode") or with ▶ next to it, no voice
needed. "End Claude mode" pauses its music and closes the apps it opened (closing can be
switched off per mode). A step can be "My Apple Music songs" (shuffled or in order), e.g. open
Claude → shuffle my songs → open Claude. Steps run in order; one that fails is skipped and the island says which. WhatsApp
messages and calls inside a mode never send without your yes, so they're skipped in a mode.

### AI brain (optional, free)

Jarvis works without AI: it matches what you say against its rules. With a free
[Groq](https://console.groq.com/keys) key pasted in Control Center → Options → **AI brain**, two
things change:
- **Loose phrasing works.** "It's too bright", "I can't hear anything", "put something relaxing on":
  the brain rewrites it as normal Jarvis commands, which then run through the same rules (so
  "Send to Rahul?" and "Shut down?" still ask first).
- **Questions get answers** on the island instead of a Google search: "what's the capital of
  France", "how many grams in a pound", "tell me a joke".

Only phrases the rules don't understand, and questions, are sent to Groq, with the time, the
app you're in, your app names, mode names and contact names (no numbers). Your key is
encrypted with your Windows account. The free tier allows about 1,000 requests a day. Without a
key nothing leaves your laptop.

## Reminders (and your iPhone)

Home → **Reminders** is a checklist. Type one and press Enter ("call mom at 6 pm" keeps the time),
tick the circle when it's done, × deletes it. Jarvis can add, tick off and read them too. One with a
time pops up on the island and chimes when it's due. Ticked ones stay, struck through, until the day
ends. They're saved in `reminders.json` next to the log.

**On your iPhone** (optional): the same list, synced through your own free Supabase project.

1. Make a project at [supabase.com](https://supabase.com). In **SQL Editor**, run:
   ```sql
   create table reminders (
     id text primary key, text text not null, due timestamptz, done boolean not null default false,
     done_at timestamptz, updated bigint not null, deleted boolean not null default false);
   alter table reminders enable row level security;
   create policy "my reminders" on reminders for all to anon using (true) with check (true);
   ```
2. **Project Settings → API**: copy the **Project URL**, then in DyIsd's Control Center press
   **Paste URL**. Copy the **anon / publishable key**, press **Paste key**. The dot turns green.
3. Open the reminders page, <https://imran2007-a.github.io/DyIsd/web/reminders/>, in Safari on the
   iPhone, paste the same URL and key once, then **Share → Add to Home Screen**.

Laptop and phone sync every 15–20 seconds; if both change the same reminder, the newest change wins.
Anyone with the URL *and* the key could read the list, so keep the key to your own devices.

## Honest limits

- **WhatsApp, Discord, Claude, Apple Music play-by-name, ringing calls, Windows Clock and file
  transfers** are read off those apps' windows with
  UI Automation (what screen readers use), because Windows has no API for them. They depend on
  how each app labels its buttons and may need tuning on your PC: `log.txt` records what DyIsd
  saw (lines starting `whatsapp sees`, `discord sees`, `claude sees`, `apple music sees`, `ring sees`, `clock sees`, `transfer sees`; see "Where things are" for the folder). Send those if one doesn't show.
- **WhatsApp (the newer app)** draws everything inside a web view that Windows can't see into
  (no buttons or text for UI Automation, and it's blank in screenshots). So messages and calls go
  through WhatsApp's own `whatsapp://send` link plus a click into the message box and typing.
  Calls can't be started for you: WhatsApp 2.26 ignores simulated clicks on its call buttons
  (and the Ctrl + Shift + C / V shortcuts some guides list do nothing), so "call Rahul" opens
  Rahul's chat and the island says to tap the call button.
- **Notifications** in Home only work in the installed app (Windows ties the notification
  listener to the app's identity), and Windows asks you once to allow it.
- **Claude Code status** needs Claude Code's hooks (Connect adds them to `~/.claude/settings.json`,
  keeping a copy as `settings.json.before-dyisd`; Disconnect takes them back out). The hook is one
  `curl` to `127.0.0.1:47811` that gives up after a second, so Claude Code is never slowed when
  DyIsd isn't running. New Claude Code sessions pick it up.
- **Listen for "Jarvis"** keeps the microphone open, so Windows shows its mic icon. Every phrase
  it hears is turned into text on your laptop to check for the name; nothing is sent anywhere.
  It keeps listening during calls. Turn it off and Ctrl + Space still works.
- **Play on…** uses the same hidden Windows switch that the Sound settings page and apps like
  EarTrumpet use (Windows has no official one); it moves music, system sounds and calls together.
- **AI brain answers can be wrong**, like any AI. Facts that change (prices, rates, scores) may
  be out of date. Commands it writes still go through the rules, so it can't do anything
  Jarvis couldn't do by voice.
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
| Settings file | `settings.json` in DyIsd's data folder (below) |
| Log (send this if something breaks) | `log.txt` in DyIsd's data folder |

DyIsd's data folder (or click **Log folder** at the bottom of the Control Center):
- **Installed with Install.bat:** Windows keeps installed apps' files private, so it's at
  `%LOCALAPPDATA%\Packages\DyIsd_<letters>\LocalCache\Local\DyIsd`
  (press Win + R, type `%LOCALAPPDATA%\Packages`, open the folder starting with `DyIsd_`).
- **Portable version:** `%LOCALAPPDATA%\DyIsd`

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

Two self-checks, no window needed:

- `DyIsd.exe --hear clip.wav` runs a 16 kHz mono recording through Jarvis's ears (laptop and
  Groq) and the command rules, and writes what each heard and what it would do to `log.txt`.
- `DyIsd.exe --whatsapp-test message NAME "text"` runs Jarvis's WhatsApp message flow for a saved
  contact with the "Send to …?" already answered yes (it really sends), logging each step.
- `DyIsd.exe --demo VIEW out.png` saves a picture of one island view with sample data
  (`face`, `media`, `media-e`, `claude`, `claude-e`, `timer`, `home`, `home-tray`).

See `LEARN-LATER.md` for the learning checklist.
