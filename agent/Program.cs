using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Win32;

const string AppUrl = "http://127.0.0.1:4180";
if (UpdateService.TryApplyStagedUpdate(args)) return;
if (!args.Contains("--portable", StringComparer.OrdinalIgnoreCase) && !UserInstallation.IsInstalledLocation())
{
    UserInstallation.InstallAndStart();
    return;
}
UpdateService.ScheduleStagingCleanup();
var openBrowserOnStart = !args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);
using var singleInstance = new Mutex(true, "Local\\CodexSessionManager.Agent", out var isFirstInstance);
if (!isFirstInstance)
{
    Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(AppUrl);
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<TerminalSettingsStore>();
builder.Services.AddSingleton<CodexClient>();
var app = builder.Build();
var actionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
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

app.MapGet("/api/bootstrap", () => Results.Ok(new { data = new { token = actionToken, version = ProductInfo.Version, runtime = ".NET Companion", supportsAutostart = true, autostartEnabled = AutoStartManager.IsEnabled(), installationPath = UserInstallation.InstallDirectory } }));
app.MapGet("/api/health", () => Results.Ok(new { ok = true, runtime = ".NET Companion" }));
app.MapGet("/api/events", async (HttpContext context, CancellationToken cancellation) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Connection = "keep-alive";
    await context.Response.WriteAsync("retry: 2000\n\n", cancellation);
    await context.Response.Body.FlushAsync(cancellation);

    var lastStamp = CodexStateStore.ChangeStamp();
    var lastUpdate = DateTime.UtcNow;
    try
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
            var stamp = CodexStateStore.ChangeStamp();
            var fallbackDue = DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(5);
            if (stamp == lastStamp && !fallbackDue) continue;

            lastStamp = stamp;
            lastUpdate = DateTime.UtcNow;
            await context.Response.WriteAsync($"event: sessions\ndata: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\n\n", cancellation);
            await context.Response.Body.FlushAsync(cancellation);
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
});
app.MapPost("/api/autostart/{enabled:bool}", (bool enabled) =>
{
    if (enabled) AutoStartManager.Enable(); else AutoStartManager.Disable();
    return Results.Ok(new { data = new { enabled = AutoStartManager.IsEnabled() } });
});

app.MapGet("/api/terminal-settings", (TerminalSettingsStore settings) => Results.Ok(new { data = settings.GetView() }));

