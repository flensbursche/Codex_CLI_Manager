using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Win32;

const string AppUrl = "http://127.0.0.1:4180";
if (!args.Contains("--portable", StringComparer.OrdinalIgnoreCase) && !UserInstallation.IsInstalledLocation())
{
    UserInstallation.InstallAndStart();
    return;
}
var openBrowserOnStart = !args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);
using var singleInstance = new Mutex(true, "Local\\CodexSessionManager.Agent", out var isFirstInstance);
if (!isFirstInstance)
{
    Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
    return;
}
BackgroundConsole.InitializeHidden();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(AppUrl);
builder.Services.AddSingleton<CodexClient>();
var app = builder.Build();
var actionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

app.Use(async (context, next) =>
{
    if (!context.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
        !context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    if (HttpMethods.IsPost(context.Request.Method) &&
        !context.Request.Headers["X-Codex-Manager-Token"].Equals(actionToken))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Ungültiger Aktionstoken." });
        return;
    }
    await next();
});

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(webRoot) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });

app.MapGet("/api/bootstrap", () => Results.Ok(new { data = new { token = actionToken, version = "0.6.2", runtime = ".NET Companion", supportsAutostart = true, autostartEnabled = AutoStartManager.IsEnabled(), installationPath = UserInstallation.InstallDirectory } }));
app.MapGet("/api/health", () => Results.Ok(new { ok = true, runtime = ".NET Companion" }));
app.MapPost("/api/autostart/{enabled:bool}", (bool enabled) =>
{
    if (enabled) AutoStartManager.Enable(); else AutoStartManager.Disable();
    return Results.Ok(new { data = new { enabled = AutoStartManager.IsEnabled() } });
});

app.MapGet("/api/sessions", async (bool? archived, CodexClient codex, CancellationToken cancellation) =>
{
    try { return Results.Ok(new { data = await codex.ListThreadsAsync(archived ?? false, cancellation) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 500); }
});

app.MapGet("/api/session/{threadId}", async (string threadId, CodexClient codex, CancellationToken cancellation) =>
{
    try { return Results.Ok(new { data = await codex.ReadThreadAsync(threadId, cancellation) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 500); }
});

app.MapPost("/api/{action}", async (string action, ActionRequest request, CodexClient codex, CancellationToken cancellation) =>
{
    try
    {
        switch (action)
        {
            case "resume": await codex.ResumeAsync(request.ThreadId, cancellation); break;
            case "open-desktop": await codex.OpenDesktopAsync(request.ThreadId, cancellation); break;
            case "open-project": await codex.OpenProjectAsync(request.ThreadId, cancellation); break;
            case "archive": await codex.ArchiveAsync(request.ThreadId, cancellation); break;
            default: return Results.NotFound(new { error = "Unbekannte Aktion." });
        }
        return Results.Ok(new { data = new { ok = true } });
    }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 500); }
});

app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
});

if (openBrowserOnStart)
    app.Lifetime.ApplicationStarted.Register(() => Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true }));
using var trayIcon = new TrayIconHost(AppUrl, app.Lifetime.StopApplication);
await app.RunAsync();

internal sealed record ActionRequest(string ThreadId);

internal static class BackgroundConsole
{
    private const int Hide = 0;
    private const int Show = 5;
    private static readonly object Sync = new();
    private static IntPtr _window;
    private static bool _visible;

    public static void InitializeHidden()
    {
        lock (Sync)
        {
            _window = GetConsoleWindow();
            if (_window == IntPtr.Zero) return;
            TryConnectStreams();
            ShowWindow(_window, Hide);
            _visible = false;
        }
    }

    public static void Toggle()
    {
        lock (Sync)
        {
            if (_window == IntPtr.Zero)
            {
                if (!AllocConsole()) return;
                _window = GetConsoleWindow();
                if (_window == IntPtr.Zero) return;
                TryConnectStreams();
                _visible = true;
                ShowWindow(_window, Show);
                SetForegroundWindow(_window);
                return;
            }
            _visible = !_visible;
            ShowWindow(_window, _visible ? Show : Hide);
            if (_visible) SetForegroundWindow(_window);
        }
    }

    private static void TryConnectStreams()
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}

