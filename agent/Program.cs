using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Win32;

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
    Process.Start(new ProcessStartInfo(AppEndpoint.Read()) { UseShellExecute = true });
    return;
}
var appUrl = AppEndpoint.SelectAndSave();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(appUrl);
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<SessionMetadataStore>();
builder.Services.AddSingleton<TerminalSettingsStore>();
builder.Services.AddSingleton<LanguageSettingsStore>();
builder.Services.AddSingleton<UpdateStatusStore>();
builder.Services.AddSingleton<GitStatusService>();
builder.Services.AddSingleton<CodexClient>();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));
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
app.MapGet("/api/events", async (HttpContext context, IHostApplicationLifetime lifetime, CancellationToken cancellation) =>
{
    using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.ApplicationStopping);
    var eventToken = eventCancellation.Token;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Connection = "keep-alive";
    await context.Response.WriteAsync("retry: 2000\n\n", eventToken);
    await context.Response.Body.FlushAsync(eventToken);

    var lastStamp = CodexStateStore.ChangeStamp();
    var lastUpdate = DateTime.UtcNow;
    try
    {
        while (!eventToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), eventToken);
            var stamp = CodexStateStore.ChangeStamp();
            var fallbackDue = DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(5);
            if (stamp == lastStamp && !fallbackDue) continue;

            lastStamp = stamp;
            lastUpdate = DateTime.UtcNow;
            await context.Response.WriteAsync($"event: sessions\ndata: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\n\n", eventToken);
            await context.Response.Body.FlushAsync(eventToken);
        }
    }
    catch (OperationCanceledException) when (eventToken.IsCancellationRequested)
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

app.MapGet("/api/language", (LanguageSettingsStore settings) => Results.Ok(new { data = settings.GetView() }));