app.MapPost("/api/terminal-settings", (TerminalSettingsRequest request, TerminalSettingsStore settings) =>
{
    try { return Results.Ok(new { data = settings.Save(request) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 400); }
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

app.MapGet("/api/projects", (ProjectStore projects) => Results.Ok(new { data = projects.List() }));

app.MapPost("/api/projects", (CreateProjectRequest request, ProjectStore projects) =>
{
    try { return Results.Ok(new { data = projects.Create(request.Name) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 400); }
});

app.MapPost("/api/projects/{projectId}/sessions/{threadId}", (string projectId, string threadId, ProjectStore projects) =>
{
    try
    {
        projects.Assign(threadId, projectId);
        return Results.Ok(new { data = new { projectId } });
    }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 400); }
});

app.MapPost("/api/session/{threadId}/pinned/{pinned:bool}", (string threadId, bool pinned, CodexClient codex) =>
{
    try
    {
        codex.SetPinned(threadId, pinned);
        return Results.Ok(new { data = new { pinned } });
    }
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
internal sealed record CreateProjectRequest(string Name);
internal sealed record TerminalSettingsRequest(string Terminal, string Shell);

internal sealed class ProjectStore
{
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(UserInstallation.InstallDirectory, "projects.json");
    private StoredProjectState _state;

    public ProjectStore() => _state = Load();

    public JsonArray List()
    {
        lock (_sync)
            return new JsonArray(_state.Projects
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(project => (JsonNode)new JsonObject
                {
                    ["id"] = project.Id,
                    ["name"] = project.Name,
                    ["createdAt"] = project.CreatedAt
                }).ToArray());
    }

    public JsonObject Create(string? value)
    {
        var name = NormalizeName(value);
        lock (_sync)
        {
            if (_state.Projects.Any(project => project.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("Ein Projekt mit diesem Namen existiert bereits.");
            var project = new StoredProject(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _state.Projects.Add(project);
            Save();
            return new JsonObject { ["id"] = project.Id, ["name"] = project.Name, ["createdAt"] = project.CreatedAt };
        }
    }

    public void Assign(string threadId, string projectId)
    {
        if (!Guid.TryParse(threadId, out _)) throw new ArgumentException("Ungültige Session-ID.");
        lock (_sync)
        {
            if (!_state.Projects.Any(project => project.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase)))
                throw new KeyNotFoundException("Projekt wurde nicht gefunden.");
            _state.Assignments[threadId] = projectId;
            Save();
        }
    }

    public void Apply(JsonObject session)
    {
        lock (_sync)
        {
            var threadId = session["id"]?.GetValue<string>() ?? "";
            var baseProject = session["project"]?.GetValue<string>() ?? "Ohne Projekt";
            session["baseProject"] = baseProject;
            StoredProject? project = null;
            if (_state.Assignments.TryGetValue(threadId, out var assignedId))
                project = _state.Projects.FirstOrDefault(candidate => candidate.Id.Equals(assignedId, StringComparison.OrdinalIgnoreCase));
            if (project is null) return;
            session["projectId"] = project.Id;
            session["project"] = project.Name;
        }
    }

    private StoredProjectState Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<StoredProjectState>(File.ReadAllText(_path), StorageJson) ?? new StoredProjectState();
        }
        catch
        {
        }
        return new StoredProjectState();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_state, StorageJson), new UTF8Encoding(false));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string NormalizeName(string? value)
    {
        var name = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 1 or > 64) throw new ArgumentException("Der Projektname muss zwischen 1 und 64 Zeichen lang sein.");
        return name;
    }

    private sealed class StoredProjectState
    {
        public List<StoredProject> Projects { get; set; } = [];
        public Dictionary<string, string> Assignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoredProject(string Id, string Name, long CreatedAt);
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
            Name = "Codex CLI Manager Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(string appUrl, Action stopApplication)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        var openBrowser = menu.Items.Add("Codex Manager öffnen");
        var openPowerShell = menu.Items.Add("Monitor");
        var update = menu.Items.Add("Nach Updates suchen");
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exit = menu.Items.Add("Beenden");
        using var trayIcon = AppIcon.Create();
        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "Codex CLI Manager",
            ContextMenuStrip = menu,
            Visible = true
        };
        openBrowser.Click += (_, _) => OpenBrowser(appUrl);
        openPowerShell.Click += (_, _) => TerminalHost.OpenPowerShell(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        update.Click += async (_, _) =>
        {
            update.Enabled = false;
            update.Text = "Suche nach Updates …";
            try
            {
                var available = update.Tag as UpdateInfo ?? await UpdateService.CheckForUpdateAsync(CancellationToken.None);
                if (available is null)
                {
                    update.Text = "Nach Updates suchen";
                    System.Windows.Forms.MessageBox.Show(
                        $"Codex CLI Manager {ProductInfo.Version} ist aktuell.",
                        "Keine Updates",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                    return;
                }

                var answer = System.Windows.Forms.MessageBox.Show(
                    $"Version {available.Version} ist verfügbar.\n\nJetzt herunterladen, installieren und den Manager neu starten?",
                    "Codex CLI Manager aktualisieren",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Information);
                if (answer != System.Windows.Forms.DialogResult.Yes)
                {
                    update.Text = $"Update {available.Version} installieren";
                    update.Tag = available;
                    return;
                }

                update.Text = $"Update {available.Version} wird geladen …";
                await UpdateService.DownloadAndLaunchAsync(available, CancellationToken.None);
                icon.Visible = false;
                stopApplication();
                System.Windows.Forms.Application.ExitThread();
            }
            catch (Exception error)
            {
                update.Text = "Nach Updates suchen";
                update.Tag = null;
                System.Windows.Forms.MessageBox.Show(
                    error.Message,
                    "Update fehlgeschlagen",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                update.Enabled = true;
            }
        };
        icon.DoubleClick += (_, _) => OpenBrowser(appUrl);
        exit.Click += (_, _) =>
        {
            icon.Visible = false;
            stopApplication();
            System.Windows.Forms.Application.ExitThread();
        };
        using var updateTimer = new System.Windows.Forms.Timer { Interval = 1800 };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Stop();
            try
            {
                var available = await UpdateService.CheckForUpdateAsync(CancellationToken.None);
                if (available is null) return;
                update.Tag = available;
                update.Text = $"Update {available.Version} installieren";
            }
            catch
            {
            }
        };
        updateTimer.Start();
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

internal static class AppIcon
{
    public static System.Drawing.Icon Create()
    {
        try
        {
            if (Environment.ProcessPath is string executable && File.Exists(executable))
            {
                using var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
                if (appIcon is not null) return (System.Drawing.Icon)appIcon.Clone();
            }
        }
        catch
        {
        }
        return CreateFallback();
    }

    private static System.Drawing.Icon CreateFallback()
    {
        using var bitmap = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);

        using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 101, 55));
        using var shape = new System.Drawing.Drawing2D.GraphicsPath();
        const float radius = 13;
        shape.AddArc(2, 2, radius * 2, radius * 2, 180, 90);
        shape.AddArc(62 - radius * 2, 2, radius * 2, radius * 2, 270, 90);
        shape.AddArc(62 - radius * 2, 62 - radius * 2, radius * 2, radius * 2, 0, 90);
        shape.AddArc(2, 62 - radius * 2, radius * 2, radius * 2, 90, 90);
        shape.CloseFigure();
        graphics.FillPath(background, shape);

        using var prompt = new System.Drawing.Pen(System.Drawing.Color.White, 5.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        graphics.DrawLines(prompt, [
            new System.Drawing.PointF(19, 19),
            new System.Drawing.PointF(31, 31),
            new System.Drawing.PointF(19, 43)
        ]);
        graphics.DrawLine(prompt, 35, 43, 47, 43);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class ProductInfo
{
    public const string Version = "0.7.0";
}

internal sealed record UpdateInfo(string Version, string PackageUrl, string ChecksumUrl);

internal static class UpdateService
{
    private const string Repository = "flensbursche/Codex_CLI_Manager";
    private const string PackageName = "CodexSessionManager-Setup-win-x64.zip";
    private static readonly string UpdatesRoot = Path.Combine(Path.GetTempPath(), "CodexSessionManager", "updates");
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellation)
    {
        using var response = await Client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellation);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("Das GitHub Release hat keine Versionsnummer.");
        var latest = ParseVersion(tag);
        if (latest <= ParseVersion(ProductInfo.Version)) return null;

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == PackageName) packageUrl = url;
            if (name == PackageName + ".sha256") checksumUrl = url;
        }
        if (packageUrl is null || checksumUrl is null)
            throw new InvalidOperationException("Das neueste GitHub Release enthält kein vollständiges Windows-Update.");
        ValidateDownloadUrl(packageUrl);
        ValidateDownloadUrl(checksumUrl);
        return new UpdateInfo(latest.ToString(3), packageUrl, checksumUrl);
    }

    public static async Task DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellation)
    {
        var updateDirectory = Path.Combine(UpdatesRoot, $"{update.Version}-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(updateDirectory, "payload");
        var packagePath = Path.Combine(updateDirectory, PackageName);
        Directory.CreateDirectory(payloadDirectory);
        await DownloadFileAsync(update.PackageUrl, packagePath, cancellation);

        var expectedHash = (await Client.GetStringAsync(update.ChecksumUrl, cancellation))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Die Release-Prüfsumme ist ungültig.");
        await using (var package = File.OpenRead(packagePath))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellation));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Die Prüfsumme des heruntergeladenen Updates stimmt nicht.");
        }

        ZipFile.ExtractToDirectory(packagePath, payloadDirectory, overwriteFiles: true);
        var stagedExecutable = Directory.EnumerateFiles(payloadDirectory, "CodexSessionManager.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("Das Update enthält keine CodexSessionManager.exe.");
        var payloadRoot = Path.GetDirectoryName(stagedExecutable)!;
        var startInfo = new ProcessStartInfo(stagedExecutable) { UseShellExecute = true, WorkingDirectory = payloadRoot };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(UserInstallation.InstallDirectory);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Der Update-Helfer konnte nicht gestartet werden.");
    }

    public static bool TryApplyStagedUpdate(string[] arguments)
    {
        var updateIndex = Array.FindIndex(arguments, value => value.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));
        if (updateIndex < 0) return false;
        try
        {
            if (arguments.Length <= updateIndex + 2 || !int.TryParse(arguments[updateIndex + 2], out var processId))
                throw new ArgumentException("Ungültige Update-Parameter.");
            var destination = Path.GetFullPath(arguments[updateIndex + 1]);
            var expectedDestination = Path.GetFullPath(UserInstallation.InstallDirectory);
            if (!destination.Equals(expectedDestination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Das Update-Ziel ist nicht zulässig.");

            WaitForManagerExit(processId);
            CopyPayload(AppContext.BaseDirectory, destination);
            var installedExecutable = Path.Combine(destination, "CodexSessionManager.exe");
            AutoStartManager.Enable(installedExecutable);
            Process.Start(new ProcessStartInfo(installedExecutable) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            System.Windows.Forms.MessageBox.Show(
                error.Message,
                "Update fehlgeschlagen",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
        return true;
    }

    public static void ScheduleStagingCleanup()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            try
            {
                var root = Path.GetFullPath(UpdatesRoot);
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !root.EndsWith(Path.Combine("CodexSessionManager", "updates"), StringComparison.OrdinalIgnoreCase)) return;
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Codex-CLI-Manager/{ProductInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellation)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, cancellation);
    }

    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return System.Version.TryParse(normalized, out var version)
            ? version
            : throw new InvalidOperationException($"Ungültige Release-Version: {value}");
    }

    private static void ValidateDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das Release enthält eine unzulässige Download-Adresse.");
    }

    private static void WaitForManagerExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.WaitForExit(30000)) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void CopyPayload(string source, string destination)
    {
        var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(ValidatedDestination(destinationRoot, relative));
        }
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = ValidatedDestination(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string ValidatedDestination(string destinationRoot, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(destinationRoot, relative));
        if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das Update enthält einen unzulässigen Pfad.");
        return target;
    }
}

