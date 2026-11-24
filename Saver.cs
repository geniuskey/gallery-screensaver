using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace GallerySaver;

internal static class Loc
{
    public static readonly bool Korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
    public static string L(string ko, string en) => Korean ? ko : en;
}

/// <summary>Covers the desktop until the user returns.</summary>
internal static class Saver
{
    private const int MoveThreshold = 10;
    private const int GraceMs = 1200;

    private static Task<CoreWebView2Environment>? environment;
    private static readonly string Profile = Path.Combine(Program.DataFolder, "ExhibitionWebView2");

    public static Task<CoreWebView2Environment> Environment()
    {
        if (environment == null || environment.IsFaulted) environment = CreateEnvironment();
        return environment;
    }

    private static async Task<CoreWebView2Environment> CreateEnvironment()
    {
        // Resolve automatic proxy settings through Windows, including PAC and WPAD.
        var options = new CoreWebView2EnvironmentOptions(
            "--autoplay-policy=user-gesture-required --use-system-proxy-resolver --enable-features=SystemProxyResolver");
        var env = await CoreWebView2Environment.CreateAsync(null, Profile, options);
        env.BrowserProcessExited += (_, _) =>
        {
            if (environment?.Status == TaskStatus.RanToCompletion && environment.Result == env) environment = null;
        };
        return env;
    }

    public static void Run(Settings settings, Exception? problem)
    {
        var message = problem == null
            ? null
            : Loc.L("화면 보호기를 시작하지 못했습니다.\n", "The screen saver could not start.\n") + problem.Message;
        var screens = Screen.AllScreens;
        var forms = settings.SpanDisplays && screens.Length > 1
            ? new[]
            {
                new SaverForm(SystemInformation.VirtualScreen, "virtual screen", true,
                    problem == null ? settings : null, message, Screen.PrimaryScreen.Bounds),
            }
            : screens.Select(screen => new SaverForm(screen.Bounds, screen.DeviceName, screen.Primary,
                problem == null && screen.Primary ? settings : null,
                screen.Primary ? message : null)).ToArray();

        using var context = new ApplicationContext();
        using var watch = new Timer { Interval = 100 };
        var ending = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        void Quit(string reason)
        {
            if (ending) return;
            ending = true;
            Log.Write("exit: " + reason);
            foreach (var form in forms) form.Stop();
            context.ExitThread();
        }

        foreach (var form in forms)
        {
            form.ExitRequested += reason =>
            {
                if (clock.ElapsedMilliseconds >= GraceMs) Quit(reason);
            };
            form.FormClosed += (_, _) => Quit("window closed");
            form.Show();
        }
        forms.FirstOrDefault(f => f.IsPrimary)?.Activate();

        var origin = Cursor.Position;
        var lastInput = LastInputTick();
        watch.Tick += (_, _) =>
        {
            var position = Cursor.Position;
            var input = LastInputTick();
            if (clock.ElapsedMilliseconds < GraceMs)
            {
                origin = position;
                lastInput = input;
                return;
            }
            if (input == lastInput) return;
            lastInput = input;
            var dx = position.X - origin.X;
            var dy = position.Y - origin.Y;
            var moved = (long)dx * dx + (long)dy * dy;
            if (moved == 0) Quit("key, click, wheel or touch");
            else if (moved > MoveThreshold * MoveThreshold) Quit($"cursor moved {Math.Sqrt(moved):0} px");
        };

        Cursor.Hide();
        try
        {
            watch.Start();
            Application.Run(context);
        }
        finally
        {
            watch.Stop();
            Cursor.Show();
            foreach (var form in forms) form.Dispose();
            Log.Write("ended");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static uint LastInputTick()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.dwTime : 0;
    }
}

/// <summary>A borderless, topmost window covering a display or the virtual desktop.</summary>
internal sealed class SaverForm : Form
{
    private const int WatchdogMs = 25_000;
    private const int RetryMs = 60_000;
    private const int FramePollMs = 500;
    private const string FrameReadyScript = """
        Array.from(document.querySelectorAll('canvas')).some(canvas =>
          canvas.width > 0 && canvas.height > 0 && canvas.getBoundingClientRect().width > 0 &&
          (!canvas.parentElement || Number(getComputedStyle(canvas.parentElement).opacity) > 0))
        """;

    public event Action<string>? ExitRequested;
    public bool IsPrimary { get; }

