using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DyIsd.Native;

namespace DyIsd.Services;

public enum ClipKind { Text, Files, Image }

public sealed record ClipInfo(ClipKind Kind, string Text, int Count);

/// <summary>
/// Gets told by Windows every time something is copied. Skips anything a password manager
/// marks as private.
/// </summary>
public sealed class ClipboardService
{
    public event Action<ClipInfo>? Copied;

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    string? _lastText;
    DateTime _lastAt;

    public void Attach(MessageWindow window)
    {
        if (!Win32.AddClipboardFormatListener(window.Handle))
        {
            Log.Write("clipboard listener failed: " + Marshal.GetLastWin32Error());
            return;
        }
        window.Message += (msg, _, _) =>
        {
            // Wait a moment so the copying app finishes writing before we read.
            if (msg == Win32.WM_CLIPBOARDUPDATE) _ui.BeginInvoke(DispatcherPriority.Background, Read);
        };
    }

    void Read()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                ReadOnce();
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40); // another app still has the clipboard open
            }
            catch (Exception ex)
            {
                Log.Error("clipboard", ex);
                return;
            }
        }
    }

    void ReadOnce()
    {
        var data = Clipboard.GetDataObject();
        if (data == null) return;

        // Password managers set these flags so clipboard tools ignore secrets.
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
            data.GetDataPresent("Clipboard Viewer Ignore")) return;
        if (data.GetDataPresent("CanIncludeInClipboardHistory") &&
            data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4)
        {
            var b = new byte[4];
            ms.Position = 0;
            ms.ReadExactly(b, 0, 4);
            if (BitConverter.ToInt32(b, 0) == 0) return;
        }

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string ?? "";
            if (text.Length == 0) return;
            if (text == _lastText && (DateTime.Now - _lastAt).TotalSeconds < 1.5) return; // some apps fire twice
            _lastText = text;
            _lastAt = DateTime.Now;
            Copied?.Invoke(new ClipInfo(ClipKind.Text, text, text.Length));
            return;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            Copied?.Invoke(new ClipInfo(ClipKind.Files, Path.GetFileName(files[0]), files.Length));
            return;
        }

        if (data.GetDataPresent(DataFormats.Bitmap))
            Copied?.Invoke(new ClipInfo(ClipKind.Image, "", 1));
    }
}