internal sealed record TerminalPreferences(string Terminal = "auto", string Shell = "auto");
internal sealed record ShellResolution(string Key, string Name, string Path, string Version, string ProfileId);

internal sealed class TerminalSettingsStore
{
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string SettingsPath => Path.Combine(UserInstallation.InstallDirectory, "terminal-settings.json");
    private readonly object _sync = new();

    public JsonObject GetView()
    {
        lock (_sync) return BuildView(LoadCurrent());
    }

    public JsonObject Save(TerminalSettingsRequest request)
    {
        var terminal = (request.Terminal ?? "").Trim().ToLowerInvariant();
        var shell = (request.Shell ?? "").Trim().ToLowerInvariant();
        if (terminal is not ("auto" or "windows-terminal" or "console"))
            throw new ArgumentException("Unbekannte Terminal-Auswahl.");
        if (shell is not ("auto" or "powershell7" or "windows-powershell"))
            throw new ArgumentException("Unbekannte PowerShell-Auswahl.");

        var preferences = new TerminalPreferences(terminal, shell);
        TerminalHost.ResolveTerminal(preferences.Terminal);
        PowerShellHost.Resolve(preferences.Shell);
        lock (_sync)
        {
            Directory.CreateDirectory(UserInstallation.InstallDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(preferences, StorageJson));
            return BuildView(preferences);
        }
    }

