using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GallerySaver;

/// <summary>
/// Windows starts a screen saver with one of these arguments:
///   /s          run full screen (also when there is no argument but "/s" in the shortcut)
///   /c[:hwnd]   show the settings dialog (no argument at all means the same)
///   /p hwnd     draw into the small preview of the Screen Saver Settings dialog
/// </summary>
internal static class Program
{
    /// <summary>Per-user data: the WebView2 profile and the extracted loader.</summary>
    public static readonly string DataFolder = Environment.GetEnvironmentVariable("GALLERY_SAVER_DATA_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Euiyun", "Gallery Screen Saver");

    [STAThread]
    private static int Main(string[] args)
    {
        // The WebView2 assemblies are resources of this executable, so that a
        // single .scr can be copied anywhere. Hook up their resolution before
        // any method that mentions a WebView2 type is compiled.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("unhandled: " + e.ExceptionObject);
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("thread exception: " + e.Exception);

        var (mode, handle) = Parse(args);
        switch (mode)
        {
            case 's':
                RunSaver(Settings.Load());
                return 0;
            case 'p':
                if (handle != IntPtr.Zero && IsWindow(handle)) Preview.Run(handle);
                return 0;
            case 't':
                return SelfTest();
            default:
                var settings = Settings.Load();
                while (true)
                {
                    using var dialog = new ConfigForm(settings);
                    var result = handle != IntPtr.Zero && IsWindow(handle)
                        ? dialog.ShowDialog(new Owner(handle))
                        : dialog.ShowDialog();
                    if (result != DialogResult.Retry || dialog.PreviewSettings is not { } preview) return 0;
                    RunSaver(preview);
                    settings = preview;
                }
        }
    }

    private static void RunSaver(Settings settings)
    {
        using var instance = new System.Threading.Mutex(false, @"Local\Euiyun.Gallery.ScreenSaver");
        try
        {
            if (!instance.WaitOne(0)) return;
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // The previous process ended before releasing it. This process now owns it.
        }
        try
        {
            Log.Start($"Gallery.scr {Assembly.GetExecutingAssembly().GetName().Version} /s · {Environment.OSVersion} · {RuntimeInformation.ProcessArchitecture} · {Screen.AllScreens.Length} display(s)");
            Exception? problem = null;
            try
            {
                PrepareWebView2();
            }
            catch (Exception ex)
            {
                Log.Write("prepare failed: " + ex);
                problem = ex;
            }
            Saver.Run(settings, problem);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    private static (char mode, IntPtr handle) Parse(string[] args)
    {
        if (args.Length == 0) return ('c', IntPtr.Zero);
        var first = args[0].Trim().ToLowerInvariant();
        if (first == "/selftest") return ('t', IntPtr.Zero);
        if (first.Length < 2 || (first[0] != '/' && first[0] != '-')) return ('c', IntPtr.Zero);
        var separator = first.IndexOf(':');
        var option = separator < 0 ? first : first.Substring(0, separator);
        if (option.Length != 2) return ('c', IntPtr.Zero);
        // The window handle comes either as "/c:1234" or as a second argument.
        var rest = separator >= 0 ? first.Substring(separator + 1) : args.Length > 1 ? args[1] : "";
        var handle = long.TryParse(rest, out var value)
            && (IntPtr.Size == 8 || value is >= int.MinValue and <= uint.MaxValue)
            ? (IntPtr.Size == 8 ? new IntPtr(value) : new IntPtr(unchecked((int)value)))
            : IntPtr.Zero;
        return (option[1] is 's' or 'p' or 'c' ? option[1] : 'c', handle);
    }

    private static readonly Dictionary<string, Assembly> Resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loaded once and remembered: every Assembly.Load(bytes) makes a distinct
    /// copy, and two copies of WebView2.Core would not share the loader path.
    /// </summary>
    private static Assembly? ResolveEmbedded(object? sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        lock (Resolved)
        {
            if (Resolved.TryGetValue(name, out var known)) return known;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) return null;
            var bytes = new byte[stream.Length];
            var read = 0;
            while (read < bytes.Length) read += stream.Read(bytes, read, bytes.Length - read);
            var assembly = Assembly.Load(bytes);
            Resolved[name] = assembly;
            Log.Write("resolved " + assembly.FullName);
            return assembly;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    private static string Architecture() => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    private static bool loaderPrepared;

    /// <summary>
    /// Write the native WebView2Loader.dll for this process's architecture to
    /// the data folder and point WebView2 at it. Must precede any other WebView2 call.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PrepareWebView2()
    {
        // Full-screen previews reuse this process after the settings dialog reopens.
        if (loaderPrepared) return;

        var arch = Architecture();
        var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        var folder = Path.Combine(DataFolder, "loader", version, arch);
        var target = Path.Combine(folder, "WebView2Loader.dll");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"loader/{arch}/WebView2Loader.dll")
            ?? throw new InvalidOperationException("WebView2Loader.dll is not embedded for " + arch);
        if (!File.Exists(target) || new FileInfo(target).Length != stream.Length)
        {
            Directory.CreateDirectory(folder);
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = File.Create(temp)) stream.CopyTo(file);
            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
            }
            catch (IOException)
            {
                // Another instance (one per Windows session) won the race; its copy is the same file.
                File.Delete(temp);
            }
        }
        // Loaded by full path now, so that the later P/Invoke by bare name finds it in the process.
        if (LoadLibrary(target) == IntPtr.Zero)
            throw new IOException($"LoadLibrary({target}) failed: error {Marshal.GetLastWin32Error()}");
        Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(folder);
        loaderPrepared = true;
        Log.Write("loader " + target);
        Log.Write("WebView2 runtime " + Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
    }

    /// <summary>
    /// For CI: from a lone .scr, resolve the embedded assemblies, extract the
    /// loader and reach the installed WebView2 runtime. Exit code 0 on success.
    /// </summary>
    private static int SelfTest()
    {
        try
        {
            PrepareWebView2();
            var runtime = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            PrepareWebView2(); // The next preview must reuse the loader even after calling WebView2.
            var url = new Settings().ExhibitionUrl();
            File.WriteAllText(Path.Combine(DataFolder, "selftest.txt"), $"WebView2 {runtime}\n{url}\n");
            return string.IsNullOrEmpty(runtime) ? 2 : 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(Path.Combine(DataFolder, "selftest.txt"), ex.ToString());
            }
            catch
            {
                /* nothing more to report with */
            }
            return 1;
        }
    }

    private sealed class Owner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}

/// <summary>%LOCALAPPDATA%\Euiyun\Gallery Screen Saver\log.txt — what happened in the last run.</summary>
internal static class Log
{
    public static readonly string Path = System.IO.Path.Combine(Program.DataFolder, "log.txt");
    private static readonly object Gate = new();
    private static bool enabled;
    private static DateTime started;

    public static void Start(string line)
    {
        try
        {
            started = DateTime.Now;
            Directory.CreateDirectory(Program.DataFolder);
            File.WriteAllText(Path, $"{started:yyyy-MM-dd HH:mm:ss} {line}\r\n");
            enabled = true;
        }
        catch
        {
            /* no log, then */
        }
    }

    public static void Write(string line)
    {
        if (!enabled) return;
        lock (Gate)
        {
            try
            {
                if (new FileInfo(Path).Length > 1_000_000) return;
                File.AppendAllText(Path, $"+{(DateTime.Now - started).TotalSeconds,7:0.000}s {line}\r\n");
            }
            catch
            {
                /* never let logging end the screen saver */
            }
        }
    }
}
