# Learn Later — DyIsd

Imran chose "just build it". This is the list of things to come back and actually understand,
so this project is explainable in an interview. Tick them off as you go.

## Before interviews (must know)
- [ ] What WPF is, and how a XAML window + C# code-behind work together
- [ ] How the island window stays on top, has no border, and ignores Alt+Tab
- [ ] How Windows tells us what's playing (GlobalSystemMediaTransportControls / "GSMTC")
- [ ] Events vs polling: why the app listens for changes instead of checking every second
- [ ] async/await in C# — why calls to Windows APIs are awaited
- [ ] Git basics: commit, branch, push, pull request

## Nice to know
- [ ] How volume changes are detected (Core Audio, via NAudio)
- [ ] How brightness is read/changed (WMI)
- [ ] How battery/charging state is read
- [ ] Why reading notifications needs a packaged (MSIX) app and user permission
- [ ] How the distraction nudge reads the browser tab title
- [ ] Animations in WPF (Storyboards, easing functions)
- [ ] Saving settings to a JSON file

## Prototype
- [ ] Open `prototype/island.html` and read how `render()` picks what the island shows
