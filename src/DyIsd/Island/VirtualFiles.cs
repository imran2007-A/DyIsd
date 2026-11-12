using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DyIsd.Island;

/// <summary>
/// Files dragged out of apps like WhatsApp or Outlook aren't real files: the app hands over
/// their names (FileGroupDescriptorW) and their bytes (FileContents) instead of a path. This
/// saves them into a folder and returns the saved paths.
/// </summary>
static class VirtualFiles
{
    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM m);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);

    public static List<string> Save(System.Windows.IDataObject data, string folder)
    {
        var saved = new List<string>();
        if (data is not IDataObject com || !data.GetDataPresent("FileGroupDescriptorW")) return saved;
        if (data.GetData("FileGroupDescriptorW") is not MemoryStream desc) return saved;
        var d = desc.ToArray();
        int count = BitConverter.ToInt32(d, 0);
        short fmt = (short)System.Windows.DataFormats.GetDataFormat("FileContents").Id;
        Directory.CreateDirectory(folder);
        for (int i = 0; i < count && 4 + (i + 1) * 592 <= d.Length; i++)
        {
            // FILEDESCRIPTORW is 592 bytes; the name starts 72 bytes in.
            var name = Encoding.Unicode.GetString(d, 4 + i * 592 + 72, 520).Split('\0')[0];
            name = Path.GetFileName(name);
            if (name.Length == 0) continue;
            var target = Unique(folder, name);
            var fe = new FORMATETC { cfFormat = fmt, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = i, tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL };
            try
            {
                com.GetData(ref fe, out var m);
                try
                {
                    using var f = File.Create(target);
                    if (m.tymed == TYMED.TYMED_ISTREAM) Copy((IStream)Marshal.GetObjectForIUnknown(m.unionmember), f);
                    else
                    {
                        var p = GlobalLock(m.unionmember);
                        var buf = new byte[(int)GlobalSize(m.unionmember)];
                        Marshal.Copy(p, buf, 0, buf.Length);
                        GlobalUnlock(m.unionmember);
                        f.Write(buf);
                    }
                }
                finally { ReleaseStgMedium(ref m); }
                saved.Add(target);
            }
            catch (Exception ex)
            {
                Log.Error("tray virtual file", ex);
                try { File.Delete(target); } catch { }
            }
        }
        return saved;
    }

    static void Copy(IStream s, Stream to)
    {
        var buf = new byte[1 << 16];
        var read = Marshal.AllocHGlobal(4);
        try
        {
            while (true)
            {
                s.Read(buf, buf.Length, read);
                int n = Marshal.ReadInt32(read);
                if (n <= 0) break;
                to.Write(buf, 0, n);
            }
        }
        finally { Marshal.FreeHGlobal(read); }
    }

    public static string Unique(string folder, string name)
    {
        var p = Path.Combine(folder, name);
        for (int n = 2; File.Exists(p); n++)
            p = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        return p;
    }
}