    public static TerminalPreferences LoadCurrent()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            return JsonSerializer.Deserialize<TerminalPreferences>(File.ReadAllText(SettingsPath), StorageJson) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static JsonObject BuildView(TerminalPreferences preferences)
    {
        var shell = PowerShellHost.Resolve(preferences.Shell);
        var terminal = TerminalHost.ResolveTerminal(preferences.Terminal);
        return new JsonObject
        {
            ["terminal"] = preferences.Terminal,
            ["shell"] = preferences.Shell,
            ["resolvedTerminal"] = terminal == "windows-terminal" ? "Windows Terminal" : "PowerShell-Konsole",
            ["resolvedShell"] = shell.Name,
            ["detected"] = new JsonObject
            {
                ["windowsTerminal"] = Detection(TerminalHost.ExecutablePath, "Windows Terminal", PowerShellHost.FileVersion(TerminalHost.IconExecutablePath)),
                ["powershell7"] = Detection(PowerShellHost.PowerShell7Path, "PowerShell 7", PowerShellHost.PowerShell7Version),
                ["windowsPowerShell"] = Detection(PowerShellHost.WindowsPowerShellPath, "Windows PowerShell 5.1", PowerShellHost.WindowsPowerShellVersion)
            }
        };
    }

    private static JsonObject Detection(string? path, string name, string version) => new()
    {
        ["name"] = name,
        ["available"] = path is not null && File.Exists(path),
        ["path"] = path ?? "",
        ["version"] = version
    };
}