    private readonly Settings? settings;
    private readonly string display;
    private readonly Rectangle? primaryArea;
    private readonly Timer watchdog = new() { Interval = WatchdogMs };
    private readonly Timer retry = new() { Interval = RetryMs };
    private readonly Timer framePoll = new() { Interval = FramePollMs };
    private CoreWebView2Controller? controller;
    private ulong? navigation;
    private bool displayOn = true;
    private bool showWebView;
    private bool showingFrame;
    private bool needsController;
    private bool stopping;
    private string message;
    private IntPtr powerNotification;

    public SaverForm(Rectangle bounds, string display, bool isPrimary, Settings? settings, string? problem,
        Rectangle? primaryBounds = null)
    {
        IsPrimary = isPrimary;
        this.settings = settings;
        this.display = display;
        if (primaryBounds is { } area)
            primaryArea = new Rectangle(area.X - bounds.X, area.Y - bounds.Y, area.Width, area.Height);
        Text = "GALLERY";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        KeyDown += (_, _) => ExitRequested?.Invoke("key in window");
        MouseDown += (_, _) => ExitRequested?.Invoke("click in window");
        MouseWheel += (_, _) => ExitRequested?.Invoke("wheel in window");

        message = problem ?? "";
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            if (controller == null)
            {
                SetMessage(Loc.L("웹뷰를 시작하는 중입니다.", "The web view is taking longer to start."));
                Write("waiting for WebView2");
            }
            else
            {
                RetryAfterFailure("timed out waiting for an artwork");
                controller.CoreWebView2.Stop();
            }
        };
        retry.Tick += async (_, _) =>
        {
            retry.Stop();
            if (needsController) await StartWebView();
            else Navigate();
        };
        framePoll.Tick += async (_, _) => await CheckFrame();
    }

    private void Write(string detail) => Log.Write($"{display}: {detail}");

    public void Stop()
    {
        stopping = true;
        retry.Stop();
        watchdog.Stop();
        framePoll.Stop();
    }