app.MapPost("/api/language", (LanguageRequest request, LanguageSettingsStore settings) =>
{
    try { return Results.Ok(new { data = settings.Save(request.Language) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 400); }
});

app.MapGet("/api/update-status", async (bool? refresh, UpdateStatusStore updates, CancellationToken cancellation) =>
    Results.Ok(new { data = await updates.GetAsync(refresh ?? false, cancellation) }));

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

app.MapPost("/api/projects/sessions/{threadId}/unassign", (string threadId, ProjectStore projects) =>
{
    try
    {
        projects.Unassign(threadId);
        return Results.Ok(new { data = new { unassigned = true } });
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

app.MapPost("/api/session/{threadId}/title", (string threadId, SessionTitleRequest request, CodexClient codex) =>
{
    try { return Results.Ok(new { data = codex.SetTitle(threadId, request.Title) }); }
    catch (Exception error) { return Results.Problem(error.Message, statusCode: 400); }
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
    app.Lifetime.ApplicationStarted.Register(() => Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true }));
using var updateShutdown = new UpdateShutdownListener(app.Lifetime);
using var trayIcon = new TrayIconHost(appUrl, app.Lifetime.StopApplication, app.Services.GetRequiredService<LanguageSettingsStore>());
try
{
    await app.RunAsync();
}
catch (Exception error)
{
    System.Windows.Forms.MessageBox.Show(
        $"Codex CLI Manager konnte nicht gestartet werden.\n\n{error.Message}",
        "Codex CLI Manager",
        System.Windows.Forms.MessageBoxButtons.OK,
        System.Windows.Forms.MessageBoxIcon.Error);
}

internal sealed record ActionRequest(string ThreadId);
internal sealed record CreateProjectRequest(string Name);
internal sealed record TerminalSettingsRequest(string Terminal, string Shell);
internal sealed record LanguageRequest(string Language);
internal sealed record SessionTitleRequest(string? Title);

internal static class AppEndpoint
{
    private const int PreferredPort = 4180;
    private static readonly string StatePath = Path.Combine(UserInstallation.InstallDirectory, "endpoint.txt");

    public static string SelectAndSave()
    {
        for (var port = PreferredPort; port < PreferredPort + 20; port++)
        {
            System.Net.Sockets.TcpListener? listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                var url = $"http://127.0.0.1:{port}";
                Directory.CreateDirectory(UserInstallation.InstallDirectory);
                File.WriteAllText(StatePath, url, new UTF8Encoding(false));
                return url;
            }
            catch (System.Net.Sockets.SocketException)
            {
            }
            finally
            {
                listener?.Stop();
            }
        }
        const string message = "Kein freier lokaler Port zwischen 4180 und 4199 gefunden. Bitte eine andere lokale Anwendung schließen und den Codex CLI Manager erneut starten.";
        System.Windows.Forms.MessageBox.Show(message, "Codex CLI Manager", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        throw new InvalidOperationException(message);
    }

    public static string Read()
    {
        try
        {
            var value = File.ReadAllText(StatePath).Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host == "127.0.0.1") return value;
        }
        catch
        {
        }
        return $"http://127.0.0.1:{PreferredPort}";
    }
}

internal sealed class UpdateStatusStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JsonObject? _cached;
    private DateTimeOffset _checkedAt;

    public async Task<JsonObject> GetAsync(bool refresh, CancellationToken cancellation)
    {
        if (IsCacheFresh(refresh))
            return (JsonObject)_cached!.DeepClone();
        await _gate.WaitAsync(cancellation);
        try
        {
            if (IsCacheFresh(refresh))
                return (JsonObject)_cached!.DeepClone();
            try
            {
                var update = await UpdateService.CheckForUpdateAsync(cancellation);
                _cached = new JsonObject
                {
                    ["status"] = update is null ? "current" : "available",
                    ["currentVersion"] = ProductInfo.Version,
                    ["version"] = update?.Version,
                    ["checkedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }
            catch (Exception error)
            {
                _cached = new JsonObject
                {
                    ["status"] = "error",
                    ["currentVersion"] = ProductInfo.Version,
                    ["error"] = error.Message,
                    ["checkedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }
            _checkedAt = DateTimeOffset.UtcNow;
            return (JsonObject)_cached.DeepClone();
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsCacheFresh(bool refresh)
    {
        if (refresh || _cached is null) return false;
        var isError = _cached["status"]?.GetValue<string>() == "error";
        var maximumAge = isError ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(15);
        return DateTimeOffset.UtcNow - _checkedAt < maximumAge;
    }
}

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

    public void Unassign(string threadId)
    {
        if (!Guid.TryParse(threadId, out _)) throw new ArgumentException("Ungültige Session-ID.");
        lock (_sync)
        {
            if (_state.Assignments.Remove(threadId)) Save();
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
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var stored = Normalize(JsonSerializer.Deserialize<StoredProjectState>(json, StorageJson) ?? new StoredProjectState());
                if (IsUnchangedAutoImport(json, stored))
                {
                    var empty = new StoredProjectState();
                    Persist(empty);
                    return empty;
                }
                return stored;
            }
        }
        catch
        {
        }
        return new StoredProjectState();
    }

    private static bool IsUnchangedAutoImport(string currentJson, StoredProjectState state)
    {
        if (state.Projects.Count == 0 || state.Assignments.Count != 0) return false;
        JsonNode? current;
        try { current = JsonNode.Parse(currentJson); }
        catch { return false; }
        foreach (var importPath in Directory.EnumerateFiles(UserInstallation.InstallDirectory, "projects.auto-import-*.json"))
        {
            try
            {
                if (JsonNode.DeepEquals(current, JsonNode.Parse(File.ReadAllText(importPath)))) return true;
            }
            catch
            {
            }
        }
        return false;
    }

    private void Save()
    {
        Persist(_state);
    }

    private void Persist(StoredProjectState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, StorageJson), new UTF8Encoding(false));
        File.Move(temporary, _path, overwrite: true);
    }

    private static StoredProjectState Normalize(StoredProjectState state)
    {
        state.Projects ??= [];
        state.Assignments = new Dictionary<string, string>(state.Assignments ?? [], StringComparer.OrdinalIgnoreCase);
        return state;
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

internal sealed class SessionMetadataStore
{
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(UserInstallation.InstallDirectory, "session-metadata.json");
    private StoredSessionMetadata _state;

    public SessionMetadataStore() => _state = Load();

    public void Apply(JsonObject session)
    {
        var threadId = session["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(threadId)) return;
        lock (_sync)
        {
            if (_state.Titles.TryGetValue(threadId, out var customTitle))
            {
                var originalTitle = session["title"]?.GetValue<string>() ?? "";
                session["originalTitle"] = originalTitle;
                session["title"] = customTitle;
                session["customTitle"] = true;
            }
            if (_state.Pinned.TryGetValue(threadId, out var pinned)) session["pinned"] = pinned;
        }
    }

    public JsonObject SaveTitle(string threadId, string? value, string originalTitle)
    {
        if (!Guid.TryParse(threadId, out _)) throw new ArgumentException("Ungültige Session-ID.");
        var title = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length > 160) throw new ArgumentException("Der Session-Titel darf höchstens 160 Zeichen lang sein.");
        var customized = !string.IsNullOrWhiteSpace(title) && !title.Equals(originalTitle, StringComparison.CurrentCulture);
        lock (_sync)
        {
            if (customized) _state.Titles[threadId] = title;
            else _state.Titles.Remove(threadId);
            Save();
        }
        return new JsonObject
        {
            ["title"] = customized ? title : originalTitle,
            ["originalTitle"] = originalTitle,
            ["customTitle"] = customized
        };
    }

    public void SavePinned(string threadId, bool pinned)
    {
        if (!Guid.TryParse(threadId, out _)) throw new ArgumentException("Ungültige Session-ID.");
        lock (_sync)
        {
            _state.Pinned[threadId] = pinned;
            Save();
        }
    }

    private StoredSessionMetadata Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var root = JsonNode.Parse(json) as JsonObject;
                if (root?.ContainsKey("titles") == true || root?.ContainsKey("pinned") == true)
                {
                    var stored = JsonSerializer.Deserialize<StoredSessionMetadata>(json, StorageJson) ?? new StoredSessionMetadata();
                    return Normalize(stored);
                }

                // v0.8.0 stored custom titles as a plain dictionary. Keep those values
                // when migrating to the combined app-owned metadata format.
                var legacyTitles = JsonSerializer.Deserialize<Dictionary<string, string>>(json, StorageJson);
                return new StoredSessionMetadata
                {
                    Titles = new Dictionary<string, string>(legacyTitles ?? [], StringComparer.OrdinalIgnoreCase)
                };
            }
        }
        catch
        {
        }
        return new StoredSessionMetadata();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_state, StorageJson), new UTF8Encoding(false));
        File.Move(temporary, _path, overwrite: true);
    }

    private static StoredSessionMetadata Normalize(StoredSessionMetadata value) => new()
    {
        Titles = new Dictionary<string, string>(value.Titles ?? [], StringComparer.OrdinalIgnoreCase),
        Pinned = new Dictionary<string, bool>(value.Pinned ?? [], StringComparer.OrdinalIgnoreCase)
    };

    private sealed class StoredSessionMetadata
    {
        public Dictionary<string, string> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Pinned { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class LanguageSettingsStore
{
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(UserInstallation.InstallDirectory, "language.json");
    private string _language;

    public LanguageSettingsStore() => _language = Load();

    public event Action? Changed;

    public string Current
    {
        get { lock (_sync) return _language; }
    }

    public JsonObject GetView() => new()
    {
        ["language"] = Current,
        ["supported"] = new JsonArray("de", "en", "da")
    };

    public JsonObject Save(string? value)
    {
        var language = Normalize(value);
        lock (_sync)
        {
            _language = language;
            Directory.CreateDirectory(UserInstallation.InstallDirectory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredLanguage(language), StorageJson), new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
        }
        Changed?.Invoke();
        return GetView();
    }

    public string Text(string key) => TrayTranslations.Get(Current, key);
    public string Format(string key, params object[] arguments) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Text(key), arguments);

    private string Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var stored = JsonSerializer.Deserialize<StoredLanguage>(File.ReadAllText(_path), StorageJson);
                if (stored is not null) return Normalize(stored.Language);
            }
        }
        catch
        {
        }
        var detected = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return detected is "de" or "da" ? detected : "en";
    }

    private static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized is not ("de" or "en" or "da")) throw new ArgumentException("Unsupported language.");
        return normalized;
    }

    private sealed record StoredLanguage(string Language);
}

