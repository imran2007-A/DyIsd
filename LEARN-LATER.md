# Learn Later — DyIsd

Imran chose "just build it". This is what to come back and actually understand, so you can
explain every part of this project in an interview. Tick items off as you go. Each one points
at the file where it happens.

## Must know before putting this on your resume
- [ ] What WPF is: a XAML file describes the UI, the `.xaml.cs` file next to it is the code
      → `src/DyIsd/Island/IslandWindow.xaml` + `IslandWindow.xaml.cs`
- [ ] How the island stays on top, is see-through, and never steals focus
      → `IslandWindow.xaml` (AllowsTransparency, Topmost) and `SetExStyle()`
- [ ] How clicks pass through the island until you hold Alt (WS_EX_TRANSPARENT + cursor polling)
      → `IslandWindow.xaml.cs` → `Track()`
- [ ] The spring animation: an easing function is just math → `Controls/SpringEase.cs`
- [ ] How the app decides what the island shows (newest activity, hidden if you're in its app)
      → `src/DyIsd/Island/IslandController.cs` → `PickActivity()`
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
- [ ] Guessing "AI is working" from CPU time → `Services/AiActivityService.cs`
- [ ] Mic/camera in use: the registry Windows keeps for privacy → `Services/PrivacyService.cs`
- [ ] Bluetooth earbuds: `DeviceWatcher` → `Services/EarbudsService.cs`
- [ ] Building UI in code instead of XAML → `ControlCenter.xaml.cs` (tiles, deadline rows)
- [ ] Clipboard: `AddClipboardFormatListener` + a message-only window → `Services/ClipboardService.cs`, `MessageWindow.cs`
- [ ] Calendar: downloading and parsing an .ics file → `Services/CalendarService.cs`
- [ ] Downloads: `FileSystemWatcher` and browser temp files → `Services/DownloadService.cs`
- [ ] Global hotkey (Ctrl+Alt+F): `RegisterHotKey` → `App.xaml.cs`
- [ ] P/Invoke: calling raw Windows functions from C# → `Native/Win32.cs`

## Packaging
- [ ] What MSIX is, what a certificate is, and why Install.bat trusts one → `packaging/`

## Prototype
- [ ] Open `prototype/island.html` and read how `render()` picks what the island shows
