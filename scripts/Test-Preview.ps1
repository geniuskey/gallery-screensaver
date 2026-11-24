param([Parameter(Mandatory = $true)][string]$ScreenSaver)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class PreviewSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr parent, uint command);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    private static bool FillsHost(Form host)
    {
        const uint GW_CHILD = 5;
        Rect bounds;
        return GetClientRect(GetWindow(host.Handle, GW_CHILD), out bounds)
            && bounds.Right == host.ClientSize.Width && bounds.Bottom == host.ClientSize.Height;
    }

    private static void WaitFor(Func<bool> done, string failure)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (done()) return;
            Thread.Sleep(20);
        } while (clock.ElapsedMilliseconds < 5000);
        throw new InvalidOperationException(failure);
    }

    public static void Run(string screenSaver)
    {
        using (var host = new Form { ClientSize = new Size(152, 112), Text = "Gallery preview test" })
        {
            host.Show();
            var startInfo = new ProcessStartInfo(screenSaver, "/p " + host.Handle.ToInt64()) { UseShellExecute = false };
            using (var preview = Process.Start(startInfo))
            {
                try
                {
                    WaitFor(() => FillsHost(host), "Preview did not fill its parent window.");
                    host.ClientSize = new Size(215, 135);
                    WaitFor(() => FillsHost(host), "Preview did not resize with its parent window.");
                    host.Close();
                    WaitFor(() => preview.HasExited, "Preview did not exit after its parent closed.");
                    if (preview.ExitCode != 0) throw new InvalidOperationException("Preview exit code " + preview.ExitCode);
                }
                finally
                {
                    if (!preview.HasExited) preview.Kill();
                }
            }
        }
    }
}
'@

[PreviewSmoke]::Run((Resolve-Path -LiteralPath $ScreenSaver).Path)
'Preview filled its parent, resized and exited successfully.'