internal static class PowerShellHost
{
    public static string? PowerShell7Path { get; } = ResolvePowerShell7();
    public static string WindowsPowerShellPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        @"WindowsPowerShell\v1.0\powershell.exe");
    public static string PowerShell7Version { get; } = ShellVersion(PowerShell7Path);
    public static string WindowsPowerShellVersion { get; } = ShellVersion(WindowsPowerShellPath);

    public static ShellResolution Resolve(string selection)
    {
        var key = selection == "auto" ? PowerShell7Path is not null ? "powershell7" : "windows-powershell" : selection;
        return key switch
        {
            "powershell7" when PowerShell7Path is not null => new(key, "PowerShell 7", PowerShell7Path, PowerShell7Version, "{574e775e-4f2a-5b96-ac1e-a2962a402336}"),
            "powershell7" => throw new FileNotFoundException("PowerShell 7 wurde auf diesem Rechner nicht gefunden."),
            "windows-powershell" when File.Exists(WindowsPowerShellPath) => new(key, "Windows PowerShell 5.1", WindowsPowerShellPath, WindowsPowerShellVersion, "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}"),
            _ => throw new FileNotFoundException("Keine unterstützte PowerShell-Version wurde gefunden.")
        };
    }

    public static string FileVersion(string? path)
    {
        if (path is null || !File.Exists(path)) return "";
        try { return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? FileVersionInfo.GetVersionInfo(path).FileVersion ?? ""; }
        catch { return ""; }
    }

    private static string ShellVersion(string? path)
    {
        if (path is null || !File.Exists(path)) return "";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) process.Kill(true);
            return output.Trim();
        }
        catch { return ""; }
    }

    private static string? ResolvePowerShell7()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Microsoft.PowerShell_8wekyb3d8bbwe", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}

internal static class TerminalHost
{
    public static string ExecutablePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "WindowsApps",
        "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        "wt.exe");

    public static string IconExecutablePath { get; } = ResolveIconExecutable();

    public static string ResolveTerminal(string selection)
    {
        var resolved = selection == "auto" ? File.Exists(ExecutablePath) ? "windows-terminal" : "console" : selection;
        if (resolved == "windows-terminal" && !File.Exists(ExecutablePath))
            throw new FileNotFoundException("Windows Terminal wurde auf diesem Rechner nicht gefunden.", ExecutablePath);
        if (resolved is not ("windows-terminal" or "console"))
            throw new ArgumentException("Unbekannte Terminal-Auswahl.");
        return resolved;
    }

