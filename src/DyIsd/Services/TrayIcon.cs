using System;
using System.Drawing;
using System.Reflection;
using WF = System.Windows.Forms;

namespace DyIsd.Services;

/// <summary>
/// The DyIsd icon in the taskbar corner. Left-click opens the Control Center,
/// right-click shows quick actions.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public event Action? OpenControlCenter, ToggleTimer, WhatsNext, Quit;
    public event Action<bool>? PauseChanged;

    readonly WF.NotifyIcon _icon;
    readonly WF.ToolStripMenuItem _timerItem, _pauseItem;

    public TrayIcon()
    {
        WF.Application.EnableVisualStyles();
        // Errors inside tray-menu clicks go to the log instead of a "Microsoft .NET" error box.
        WF.Application.ThreadException += (_, e) => Log.Error("tray", e.Exception);
        _timerItem = new WF.ToolStripMenuItem("Start focus session", null, (_, _) => ToggleTimer?.Invoke());
        _pauseItem = new WF.ToolStripMenuItem("Pause island", null, (_, _) => PauseChanged?.Invoke(!_pauseItem!.Checked));

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(new WF.ToolStripMenuItem("Open Control Center", null, (_, _) => OpenControlCenter?.Invoke()) { Font = new Font(WF.Control.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(_timerItem);
        menu.Items.Add(new WF.ToolStripMenuItem("What's next", null, (_, _) => WhatsNext?.Invoke()));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WF.ToolStripMenuItem("Quit DyIsd", null, (_, _) => Quit?.Invoke()));

        _icon = new WF.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DyIsd · click for Control Center",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) OpenControlCenter?.Invoke(); };
    }

    static Icon LoadIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DyIsd.ico");
            if (s != null) return new Icon(s, WF.SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void SetTimer(bool active, int minutes) =>
        _timerItem.Text = active ? "End focus session" : $"Start focus session ({minutes} min)";

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
