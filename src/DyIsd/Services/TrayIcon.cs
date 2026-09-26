using System;
using System.Drawing;
using System.Reflection;
using WF = System.Windows.Forms;

namespace DyIsd.Services;

/// <summary>The DyIsd icon in the taskbar corner, with its right-click menu.</summary>
public sealed class TrayIcon : IDisposable
{
    public event Action? OpenSettings, ToggleTimer, WhatsNext, Quit;
    public event Action<bool>? PauseChanged;

    readonly WF.NotifyIcon _icon;
    readonly WF.ToolStripMenuItem _timerItem, _pauseItem;

    public TrayIcon()
    {
        WF.Application.EnableVisualStyles();
        _timerItem = new WF.ToolStripMenuItem("Start focus session", null, (_, _) => ToggleTimer?.Invoke());
        _pauseItem = new WF.ToolStripMenuItem("Pause island", null, (_, _) =>
        {
            _pauseItem!.Checked = !_pauseItem.Checked;
            PauseChanged?.Invoke(_pauseItem.Checked);
        });

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(_timerItem);
        menu.Items.Add(new WF.ToolStripMenuItem("What's next", null, (_, _) => WhatsNext?.Invoke()));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(new WF.ToolStripMenuItem("Settings", null, (_, _) => OpenSettings?.Invoke()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(new WF.ToolStripMenuItem("Quit DyIsd", null, (_, _) => Quit?.Invoke()));

        _icon = new WF.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DyIsd",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenSettings?.Invoke();
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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