    public static void OpenPowerShell(string workingDirectory, string? command = null)
    {
        var preferences = TerminalSettingsStore.LoadCurrent();
        var terminal = ResolveTerminal(preferences.Terminal);
        var shell = PowerShellHost.Resolve(preferences.Shell);
        var startInfo = new ProcessStartInfo
        {
            FileName = terminal == "windows-terminal" ? ExecutablePath : shell.Path,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        if (terminal == "windows-terminal")
        {
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("new-tab");
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(shell.ProfileId);
            startInfo.ArgumentList.Add("--startingDirectory");
            startInfo.ArgumentList.Add(workingDirectory);
            startInfo.ArgumentList.Add(shell.Path);
        }

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoExit");
        var initialization = "$env:NO_COLOR=$null; $env:TERM=$null; $env:COLORTERM=$null";
        if (!string.IsNullOrWhiteSpace(command)) initialization += $"; {command}";
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(initialization)));
        Process.Start(startInfo);
    }

    private static string ResolveIconExecutable()
    {
        const string packagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(packagesKey);
            var packageName = packages?.GetSubKeyNames()
                .Where(name => name.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (packageName is not null)
            {
                using var package = packages!.OpenSubKey(packageName);
                if (package?.GetValue("PackageRootFolder") is string root)
                {
                    var executable = Path.Combine(root, "WindowsTerminal.exe");
                    if (File.Exists(executable)) return executable;
                }
            }
        }
        catch
        {
        }
        return ExecutablePath;
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
    private readonly ProjectStore _projects;

    public CodexClient(ProjectStore projects) => _projects = projects;

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
        var items = new List<JsonObject>();
        var activeProcessSessions = DetectActiveProcessSessions();
        foreach (var node in data)
            if (node is JsonObject thread)
            {
                var item = NormalizeDatabaseThread(thread);
                var id = item["id"]?.GetValue<string>();
                item["active"] = id is not null && (activeProcessSessions.Contains(id) || IsSessionActive(id));
                normalized.Add(item);
                items.Add(item);
            }
        foreach (var item in items) _projects.Apply(item);
        AttachShadowSessions(items);
        return normalized;
    }

    private static void AttachShadowSessions(IReadOnlyList<JsonObject> items)
    {
        var parents = items
            .Where(item => !string.Equals(item["threadSource"]?.GetValue<string>(), "agent_created_thread", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var child in items.Where(item => string.Equals(item["threadSource"]?.GetValue<string>(), "agent_created_thread", StringComparison.OrdinalIgnoreCase)))
        {
            var childId = child["id"]?.GetValue<string>();
            var childCreated = child["createdAt"]?.GetValue<long>() ?? 0;
            var originator = child["originator"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(childId) || childCreated <= 0) continue;

            var candidates = parents
                .Where(candidate => string.Equals(candidate["originator"]?.GetValue<string>() ?? "", originator, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => new
                {
                    Item = candidate,
                    Distance = Math.Min(
                        Math.Abs((candidate["createdAt"]?.GetValue<long>() ?? 0) - childCreated),
                        Math.Abs((candidate["updatedAt"]?.GetValue<long>() ?? 0) - childCreated))
                })
                .Where(candidate => candidate.Distance <= TimeSpan.FromMinutes(10).TotalMilliseconds)
                .OrderBy(candidate => candidate.Distance)
                .Take(8)
                .ToArray();

            var parent = candidates
                .Select(candidate => candidate.Item)
                .FirstOrDefault(candidate => RolloutReferencesThread(candidate["storagePath"]?.GetValue<string>(), childId));
            parent ??= candidates.FirstOrDefault(candidate => candidate.Distance <= TimeSpan.FromMinutes(1).TotalMilliseconds)?.Item;

            if (parent?["id"] is JsonValue parentId) child["parentId"] = parentId.DeepClone();
        }
    }

    private static bool RolloutReferencesThread(string? rolloutPath, string threadId)
    {
        if (string.IsNullOrWhiteSpace(rolloutPath) || !File.Exists(rolloutPath)) return false;
        try
        {
            foreach (var line in File.ReadLines(rolloutPath))
                if (line.Contains(threadId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch
        {
        }
        return false;
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
        _projects.Apply(detail);
        detail["active"] = DetectActiveProcessSessions().Contains(threadId) || IsSessionActive(threadId);
        return detail;
    }

    public void SetPinned(string threadId, bool pinned)
    {
        ValidateThreadId(threadId);
        CodexStateStore.SetPinned(threadId, pinned);
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
        var command = $"$sessionMutex = [System.Threading.Mutex]::new($true, '{mutexName}'); try {{ npx @openai/codex resume '{threadId}' }} finally {{ $sessionMutex.ReleaseMutex(); $sessionMutex.Dispose() }}";
        try
        {
            TerminalHost.OpenPowerShell(cwd, command);
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
            await SendAsync(process, new { method = "initialize", id = 1, @params = new { clientInfo = new { name = "codex_cli_manager", title = "Codex CLI Manager", version = ProductInfo.Version }, capabilities = (object?)null } });
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
            ["threadSource"] = rawSource,
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
    private const int OpenReadWrite = 2;
    private static readonly string DatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "state_5.sqlite");

    public static long ChangeStamp()
    {
        var stamp = 17L;
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (!File.Exists(path)) continue;
            var info = new FileInfo(path);
            stamp = unchecked(stamp * 31 + info.LastWriteTimeUtc.Ticks);
            stamp = unchecked(stamp * 31 + info.Length);
        }
        return stamp;
    }

    public static JsonArray Query(string sql)
    {
        if (!File.Exists(DatabasePath)) throw new FileNotFoundException("Codex-Datenbank wurde nicht gefunden.", DatabasePath);
        var result = new JsonArray();
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(sqlite3_open_v2(DatabasePath, out database, OpenReadOnly, null), database, "Codex-Datenbank konnte nicht geöffnet werden");
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

    public static void SetPinned(string threadId, bool pinned)
    {
        if (!Guid.TryParse(threadId, out _)) throw new ArgumentException("Ungültige Session-ID.");
        if (!File.Exists(DatabasePath)) throw new FileNotFoundException("Codex-Datenbank wurde nicht gefunden.", DatabasePath);
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(sqlite3_open_v2(DatabasePath, out database, OpenReadWrite, null), database, "Codex-Datenbank konnte nicht zum Schreiben geöffnet werden");
            sqlite3_busy_timeout(database, 3000);
            var sql = $"update threads set is_pinned = {(pinned ? 1 : 0)} where id = '{threadId}'";
            Check(sqlite3_prepare_v2(database, sql, -1, out statement, IntPtr.Zero), database, "Favorit konnte nicht vorbereitet werden");
            var step = sqlite3_step(statement);
            Check(step == Done ? Ok : step, database, "Favorit konnte nicht gespeichert werden");
            if (sqlite3_changes(database) != 1) throw new KeyNotFoundException("Session wurde nicht gefunden.");
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
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_changes(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_name(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
}
