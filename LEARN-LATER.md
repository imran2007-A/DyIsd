# Learn Later — DyIsd

Imran chose "just build it". This is what to come back and actually understand, so you can
explain every part of this project in an interview. Tick items off as you go. Each one points
at the file where it happens.

## Must know before putting this on your resume
- [ ] What WPF is: a XAML file describes the UI, the `.xaml.cs` file next to it is the code
      → `src/DyIsd/Island/IslandWindow.xaml` + `IslandWindow.xaml.cs`
- [ ] How the island stays on top, is see-through, and never steals focus
      → `IslandWindow.xaml` (AllowsTransparency, Topmost) and `SetExStyle()`
- [ ] Routed events: how the seek bar tells the window you let go → `Controls/ProgressLine.cs`
- [ ] The spring animation: an easing function is just math → `Controls/SpringEase.cs`
- [ ] How the app decides what the island shows (priority order, second one in the circle,
      hidden if you're in its app, flicked ones hidden until they change)
      → `src/DyIsd/Island/IslandController.cs` → `PickActivities()` and `Signature()`
- [ ] Data binding: `{Binding Title}` in XAML + `INotifyPropertyChanged` in C#
      → `Island/Models.cs`
- [ ] Events: services raise events, `App.xaml.cs` connects them to the island
      → `App.xaml.cs` → `Wire()`
- [ ] async/await: why Windows API calls are awaited
      → `Services/MediaService.cs`
- [ ] Threads: why every Windows callback does `_ui.BeginInvoke(...)`
- [ ] Git: commit, branch, push, pull request. GitHub Actions: what `build.yml` does

## How each feature talks to Windows
- [ ] Now playing: `GlobalSystemMediaTransportControlsSessionManager` → `Services/MediaService.cs`
- [ ] Volume: Core Audio via NAudio → `Services/VolumeService.cs`
- [ ] Hiding the Windows volume pop-up: low-level keyboard hook → `Services/VolumeKeyHook.cs`
- [ ] Brightness: WMI queries and events → `Services/BrightnessService.cs`
- [ ] Battery: polling power status → `Services/BatteryService.cs`
- [ ] Which app you're in: `SetWinEventHook` → `Services/ForegroundWatcher.cs`
- [ ] Drawing an animation in code → `Controls/Equalizer.cs` (the music bars)
- [ ] Mic/camera in use: the registry Windows keeps for privacy → `Services/PrivacyService.cs`
- [ ] Calls: "a call app holds the mic" + reading window titles → `Services/CallService.cs`
- [ ] Muting the mic and reading its level (Core Audio) → `Services/MicService.cs`
- [ ] Pressing a keyboard shortcut from code (Discord mute) → `Native/Win32.cs` → `PressShortcut()`
- [ ] Bluetooth earbuds: `DeviceWatcher` → `Services/EarbudsService.cs`
- [ ] Building UI in code instead of XAML → `ControlCenter.xaml.cs` (tiles, deadline rows)
- [ ] Clipboard: `AddClipboardFormatListener` + a message-only window → `Services/ClipboardService.cs`, `MessageWindow.cs`
- [ ] Calendar: downloading and parsing an .ics file → `Services/CalendarService.cs`
- [ ] Downloads: `FileSystemWatcher` and browser temp files → `Services/DownloadService.cs`
- [ ] Global hotkey (Ctrl+Alt+F): `RegisterHotKey` → `App.xaml.cs`
- [ ] P/Invoke: calling raw Windows functions from C# → `Native/Win32.cs`

- [ ] UI Automation: reading and pressing buttons in other apps (ringing calls, Clock, transfers)
      → `Services/Uia.cs`, `Services/RingService.cs`, `Services/ClockService.cs`, `Services/TransferService.cs`
- [ ] Which apps are making sound (Core Audio sessions) → `Services/AudioSessions.cs`
- [ ] Animations: transform groups, keyframes, the liquid morph and flick
      → `IslandWindow.xaml.cs` → `Wobble()`, `Flick()`, `Squish()`, `SetBubble()`

## Jarvis (voice control)
- [ ] Recording the mic and cutting speech into phrases (voice activity detection: loudness vs
      the room's noise) → `Voice/AudioInput.cs`
- [ ] Speech to text with Whisper, and what "running on the GPU with Vulkan" means → `Voice/SpeechEngine.cs`
- [ ] Regular expressions: how hundreds of phrasings become one command → `Voice/CommandParser.cs`
      (start with `BuildRules()`; try changing a phrase and running the parser test)
- [ ] Parsing dates like "tomorrow at 5 pm" → `CommandParser.ParseReminder()`
- [ ] A tiny calculator (recursive descent parser) → `CommandParser.Calc()` / `Expr()` / `Term()`
- [ ] Pretending to be the keyboard and mouse (SendInput) → `Voice/InputSim.cs`
- [ ] Catching Ctrl + Space system-wide and swallowing the Space → `App.xaml.cs` → `KeyHook.Intercept`
- [ ] Making a sound wave in code (a WAV file is just numbers) → `Voice/Chime.cs`
- [ ] Driving other apps like a person (find the box, type, press the button) → `Voice/AppDriver.cs`,
      then see it used in `Voice/WhatsApp.cs`, `Services/DiscordControl.cs`, `Voice/ClaudeApp.cs`
- [ ] Why WhatsApp asks before sending: confirm with the real name found, never guess → `WhatsApp.MessageAsync()`
- [ ] Reading a web page without a browser (the top YouTube video) → `CommandRunner.YouTubePlayAsync()`
- [ ] Modes: data (a list of steps) turned into commands → `Voice/Modes.cs`, run by
      `CommandRunner.RunModeAsync()`; the builder UI → `ControlCenter.xaml.cs` (search "modes")
- [ ] Later, adding AI: "select, don't generate" (the AI picks one of these commands, it never
      runs anything it made up) → ask Claude when you get here

## Packaging
- [ ] What MSIX is, what a certificate is, and why Install.bat trusts one → `packaging/`

## Prototype
- [ ] Open `prototype/island.html` and read how `render()` picks what the island shows