internal sealed class TrayIconHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();

    public TrayIconHost(string appUrl, Action stopApplication)
    {
        _thread = new Thread(() => Run(appUrl, stopApplication))
        {
            IsBackground = true,
            Name = "Codex Session Manager Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(string appUrl, Action stopApplication)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        var openBrowser = menu.Items.Add("Browser öffnen");
        var showTerminal = menu.Items.Add("Terminal anzeigen / ausblenden");
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exit = menu.Items.Add("Beenden");
        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Codex Session Manager",
            ContextMenuStrip = menu,
            Visible = true
        };
        openBrowser.Click += (_, _) => OpenBrowser(appUrl);
        showTerminal.Click += (_, _) => BackgroundConsole.Toggle();
        icon.DoubleClick += (_, _) => BackgroundConsole.Toggle();
        exit.Click += (_, _) =>
        {
            icon.Visible = false;
            stopApplication();
            System.Windows.Forms.Application.ExitThread();
        };
        _ready.Set();
        System.Windows.Forms.Application.Run();
        icon.Visible = false;
    }

    private static void OpenBrowser(string appUrl) =>
        Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true });

    public void Dispose()
    {
        if (!_thread.IsAlive) return;
        System.Windows.Forms.Application.Exit();
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}

internal static class UserInstallation
{
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexSessionManager");
    private static string InstalledExecutable => Path.Combine(InstallDirectory, "CodexSessionManager.exe");

    public static bool IsInstalledLocation() =>
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(InstallDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static void InstallAndStart()
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory);
        Directory.CreateDirectory(InstallDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destination = Path.Combine(InstallDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
        }
        if (!File.Exists(InstalledExecutable)) throw new FileNotFoundException("Die installierte Anwendung wurde nicht gefunden.", InstalledExecutable);
        AutoStartManager.Enable(InstalledExecutable);
        Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = true });
    }
}

internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexSessionManager";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void Enable(string? executable = null)
    {
        executable ??= Environment.ProcessPath ?? throw new InvalidOperationException("Pfad zur Anwendung konnte nicht ermittelt werden.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal sealed class CodexClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonArray> ListThreadsAsync(bool archived, CancellationToken cancellation)
    {
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        var data = CodexStateStore.Query($"""
            select id, rollout_path, cwd, name, title, preview, first_user_message,
                   created_at_ms, updated_at_ms, recency_at_ms, source, thread_source,
                   model_provider, model, git_branch, is_pinned, archived, originator
              from threads
             where archived = {(archived ? 1 : 0)}
             order by coalesce(recency_at_ms, updated_at_ms, created_at_ms) desc
             limit 1000
            """);
        var normalized = new JsonArray();
        var activeProcessSessions = DetectActiveProcessSessions();
        foreach (var node in data)
            if (node is JsonObject thread)
            {
                var item = NormalizeDatabaseThread(thread);
                var id = item["id"]?.GetValue<string>();
                item["active"] = id is not null && (activeProcessSessions.Contains(id) || IsSessionActive(id));
                normalized.Add(item);
            }
        return normalized;
    }

    public async Task<JsonObject> ReadThreadAsync(string threadId, CancellationToken cancellation)
    {
        ValidateThreadId(threadId);
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        var rows = CodexStateStore.Query($"select * from threads where id = '{threadId}' limit 1");
        var thread = rows.FirstOrDefault() as JsonObject ?? throw new KeyNotFoundException("Session wurde nicht gefunden.");
        var detail = (JsonObject)thread.DeepClone();
        var normalized = NormalizeDatabaseThread(thread);
        foreach (var entry in normalized) detail[entry.Key] = entry.Value?.DeepClone();
        detail["active"] = DetectActiveProcessSessions().Contains(threadId) || IsSessionActive(threadId);
        return detail;
    }

    public async Task ResumeAsync(string threadId, CancellationToken cancellation)
    {
        if (IsSessionActive(threadId) || DetectActiveProcessSessions().Contains(threadId) || !_launchingSessions.TryAdd(threadId, 0))
            throw new InvalidOperationException("Diese Session ist bereits geöffnet.");
        JsonObject thread;
        try { thread = await ReadThreadAsync(threadId, cancellation); }
        catch { _launchingSessions.TryRemove(threadId, out _); throw; }
        var cwd = thread["cwd"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            _launchingSessions.TryRemove(threadId, out _);
            throw new DirectoryNotFoundException($"Projektordner nicht gefunden: {cwd}");
        }
        var mutexName = SessionMutexName(threadId);
        var command = $"$sessionMutex = [System.Threading.Mutex]::new($true, '{mutexName}'); try {{ codex --no-daemon resume '{threadId}' }} finally {{ $sessionMutex.ReleaseMutex(); $sessionMutex.Dispose() }}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                WorkingDirectory = cwd,
                UseShellExecute = true,
                Arguments = $"-NoLogo -NoProfile -NoExit -Command \"{command}\""
            });
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(4)); _launchingSessions.TryRemove(threadId, out _); });
        }
        catch
        {
            _launchingSessions.TryRemove(threadId, out _);
            throw;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _launchingSessions = new();

    private bool IsSessionActive(string threadId)
    {
        if (_launchingSessions.ContainsKey(threadId)) return true;
        if (IsCodexWriterLocked(threadId)) return true;
        try
        {
            using var activeMutex = Mutex.OpenExisting(SessionMutexName(threadId));
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static string SessionMutexName(string threadId) => $"Local\\CodexSessionManager.Session.{threadId}";

    private static bool IsCodexWriterLocked(string threadId)
    {
        var lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "thread-writer-locks", $"{threadId}.lock");
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Lock(0, 1);
            stream.Unlock(0, 1);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static HashSet<string> DetectActiveProcessSessions()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) return active;
            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic services = locator.ConnectServer(".", @"root\cimv2");
            dynamic processes = services.ExecQuery("SELECT CommandLine FROM Win32_Process WHERE CommandLine IS NOT NULL");
            foreach (dynamic process in processes)
            {
                string? commandLine = process.CommandLine as string;
                if (string.IsNullOrWhiteSpace(commandLine) || !commandLine.Contains("resume", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(commandLine, @"(?:^|\s)resume\s+['""]?(?<id>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    active.Add(match.Groups["id"].Value);
            }
        }
        catch { }
        return active;
    }

    public async Task OpenProjectAsync(string threadId, CancellationToken cancellation)
    {
        var thread = await ReadThreadAsync(threadId, cancellation);
        var cwd = thread["cwd"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) throw new DirectoryNotFoundException($"Projektordner nicht gefunden: {cwd}");
        Process.Start(new ProcessStartInfo("code", $"\"{cwd}\"") { UseShellExecute = true });
    }

    public async Task OpenDesktopAsync(string threadId, CancellationToken cancellation)
    {
        await ReadThreadAsync(threadId, cancellation);
        Process.Start(new ProcessStartInfo($"codex://threads/{Uri.EscapeDataString(threadId)}") { UseShellExecute = true });
    }

    public async Task ArchiveAsync(string threadId, CancellationToken cancellation)
    {
        ValidateThreadId(threadId);
        var result = await RunCodexCommandAsync(["archive", threadId], cancellation);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Trim());
    }

    private async Task<JsonObject> RequestAsync(string method, object parameters, CancellationToken cancellation)
    {
        // The manager must not contend with Codex' background daemon. On managed
        // Windows devices that daemon can hold an inaccessible lock and leave the
        // stdio request waiting forever.
        using var process = CreateCodexProcess(["--no-daemon", "app-server", "--listen", "stdio://"]);
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await SendAsync(process, new { method = "initialize", id = 1, @params = new { clientInfo = new { name = "codex_session_manager", title = "Codex Session Manager", version = "0.2.0" }, capabilities = (object?)null } });
            await ReadResponseAsync(process, 1, timeout.Token);
            await SendAsync(process, new { method = "initialized" });
            await SendAsync(process, new { method, id = 2, @params = parameters });
            var response = await ReadResponseAsync(process, 2, timeout.Token);
            process.Kill(true);
            return response;
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill(true);
            var stderr = await stderrTask;
            var message = error is OperationCanceledException && !cancellation.IsCancellationRequested
                ? "Codex antwortet nicht. Bitte den Manager neu starten oder `codex doctor` ausführen."
                : error.Message;
            throw new InvalidOperationException($"{message}{(string.IsNullOrWhiteSpace(stderr) ? "" : $" Codex: {stderr.Trim()}")}", error);
        }
    }

    private static Process CreateCodexProcess(IEnumerable<string> arguments)
    {
        var npmDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        var codexScript = Path.Combine(npmDirectory, "node_modules", "@openai", "codex", "bin", "codex.js");
        if (!File.Exists(codexScript))
            throw new FileNotFoundException("Codex CLI wurde im Benutzerprofil nicht gefunden.", codexScript);
        var bundledNode = Path.Combine(npmDirectory, "node.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(bundledNode) ? bundledNode : "node.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(codexScript);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return new Process { StartInfo = startInfo };
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonObject> ReadResponseAsync(Process process, int expectedId, CancellationToken cancellation)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellation) ?? throw new InvalidOperationException("Codex app-server wurde beendet.");
            if (JsonNode.Parse(line) is not JsonObject message || message["id"]?.GetValue<int>() != expectedId) continue;
            if (message["error"] is not null) throw new InvalidOperationException(message["error"]!.ToJsonString());
            return message["result"]?.AsObject() ?? throw new InvalidOperationException("Leere Codex-Antwort.");
        }
    }

    private static JsonObject NormalizeThread(JsonObject thread)
    {
        var cwd = NormalizePath(thread["cwd"]?.GetValue<string>());
        var gitInfo = thread["gitInfo"] as JsonObject;
        return new JsonObject
        {
            ["id"] = thread["id"]?.DeepClone(),
            ["title"] = (thread["name"] ?? thread["preview"])?.DeepClone(),
            ["preview"] = thread["preview"]?.DeepClone(),
            ["cwd"] = cwd,
            ["project"] = string.IsNullOrEmpty(cwd) ? "Ohne Projekt" : Path.GetFileName(cwd.TrimEnd('\\', '/')),
            ["branch"] = gitInfo?["branch"]?.DeepClone(),
            ["model"] = (thread["model"] ?? thread["modelProvider"])?.DeepClone(),
            ["source"] = NormalizeSource(thread["source"]),
            ["createdAt"] = NormalizeTimestamp(thread["createdAt"]),
            ["updatedAt"] = NormalizeTimestamp(thread["recencyAt"] ?? thread["updatedAt"] ?? thread["createdAt"]),
            ["pinned"] = thread["isPinned"]?.DeepClone() ?? false,
            ["archived"] = thread["archived"]?.DeepClone() ?? false,
            ["storagePath"] = NormalizePath((thread["path"] ?? thread["rolloutPath"])?.GetValue<string>())
        };
    }

    private static JsonObject NormalizeDatabaseThread(JsonObject thread)
    {
        var cwd = NormalizePath(thread["cwd"]?.GetValue<string>());
        var title = FirstText(thread, "name", "title", "first_user_message") ?? "Unbenannte Session";
        var preview = FirstText(thread, "preview", "first_user_message") ?? "";
        var originator = FirstText(thread, "originator") ?? "";
        var isDesktop = originator.Contains("Codex Desktop", StringComparison.OrdinalIgnoreCase);
        var rawSource = FirstText(thread, "thread_source", "source") ?? "cli";
        var isInternal = rawSource.Contains("guardian", StringComparison.OrdinalIgnoreCase) || rawSource.Contains("memory_consolidation", StringComparison.OrdinalIgnoreCase);
        var source = isDesktop ? "Codex Desktop" : rawSource;
        var project = string.IsNullOrEmpty(cwd) ? "Ohne Projekt" : Path.GetFileName(cwd.TrimEnd('\\', '/'));
        if (isDesktop && IsDesktopScratchDirectory(cwd)) project = "Codex Desktop";
        return new JsonObject
        {
            ["id"] = thread["id"]?.DeepClone(),
            ["title"] = Limit(title.Trim(), 160),
            ["preview"] = Limit(preview.Trim(), 360),
            ["cwd"] = cwd,
            ["project"] = project,
            ["branch"] = thread["git_branch"]?.DeepClone(),
            ["model"] = (thread["model"] ?? thread["model_provider"])?.DeepClone(),
            ["source"] = source,
            ["originator"] = originator,
            ["desktop"] = isDesktop,
            ["internal"] = isInternal,
            ["createdAt"] = thread["created_at_ms"]?.DeepClone() ?? 0,
            ["updatedAt"] = (thread["recency_at_ms"] ?? thread["updated_at_ms"] ?? thread["created_at_ms"])?.DeepClone() ?? 0,
            ["pinned"] = (thread["is_pinned"]?.GetValue<long>() ?? 0) != 0,
            ["archived"] = (thread["archived"]?.GetValue<long>() ?? 0) != 0,
            ["storagePath"] = NormalizePath(thread["rollout_path"]?.GetValue<string>())
        };
    }

    private static bool IsDesktopScratchDirectory(string cwd)
    {
        var marker = $"{Path.DirectorySeparatorChar}Documents{Path.DirectorySeparatorChar}Codex{Path.DirectorySeparatorChar}";
        return cwd.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstText(JsonObject value, params string[] keys)
    {
        foreach (var key in keys)
            if (value[key] is JsonValue node && node.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return null;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private static string NormalizeSource(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node is JsonObject source)
        {
            if (source["custom"] is JsonValue custom && custom.TryGetValue<string>(out var customText)) return customText;
            if (source["subAgent"] is not null) return "subAgent";
        }
        return "unknown";
    }

    private static JsonNode NormalizeTimestamp(JsonNode? node)
    {
        if (node is null) return 0;
        var value = node.GetValue<long>();
        return value < 10_000_000_000 ? value * 1000 : value;
    }

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.StartsWith(@"\\?\UNC\")) return @"\\" + value[8..];
        if (value.StartsWith(@"\\?\")) return value[4..];
        return value;
    }

    private static void ValidateThreadId(string value)
    {
        if (value.Length is < 8 or > 128 || value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Ungültige Session-ID.");
    }

    private static async Task<(int ExitCode, string Error)> RunCodexCommandAsync(IEnumerable<string> arguments, CancellationToken cancellation)
    {
        using var process = CreateCodexProcess(arguments);
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellation);
        _ = process.StandardOutput.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        return (process.ExitCode, await errorTask);
    }
}

internal static class CodexStateStore
{
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private const int OpenReadOnly = 1;

    public static JsonArray Query(string sql)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "state_5.sqlite");
        if (!File.Exists(path)) throw new FileNotFoundException("Codex-Datenbank wurde nicht gefunden.", path);
        var result = new JsonArray();
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(sqlite3_open_v2(path, out database, OpenReadOnly, null), database, "Codex-Datenbank konnte nicht geöffnet werden");
            Check(sqlite3_prepare_v2(database, sql, -1, out statement, IntPtr.Zero), database, "Codex-Abfrage konnte nicht vorbereitet werden");
            while (true)
            {
                var step = sqlite3_step(statement);
                if (step == Done) break;
                Check(step == Row ? Ok : step, database, "Codex-Abfrage ist fehlgeschlagen");
                var row = new JsonObject();
                var count = sqlite3_column_count(statement);
                for (var index = 0; index < count; index++)
                {
                    var name = Marshal.PtrToStringUTF8(sqlite3_column_name(statement, index)) ?? $"column_{index}";
                    row[name] = sqlite3_column_type(statement, index) switch
                    {
                        1 => JsonValue.Create(sqlite3_column_int64(statement, index)),
                        2 => JsonValue.Create(sqlite3_column_double(statement, index)),
                        3 => JsonValue.Create(Marshal.PtrToStringUTF8(sqlite3_column_text(statement, index)) ?? ""),
                        4 => JsonValue.Create("<binary>"),
                        _ => null
                    };
                }
                result.Add(row);
            }
            return result;
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (database != IntPtr.Zero) sqlite3_close_v2(database);
        }
    }

    private static void Check(int code, IntPtr database, string context)
    {
        if (code == Ok) return;
        var message = database == IntPtr.Zero ? $"SQLite-Fehler {code}" : Marshal.PtrToStringUTF8(sqlite3_errmsg(database));
        throw new InvalidOperationException($"{context}: {message}");
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2(string filename, out IntPtr database, int flags, string? virtualFileSystem);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_prepare_v2(IntPtr database, string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_name(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
}