    private void SetMessage(string text)
    {
        message = text;
        showWebView = false;
        UpdateVisibility();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (message.Length == 0) return;
        TextRenderer.DrawText(e.Graphics, message, Font, primaryArea ?? ClientRectangle, Color.FromArgb(150, 150, 154), Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void UpdateVisibility()
    {
        if (controller != null && !needsController) controller.IsVisible = displayOn && showWebView;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Write($"bounds={Bounds} primary={IsPrimary} exhibition={settings != null}");
        if (settings != null) await StartWebView();
    }

    private async Task StartWebView()
    {
        if (stopping || IsDisposed) return;
        needsController = false;
        watchdog.Start();
        try
        {
            var env = await Saver.Environment();
            if (stopping || IsDisposed) return;
            Write("creating WebView2 " + env.BrowserVersionString);
            var created = await env.CreateCoreWebView2ControllerAsync(Handle);
            if (stopping || IsDisposed)
            {
                created.Close();
                return;
            }
            controller = created;
            controller.DefaultBackgroundColor = Color.Black;
            controller.Bounds = ClientRectangle;
            UpdateVisibility();

            var core = controller.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.IsMuted = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ClientCertificateRequested += (_, args) => args.Cancel = true;
            core.WebMessageReceived += (_, args) =>
            {
                if (args.WebMessageAsJson == "\"exit\"") ExitRequested?.Invoke("input in page");
            };
            core.NavigationStarting += (_, args) =>
            {
                navigation = args.NavigationId;
                Write($"navigation #{args.NavigationId} starting");
            };
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += (_, args) =>
            {
                if (stopping || controller != created) return;
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    // The old controller is unusable after the browser process has exited.
                    needsController = true;
                    controller = null;
                    RetryAfterFailure("WebView2 browser exited", 2000);
                }
                else if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    RetryAfterFailure($"WebView2 {args.ProcessFailedKind}");
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PageScript(settings!.Language));
            if (!stopping && !IsDisposed && controller == created) Navigate();
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            if (stopping || IsDisposed) return;
            watchdog.Stop();
            Write(ex.ToString());
            SetMessage(Loc.L("Microsoft Edge WebView2 런타임이 필요합니다.",
                "This screen saver needs the Microsoft Edge WebView2 Runtime."));
        }
        catch (Exception ex)
        {
            if (stopping || IsDisposed) return;
            Write(ex.ToString());
            controller?.Close();
            controller = null;
            needsController = true;
            RetryAfterFailure("could not start the web view");
        }
    }

    private void Navigate()
    {
        if (stopping || controller == null || settings == null) return;
        navigation = null;
        showingFrame = false;
        var url = settings.ExhibitionUrl(spanning: primaryArea != null);
        Write("loading " + url.AbsoluteUri);
        showWebView = true;
        UpdateVisibility();
        watchdog.Stop();
        if (displayOn) watchdog.Start();
        controller.CoreWebView2.Navigate(url.AbsoluteUri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (stopping || navigation != e.NavigationId || controller == null) return;
        Write($"navigation #{e.NavigationId} completed: success={e.IsSuccess} http={e.HttpStatusCode} error={e.WebErrorStatus}");
        if (!e.IsSuccess || e.HttpStatusCode >= 400)
        {
            RetryAfterFailure("navigation failed");
            return;
        }
        framePoll.Start();
        if (IsPrimary) controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    private async Task CheckFrame()
    {
        if (stopping || controller == null || !displayOn) return;
        var current = navigation;
        var view = controller;
        framePoll.Stop();
        try
        {
            var result = await view.CoreWebView2.ExecuteScriptAsync(FrameReadyScript);
            if (stopping || IsDisposed || view != controller || current != navigation || result != "true") return;
            watchdog.Stop();
            framePoll.Stop();
            showingFrame = true;
            Write("showing the exhibition");
        }
        catch (Exception ex)
        {
            if (!stopping && view == controller && current == navigation) Write("frame check failed: " + ex.Message);
        }
        finally
        {
            if (!stopping && view == controller && current == navigation && !showingFrame) framePoll.Start();
        }
    }

    private void RetryAfterFailure(string reason, int delay = RetryMs)
    {
        if (stopping) return;
        navigation = null;
        watchdog.Stop();
        framePoll.Stop();
        Write(reason + $"; retrying in {delay / 1000}s");
        SetMessage(Loc.L("작품을 불러오지 못했습니다. 잠시 후 다시 시도합니다.\n", "The artwork could not load. Trying again shortly.\n") + Log.Path);
        retry.Interval = delay;
        retry.Start();
    }

    private string PageScript(string language)
    {
        var lang = language == "auto"
            ? "localStorage.removeItem('eg.lang');"
            : $"localStorage.setItem('eg.lang', '{language}');";
        // Keep the caption in the primary monitor's visible area on uneven monitor layouts.
        var caption = "";
        if (primaryArea is { } area)
        {
            var left = 100d * area.Left / ClientSize.Width;
            var right = 100d * (ClientSize.Width - area.Right) / ClientSize.Width;
            var bottom = 100d * (ClientSize.Height - area.Bottom) / ClientSize.Height;
            var css = FormattableString.Invariant(
                $".fade-chrome.pointer-events-none.absolute.inset-x-0.bottom-0 {{ left: {left:F6}% !important; right: {right:F6}% !important; bottom: {bottom:F6}% !important; }}");
            caption = $$"""
                addEventListener('DOMContentLoaded', () => {
                  const style = document.createElement('style');
                  style.textContent = '{{css}}';
                  document.head.append(style);
                }, { once: true });
                """;
        }
        return $$"""
            (() => {
              if (self !== top) return;
              try { {{lang}} } catch (e) {}
              {{caption}}
              const end = event => {
                if (event.isTrusted) window.chrome.webview.postMessage('exit');
              };
              addEventListener('keydown', end, true);
              addEventListener('pointerdown', end, true);
              addEventListener('wheel', end, { capture: true, passive: true });
            })();
            """;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (controller != null) controller.Bounds = ClientRectangle;
    }

    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (settings == null) return;
        var setting = ConsoleDisplayState;
        powerNotification = RegisterPowerSettingNotification(Handle, ref setting, 0);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (powerNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(powerNotification);
            powerNotification = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<PowerBroadcastSetting>(m.LParam);
            if (info.PowerSetting == ConsoleDisplayState && info.DataLength == sizeof(uint))
            {
                displayOn = info.Data != 0;
                UpdateVisibility();
                if (!displayOn && controller != null) watchdog.Stop();
                else if (displayOn && navigation != null && !showingFrame) watchdog.Start();
            }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            retry.Dispose();
            watchdog.Dispose();
            framePoll.Dispose();
            var view = controller;
            controller = null;
            view?.Close();
        }
        base.Dispose(disposing);
    }
}