internal static class TrayTranslations
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Get(string language, string key)
    {
        var resourceKey = $"tray.{key}";
        var selected = Load(language);
        if (selected.TryGetValue(resourceKey, out var value)) return value;
        return Load("en").TryGetValue(resourceKey, out var fallback) ? fallback : key;
    }

    private static IReadOnlyDictionary<string, string> Load(string language) => Cache.GetOrAdd(language, static selectedLanguage =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "locales", $"{selectedLanguage}.json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    });
}

internal sealed class TrayIconHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();

    public TrayIconHost(string appUrl, Action stopApplication, LanguageSettingsStore language)
    {
        _thread = new Thread(() => Run(appUrl, stopApplication, language))
        {
            IsBackground = true,
            Name = "Codex CLI Manager Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(string appUrl, Action stopApplication, LanguageSettingsStore language)
    {
        var trayThreadId = Environment.CurrentManagedThreadId;
        System.Windows.Forms.Application.EnableVisualStyles();
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        var openBrowser = menu.Items.Add("");
        var openPowerShell = menu.Items.Add("");
        var update = menu.Items.Add("");
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var about = menu.Items.Add("");
        var exit = menu.Items.Add("");
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
        about.Click += (_, _) => OpenBrowser($"{appUrl.TrimEnd('/')}/about");
        update.Click += async (_, _) =>
        {
            update.Enabled = false;
            update.Text = language.Text("searching");
            try
            {
                var available = update.Tag as UpdateInfo ?? await UpdateService.CheckForUpdateAsync(CancellationToken.None);
                if (available is null)
                {
                    update.Text = language.Text("updates");
                    System.Windows.Forms.MessageBox.Show(
                        language.Format("current", ProductInfo.Version),
                        language.Text("noUpdates"),
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                    return;
                }

                var answer = System.Windows.Forms.MessageBox.Show(
                    language.Format("available", available.Version),
                    language.Text("updateTitle"),
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Information);
                if (answer != System.Windows.Forms.DialogResult.Yes)
                {
                    update.Text = language.Format("install", available.Version);
                    update.Tag = available;
                    return;
                }

                update.Text = language.Format("downloading", available.Version);
                using var progress = new UpdateProgressWindow(
                    language.Text("updateTitle"),
                    language.Format("downloading", available.Version));
                await UpdateService.DownloadAndLaunchAsync(available, stage => progress.Report(stage switch
                {
                    UpdateStage.Verifying => language.Text("verifying"),
                    UpdateStage.Preparing => language.Text("preparing"),
                    _ => language.Format("downloading", available.Version)
                }), CancellationToken.None);
                progress.Report(language.Text("waiting"));
                icon.Visible = false;
                stopApplication();
                System.Windows.Forms.Application.ExitThread();
            }
            catch (Exception error)
            {
                update.Text = language.Text("updates");
                update.Tag = null;
                System.Windows.Forms.MessageBox.Show(
                    error.Message,
                    language.Text("failed"),
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
                update.Text = language.Format("install", available.Version);
            }
            catch
            {
            }
        };
        void ApplyLanguage()
        {
            if (Environment.CurrentManagedThreadId != trayThreadId)
            {
                if (!menu.IsHandleCreated) return;
                try { menu.BeginInvoke((Action)ApplyLanguage); } catch { }
                return;
            }
            openBrowser.Text = language.Text("open");
            openPowerShell.Text = language.Text("monitor");
            update.Text = update.Tag is UpdateInfo available ? language.Format("install", available.Version) : language.Text("updates");
            about.Text = language.Format("aboutHistory", ProductInfo.Version);
            exit.Text = language.Text("exit");
        }
        language.Changed += ApplyLanguage;
        ApplyLanguage();
        updateTimer.Start();
        _ready.Set();
        System.Windows.Forms.Application.Run();
        language.Changed -= ApplyLanguage;
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

internal enum UpdateStage
{
    Downloading,
    Verifying,
    Preparing
}

internal sealed class UpdateProgressWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private System.Windows.Forms.Form? _form;
    private System.Windows.Forms.Label? _status;

    public UpdateProgressWindow(string title, string initialStatus)
    {
        _thread = new Thread(() => Run(title, initialStatus))
        {
            IsBackground = true,
            Name = "Codex CLI Manager Update"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(string title, string initialStatus)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var icon = AppIcon.Create();
        using var iconBitmap = icon.ToBitmap();
        using var form = new System.Windows.Forms.Form
        {
            Text = title,
            Icon = icon,
            Width = 430,
            Height = 155,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ControlBox = false,
            ShowInTaskbar = true
        };
        var productIcon = new System.Windows.Forms.PictureBox
        {
            Image = iconBitmap,
            Left = 24,
            Top = 25,
            Width = 48,
            Height = 48,
            SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        };
        var heading = new System.Windows.Forms.Label
        {
            Text = "Codex CLI Manager",
            AutoSize = false,
            Left = 88,
            Top = 18,
            Width = 306,
            Height = 24,
            Font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold)
        };
        var status = new System.Windows.Forms.Label
        {
            Text = initialStatus,
            AutoSize = false,
            Left = 88,
            Top = 49,
            Width = 306,
            Height = 22
        };
        var activity = new System.Windows.Forms.ProgressBar
        {
            Left = 88,
            Top = 79,
            Width = 306,
            Height = 9,
            Style = System.Windows.Forms.ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28
        };
        form.Controls.AddRange([productIcon, heading, status, activity]);
        _form = form;
        _status = status;
        _ready.Set();
        System.Windows.Forms.Application.Run(form);
        _status = null;
        _form = null;
    }

    public void Report(string text)
    {
        var status = _status;
        if (status is null || !status.IsHandleCreated) return;
        try { status.BeginInvoke((Action)(() => status.Text = text)); } catch { }
    }

    public void Dispose()
    {
        var form = _form;
        if (form is not null && form.IsHandleCreated)
        {
            try { form.BeginInvoke((Action)form.Close); } catch { }
        }
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}

internal sealed class UpdateShutdownListener : IDisposable
{
    private const string SignalName = "Local\\CodexSessionManager.UpdateShutdown";
    private readonly EventWaitHandle _signal = new(false, EventResetMode.AutoReset, SignalName);
    private readonly CancellationTokenRegistration _stopping;
    private readonly Task _listener;

    public UpdateShutdownListener(IHostApplicationLifetime lifetime)
    {
        _stopping = lifetime.ApplicationStopping.Register(() =>
        {
            try { _signal.Set(); } catch { }
        });
        _listener = Task.Run(() =>
        {
            _signal.WaitOne();
            if (!lifetime.ApplicationStopping.IsCancellationRequested) lifetime.StopApplication();
        });
    }

    public static void RequestStop()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(SignalName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _stopping.Dispose();
        try { _signal.Set(); } catch { }
        try { _listener.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _signal.Dispose();
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
    public const string Version = "0.9.10";
}

internal sealed record UpdateInfo(string Version, string PackageUrl, string ChecksumUrl);

internal static class UpdateService
{
    private const string Repository = "flensbursche/Codex_CLI_Manager";
    private const string PackageName = "CodexSessionManager-Setup-win-x64.zip";
    private static readonly string UpdatesRoot = Path.Combine(Path.GetTempPath(), "CodexSessionManager", "updates");
    private static readonly HttpClient Client = CreateClient();
    private static readonly HttpClient LatestReleaseClient = CreateClient(followRedirects: false);

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellation)
    {
        var tag = await ResolveLatestTagAsync(cancellation);
        if (tag is null) return null;
        var latest = ParseVersion(tag);
        if (latest <= ParseVersion(ProductInfo.Version)) return null;

        var releaseBase = $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/";
        var packageUrl = releaseBase + PackageName;
        var checksumUrl = releaseBase + PackageName + ".sha256";
        ValidateDownloadUrl(packageUrl);
        ValidateDownloadUrl(checksumUrl);
        return new UpdateInfo(latest.ToString(3), packageUrl, checksumUrl);
    }

    private static async Task<string?> ResolveLatestTagAsync(CancellationToken cancellation)
    {
        var sources = new[]
        {
            $"https://github.com/{Repository}/releases/latest",
            $"https://github.com/{Repository}/releases/latest/download/{PackageName}.sha256"
        };
        var failures = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                using var response = await LatestReleaseClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellation);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
                var tag = ReadReleaseTag(response.Headers.Location);
                if (tag is not null) return tag;
                failures.Add($"HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                failures.Add("Zeitüberschreitung");
            }
            catch (HttpRequestException error)
            {
                failures.Add(error.StatusCode is null ? "Netzwerkfehler" : $"HTTP {(int)error.StatusCode}");
            }
        }
        if (failures.Count == 0) return null;
        throw new InvalidOperationException($"Die Release-Information ist vorübergehend nicht erreichbar ({string.Join(", ", failures.Distinct())}). Bitte später erneut prüfen.");
    }

    private static string? ReadReleaseTag(Uri? location)
    {
        if (location is null) return null;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?', 2)[0];
        foreach (var marker in new[] { $"/{Repository}/releases/tag/", $"/{Repository}/releases/download/" })
        {
            if (!path.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = path[marker.Length..].Trim('/');
            var tag = Uri.UnescapeDataString(remainder.Split('/', 2)[0]);
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }
        return null;
    }

    public static async Task DownloadAndLaunchAsync(UpdateInfo update, Action<UpdateStage>? progress, CancellationToken cancellation)
    {
        var updateDirectory = Path.Combine(UpdatesRoot, $"{update.Version}-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(updateDirectory, "payload");
        var packagePath = Path.Combine(updateDirectory, PackageName);
        Directory.CreateDirectory(payloadDirectory);
        var stageDuration = Stopwatch.StartNew();
        progress?.Invoke(UpdateStage.Downloading);
        await DownloadFileAsync(update.PackageUrl, packagePath, cancellation);
        await EnsureMinimumStageAsync(stageDuration, cancellation);

        stageDuration.Restart();
        progress?.Invoke(UpdateStage.Verifying);
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
        await EnsureMinimumStageAsync(stageDuration, cancellation);

        stageDuration.Restart();
        progress?.Invoke(UpdateStage.Preparing);
        ZipFile.ExtractToDirectory(packagePath, payloadDirectory, overwriteFiles: true);
        var stagedExecutable = Directory.EnumerateFiles(payloadDirectory, "CodexSessionManager.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("Das Update enthält keine CodexSessionManager.exe.");
        var payloadRoot = Path.GetDirectoryName(stagedExecutable)!;
        var startInfo = new ProcessStartInfo(stagedExecutable) { UseShellExecute = true, WorkingDirectory = payloadRoot };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(UserInstallation.InstallDirectory);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await EnsureMinimumStageAsync(stageDuration, cancellation);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Der Update-Helfer konnte nicht gestartet werden.");
    }

    public static bool TryApplyStagedUpdate(string[] arguments)
    {
        var updateIndex = Array.FindIndex(arguments, value => value.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));
        if (updateIndex < 0) return false;
        var language = new LanguageSettingsStore();
        using var progress = new UpdateProgressWindow(language.Text("updateTitle"), language.Text("waiting"));
        try
        {
            if (arguments.Length <= updateIndex + 2 || !int.TryParse(arguments[updateIndex + 2], out var processId))
                throw new ArgumentException("Ungültige Update-Parameter.");
            var destination = Path.GetFullPath(arguments[updateIndex + 1]);
            var expectedDestination = Path.GetFullPath(UserInstallation.InstallDirectory);
            if (!destination.Equals(expectedDestination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Das Update-Ziel ist nicht zulässig.");

            var autostartWasEnabled = AutoStartManager.IsEnabled();
            var stageDuration = Stopwatch.StartNew();
            UpdateShutdownListener.RequestStop();
            WaitForManagerExit(processId);
            EnsureMinimumStage(stageDuration);
            stageDuration.Restart();
            progress.Report(language.Text("installing"));
            CopyPayload(AppContext.BaseDirectory, destination);
            var installedExecutable = Path.Combine(destination, "CodexSessionManager.exe");
            if (autostartWasEnabled) AutoStartManager.Enable(installedExecutable);
            else AutoStartManager.Disable();
            EnsureMinimumStage(stageDuration);
            stageDuration.Restart();
            progress.Report(language.Text("restarting"));
            Thread.Sleep(400);
            Process.Start(new ProcessStartInfo(installedExecutable) { UseShellExecute = true });
            EnsureMinimumStage(stageDuration, 1100);
        }
        catch (Exception error)
        {
            System.Windows.Forms.MessageBox.Show(
                error.Message,
                language.Text("failed"),
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

    private static HttpClient CreateClient(bool followRedirects = true)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Codex-CLI-Manager/{ProductInfo.Version}");
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
            if (process.WaitForExit(10000)) return;
            process.Kill();
            process.WaitForExit(10000);
            if (!process.HasExited) throw new InvalidOperationException("Der bisherige Codex CLI Manager konnte nicht beendet werden.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task EnsureMinimumStageAsync(Stopwatch stageDuration, CancellationToken cancellation, int minimumMilliseconds = 700)
    {
        var remaining = minimumMilliseconds - (int)stageDuration.ElapsedMilliseconds;
        if (remaining > 0) await Task.Delay(remaining, cancellation);
    }

    private static void EnsureMinimumStage(Stopwatch stageDuration, int minimumMilliseconds = 700)
    {
        var remaining = minimumMilliseconds - (int)stageDuration.ElapsedMilliseconds;
        if (remaining > 0) Thread.Sleep(remaining);
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
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        if (terminal == "windows-terminal")
        {
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("new");
            startInfo.ArgumentList.Add("new-tab");
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(shell.ProfileId);
            startInfo.ArgumentList.Add("--startingDirectory");
            startInfo.ArgumentList.Add(workingDirectory);
            startInfo.ArgumentList.Add("--appendCommandLine");
            startInfo.ArgumentList.Add("--");
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

internal sealed class GitStatusService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    public async Task ApplyAsync(IReadOnlyList<JsonObject> sessions, CancellationToken cancellation)
    {
        var directories = sessions
            .Select(session => session["cwd"]?.GetValue<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();

        var inspections = await Task.WhenAll(directories.Select(async directory =>
            new KeyValuePair<string, JsonObject?>(directory, await GetAsync(directory, cancellation))));
        var byDirectory = inspections.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var directory = session["cwd"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { directory = Path.GetFullPath(directory); }
            catch { continue; }
            if (byDirectory.TryGetValue(directory, out var git) && git is not null)
                session["git"] = git.DeepClone();
        }
    }

    private async Task<JsonObject?> GetAsync(string directory, CancellationToken cancellation)
    {
        if (_cache.TryGetValue(directory, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Value is null ? null : (JsonObject)cached.Value.DeepClone();

        await _concurrency.WaitAsync(cancellation);
        try
        {
            if (_cache.TryGetValue(directory, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                return cached.Value is null ? null : (JsonObject)cached.Value.DeepClone();
            var value = await InspectAsync(directory, cancellation);
            _cache[directory] = new CacheEntry(value, DateTimeOffset.UtcNow + CacheDuration);
            return value is null ? null : (JsonObject)value.DeepClone();
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static async Task<JsonObject?> InspectAsync(string directory, CancellationToken cancellation)
    {
        var status = await RunAsync(directory, ["status", "--porcelain=v2", "--branch", "--untracked-files=normal"], cancellation);
        if (status.ExitCode != 0)
        {
            var repositoryRoot = FindRepositoryRoot(directory);
            if (repositoryRoot is null) return null;
            var (fallbackRemoteHost, fallbackProvider) = ReadRemoteFromConfig(repositoryRoot);
            return new JsonObject
            {
                ["linked"] = true,
                ["unavailable"] = true,
                ["repositoryRoot"] = repositoryRoot,
                ["remoteHost"] = fallbackRemoteHost,
                ["provider"] = fallbackProvider,
                ["error"] = Limit(status.Error.Trim(), 800)
            };
        }

        string? branch = null;
        var ahead = 0;
        var behind = 0;
        var changedFiles = 0;
        var conflicts = 0;
        var changes = new JsonArray();
        foreach (var line in status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                branch = line[14..].Trim();
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var value in line[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (value.StartsWith('+') && int.TryParse(value[1..], out var parsedAhead)) ahead = parsedAhead;
                    if (value.StartsWith('-') && int.TryParse(value[1..], out var parsedBehind)) behind = parsedBehind;
                }
            }
            else if (!line.StartsWith('#'))
            {
                changedFiles++;
                if (line.StartsWith("u ", StringComparison.Ordinal)) conflicts++;
                if (changes.Count < 12 && ParseChange(line) is JsonObject change) changes.Add(change);
            }
        }

        if (branch is "(detached)" or "(unknown)") branch = null;
        var lastCommit = await RunAsync(directory, ["--no-pager", "log", "-1", "--format=%H%x1f%h%x1f%cI%x1f%s"], cancellation);
        var commitParts = lastCommit.ExitCode == 0
            ? lastCommit.Output.Trim().Split('\x1f', 4)
            : [];
        var remotes = await RunAsync(directory, ["remote", "-v"], cancellation);
        var (remoteHost, provider) = remotes.ExitCode == 0
            ? ParseRemote(remotes.Output)
            : (null, null);

        return new JsonObject
        {
            ["linked"] = true,
            ["dirty"] = changedFiles > 0,
            ["changedFiles"] = changedFiles,
            ["conflicts"] = conflicts,
            ["changes"] = changes,
            ["branch"] = branch,
            ["ahead"] = ahead,
            ["behind"] = behind,
            ["remoteHost"] = remoteHost,
            ["provider"] = provider,
            ["lastCommitHash"] = commitParts.Length > 0 ? commitParts[0] : null,
            ["lastCommitShort"] = commitParts.Length > 1 ? commitParts[1] : null,
            ["lastCommitAt"] = commitParts.Length > 2 ? commitParts[2] : null,
            ["lastCommitSubject"] = commitParts.Length > 3 ? Limit(commitParts[3], 200) : null
        };
    }

    private static string? FindRepositoryRoot(string directory)
    {
        try
        {
            for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            {
                var marker = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker)) return current.FullName;
            }
        }
        catch
        {
        }
        return null;
    }

    private static (string? Host, string? Provider) ReadRemoteFromConfig(string repositoryRoot)
    {
        try
        {
            var marker = Path.Combine(repositoryRoot, ".git");
            string? gitDirectory = Directory.Exists(marker) ? marker : null;
            if (gitDirectory is null && File.Exists(marker))
            {
                var pointer = File.ReadLines(marker).FirstOrDefault()?.Trim();
                const string prefix = "gitdir:";
                if (pointer?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                {
                    var path = pointer[prefix.Length..].Trim();
                    gitDirectory = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));
                }
            }
            if (gitDirectory is null) return (null, null);

            var configPath = Path.Combine(gitDirectory, "config");
            if (!File.Exists(configPath))
            {
                var commonDirPath = Path.Combine(gitDirectory, "commondir");
                if (File.Exists(commonDirPath))
                {
                    var commonDir = File.ReadLines(commonDirPath).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(commonDir))
                        configPath = Path.Combine(Path.GetFullPath(Path.Combine(gitDirectory, commonDir)), "config");
                }
            }
            if (!File.Exists(configPath)) return (null, null);

            foreach (var line in File.ReadLines(configPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("url", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = trimmed.IndexOf('=');
                if (separator < 0) continue;
                var remote = trimmed[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(remote)) return ParseRemote($"origin\t{remote}\t(fetch)");
            }
        }
        catch
        {
        }
        return (null, null);
    }

    private static (string? Host, string? Provider) ParseRemote(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 2 && !parts[2].Equals("(fetch)", StringComparison.OrdinalIgnoreCase)) continue;
            var remote = parts[1];
            string? host = null;
            if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                host = uri.Host;
            else
            {
                var at = remote.IndexOf('@');
                var colon = at >= 0 ? remote.IndexOf(':', at + 1) : -1;
                if (at >= 0 && colon > at + 1) host = remote[(at + 1)..colon];
            }
            if (string.IsNullOrWhiteSpace(host)) continue;
            host = host.Trim().ToLowerInvariant();
            var provider = host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "github"
                : host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase) || host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) ? "gitlab"
                : "git";
            return (host, provider);
        }
        return (null, null);
    }

    private static JsonObject? ParseChange(string line)
    {
        string? status = null;
        string? path = null;
        if (line.StartsWith("? ", StringComparison.Ordinal))
        {
            status = "??";
            path = line[2..];
        }
        else if (line.StartsWith("1 ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', 9, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 9) { status = parts[1]; path = parts[8]; }
        }
        else if (line.StartsWith("2 ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', 10, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 10) { status = parts[1]; path = parts[9].Split('\t', 2)[0]; }
        }
        else if (line.StartsWith("u ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', 11, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 11) { status = parts[1]; path = parts[10]; }
        }

        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(path)) return null;
        return new JsonObject { ["status"] = status, ["path"] = Limit(path, 260) };
    }

    private static async Task<GitCommandResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(CommandTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new GitCommandResult(-1, "", "Git-Abfrage hat das Zeitlimit überschritten.");
            }
            return new GitCommandResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new GitCommandResult(-1, "", error.Message);
        }
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private sealed record CacheEntry(JsonObject? Value, DateTimeOffset ExpiresAt);
    private sealed record GitCommandResult(int ExitCode, string Output, string Error);
}

internal sealed class CodexClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ProjectStore _projects;
    private readonly SessionMetadataStore _metadata;
    private readonly GitStatusService _git;

    public CodexClient(ProjectStore projects, SessionMetadataStore metadata, GitStatusService git)
    {
        _projects = projects;
        _metadata = metadata;
        _git = git;
    }

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
                var processDetected = id is not null && activeProcessSessions.Contains(id);
                var lockDetected = id is not null && IsSessionActive(id);
                item["active"] = processDetected || lockDetected;
                item["openState"] = processDetected && lockDetected ? "open" : processDetected || lockDetected ? "uncertain" : "closed";
                normalized.Add(item);
                items.Add(item);
            }
        foreach (var item in items)
        {
            _projects.Apply(item);
            _metadata.Apply(item);
        }
        await _git.ApplyAsync(items, cancellation);
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
        _metadata.Apply(detail);
        await _git.ApplyAsync([detail], cancellation);
        var processDetected = DetectActiveProcessSessions().Contains(threadId);
        var lockDetected = IsSessionActive(threadId);
        detail["active"] = processDetected || lockDetected;
        detail["openState"] = processDetected && lockDetected ? "open" : processDetected || lockDetected ? "uncertain" : "closed";
        return detail;
    }

    public void SetPinned(string threadId, bool pinned)
    {
        ValidateThreadId(threadId);
        var rows = CodexStateStore.Query($"select id from threads where id = '{threadId}' limit 1");
        if (rows.Count != 1) throw new KeyNotFoundException("Session wurde nicht gefunden.");
        _metadata.SavePinned(threadId, pinned);
    }

    public JsonObject SetTitle(string threadId, string? title)
    {
        ValidateThreadId(threadId);
        var rows = CodexStateStore.Query($"select name, title, first_user_message from threads where id = '{threadId}' limit 1");
        var thread = rows.FirstOrDefault() as JsonObject ?? throw new KeyNotFoundException("Session wurde nicht gefunden.");
        var originalTitle = FirstText(thread, "name", "title", "first_user_message") ?? "Unbenannte Session";
        return _metadata.SaveTitle(threadId, title, Limit(originalTitle.Trim(), 160));
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
