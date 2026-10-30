using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Windows Terminal arguments", TestWindowsTerminalArgumentsAsync),
    ("Windows Terminal profile-only arguments", TestWindowsTerminalProfileLaunchAsync),
    ("Windows Terminal process launch", TestWindowsTerminalLaunchAsync),
    ("PowerShell package resolution", TestPowerShellPackageResolutionAsync),
    ("PowerShell process launch", TestPowerShellLaunchAsync),
    ("Session mutex and file lock", TestSessionLocksAsync),
    ("Session health state matrix", TestSessionHealthAsync),
    ("Update checksum verification", TestUpdateChecksumAsync),
    ("Update payload copy and path guard", TestUpdatePayloadAsync),
    ("Update process shutdown", TestUpdateProcessShutdownAsync),
    ("Update URL and release tag validation", TestUpdateUrlsAsync),
    ("Codex schema compatibility query", TestCodexSchemaCompatibilityAsync),
    ("Codex state read-only compatibility", TestCodexStateCompatibilityAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception error)
    {
        failures.Add($"{test.Name}: {error.Message}");
        Console.Error.WriteLine($"FAIL  {test.Name}\n      {error}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} integration test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} integration tests passed.");
return 0;

static Task TestWindowsTerminalArgumentsAsync()
{
    var shell = new ShellResolution("powershell7", "PowerShell 7", @"C:\Program Files\PowerShell\7\pwsh.exe", "7", "{test-profile}");
    var startInfo = TerminalHost.CreateStartInfo(true, @"C:\terminal\wt.exe", shell, @"C:\DEV\Project With Spaces", "Write-Output 'resume-test'");
    var arguments = startInfo.ArgumentList.ToArray();
    var expected = new[]
    {
        "-w", "new", "new-tab", "--profile", "{test-profile}", "--startingDirectory", @"C:\DEV\Project With Spaces",
        "--", PowerShellHost.ResolveTerminalExecutablePath(shell.Path), "-NoLogo", "-NoExit", "-EncodedCommand"
    };
    Assert(arguments.Length == expected.Length + 1, "Unexpected Windows Terminal argument count.");
    Assert(arguments.Take(expected.Length).SequenceEqual(expected), $"Windows Terminal arguments differ: {string.Join(" | ", arguments)}");
    var initialization = Encoding.Unicode.GetString(Convert.FromBase64String(arguments[^1]));
    Assert(initialization.Contains("$env:NO_COLOR=$null", StringComparison.Ordinal), "Color environment cleanup is missing.");
    Assert(initialization.EndsWith("Write-Output 'resume-test'", StringComparison.Ordinal), "Resume command is missing from encoded initialization.");
    Assert(!startInfo.UseShellExecute, "Terminal launch must use an exact native argument list.");
    return Task.CompletedTask;
}

static Task TestWindowsTerminalProfileLaunchAsync()
{
    var shell = new ShellResolution("powershell7", "PowerShell 7", @"C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.9.0_x64__8wekyb3d8bbwe\pwsh.exe", "7", "{test-profile}");
    var startInfo = TerminalHost.CreateStartInfo(true, @"C:\terminal\wt.exe", shell, @"C:\Users\Test User", command: null);
    var expected = new[]
    {
        "-w", "new", "new-tab", "--profile", "{test-profile}", "--startingDirectory", @"C:\Users\Test User"
    };
    Assert(startInfo.ArgumentList.SequenceEqual(expected), $"Profile-only terminal arguments differ: {string.Join(" | ", startInfo.ArgumentList)}");
    Assert(!startInfo.ArgumentList.Contains("--"), "A plain tray launch must let Windows Terminal start its configured profile command.");
    Assert(!startInfo.ArgumentList.Contains("-EncodedCommand"), "A plain tray launch must not override the configured PowerShell profile command.");
    return Task.CompletedTask;
}

static async Task TestWindowsTerminalLaunchAsync()
{
    if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("      SKIP: GitHub's hosted runner has no interactive Windows desktop.");
        return;
    }
    if (!File.Exists(TerminalHost.ExecutablePath))
    {
        Console.WriteLine("      SKIP: Windows Terminal is not installed on this runner.");
        return;
    }

    var root = CreateTestRoot();
    try
    {
        var marker = Path.Combine(root, "windows-terminal-started.txt");
        var managerExecutable = Path.Combine(AppContext.BaseDirectory, "CodexSessionManager.exe");
        Assert(File.Exists(managerExecutable), "Built CodexSessionManager.exe was not copied beside the integration tests.");
        var startInfo = new ProcessStartInfo(managerExecutable) { UseShellExecute = false };
        startInfo.Environment["CODEX_MANAGER_TERMINAL_SMOKE_MARKER"] = marker;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("CodexSessionManager smoke process was not started.");
        using (var managerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            await process.WaitForExitAsync(managerTimeout.Token);
        Assert(process.ExitCode == 0, $"CodexSessionManager smoke process exited with {process.ExitCode}.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(marker))
        {
            await Task.Delay(100, timeout.Token);
        }
        Assert(File.ReadAllText(marker) == "ok", "Windows Terminal did not execute the PowerShell initialization.");
    }
    finally
    {
        DeleteTestRoot(root);
    }
}

static Task TestPowerShellPackageResolutionAsync()
{
    var packagedExecutable = PowerShellHost.ResolvePackagedPowerShell7();
    if (packagedExecutable is null)
    {
        Console.WriteLine("      SKIP: Packaged PowerShell 7 is not installed on this runner.");
        return Task.CompletedTask;
    }

    Assert(File.Exists(packagedExecutable), "Resolved PowerShell package executable does not exist.");
    Assert(string.Equals(PowerShellHost.PowerShell7Path, packagedExecutable, StringComparison.OrdinalIgnoreCase),
        "PowerShell resolution did not prefer the real package executable over the WindowsApps alias.");
    Assert(!packagedExecutable.Contains(@"Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase),
        "PowerShell resolution still returned the WindowsApps execution alias.");
    var terminalExecutable = PowerShellHost.ResolveTerminalExecutablePath(packagedExecutable);
    Assert(File.Exists(terminalExecutable), "Windows Terminal PowerShell executable does not exist.");
    Assert(!terminalExecutable.Contains(' '), "Windows Terminal PowerShell executable still contains spaces and can be misquoted.");
    return Task.CompletedTask;
}

static async Task TestPowerShellLaunchAsync()
{
    var root = CreateTestRoot();
    try
    {
        var marker = Path.Combine(root, "powershell-started.txt");
        var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
        var shell = PowerShellHost.Resolve("windows-powershell");
        var startInfo = TerminalHost.CreateStartInfo(false, shell.Path, shell, root, $"[IO.File]::WriteAllText('{escapedMarker}', 'ok'); exit");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell process was not started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(outputTask, errorTask);
        Assert(process.ExitCode == 0, $"PowerShell exited with {process.ExitCode}.");
        Assert(File.Exists(marker) && File.ReadAllText(marker) == "ok", "Encoded PowerShell initialization was not executed.");
    }
    finally
    {
        DeleteTestRoot(root);
    }
}

static Task TestSessionLocksAsync()
{
    var threadId = Guid.NewGuid().ToString();
    var resumeCommand = CodexClient.BuildResumeCommand(threadId);
    Assert(resumeCommand.Contains(CodexClient.SessionMutexName(threadId), StringComparison.Ordinal), "Resume command does not use the session mutex.");
    Assert(resumeCommand.Contains($"npx @openai/codex resume '{threadId}'", StringComparison.Ordinal), "Resume command does not target the requested Codex session.");
    Assert(resumeCommand.Contains("finally", StringComparison.Ordinal) && resumeCommand.Contains("Dispose()", StringComparison.Ordinal), "Resume command does not release its mutex safely.");
    Assert(!CodexClient.HasSessionMutex(threadId), "Fresh test mutex unexpectedly exists.");
    using (var mutex = new Mutex(true, CodexClient.SessionMutexName(threadId), out var created))
    {
        Assert(created, "Test mutex could not be created.");
        Assert(CodexClient.HasSessionMutex(threadId), "Active manager mutex was not detected.");
    }
    Assert(!CodexClient.HasSessionMutex(threadId), "Disposed manager mutex is still reported as active.");

    var root = CreateTestRoot();
    try
    {
        var lockPath = Path.Combine(root, "session.lock");
        File.WriteAllText(lockPath, "");
        Assert(!CodexClient.IsFileLocked(lockPath), "Unlocked file is reported as locked.");
        using (File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert(CodexClient.IsFileLocked(lockPath), "Exclusive file lock was not detected.");
        Assert(!CodexClient.IsFileLocked(lockPath), "Released file lock is still reported as active.");
    }
    finally
    {
        DeleteTestRoot(root);
    }
    return Task.CompletedTask;
}

static Task TestSessionHealthAsync()
{
    AssertHealth(new(true, 0, false, false, false), "closed", false, "closed");
    AssertHealth(new(true, 0, false, false, true), "starting", true, "uncertain");
    AssertHealth(new(true, 1, true, false, false), "healthy", true, "open");
    AssertHealth(new(true, 0, true, false, false), "healthy", true, "open");
    AssertHealth(new(true, 0, false, true, false), "healthy", true, "open");
    AssertHealth(new(true, 1, false, false, false), "uncertain", true, "uncertain");
    AssertHealth(new(true, 2, true, true, false), "duplicate", true, "open");
    return Task.CompletedTask;
}

static async Task TestUpdateChecksumAsync()
{
    var payload = Encoding.UTF8.GetBytes("verified update payload");
    var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    await UpdateService.VerifyPackageChecksumAsync(new MemoryStream(payload), $"{hash}  setup.zip", CancellationToken.None);
    await AssertThrowsAsync<InvalidOperationException>(() => UpdateService.VerifyPackageChecksumAsync(new MemoryStream(payload), new string('0', 64), CancellationToken.None));
    await AssertThrowsAsync<InvalidOperationException>(() => UpdateService.VerifyPackageChecksumAsync(new MemoryStream(payload), "not-a-checksum", CancellationToken.None));
}

static Task TestUpdatePayloadAsync()
{
    var root = CreateTestRoot();
    try
    {
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "app.exe"), "new");
        File.WriteAllText(Path.Combine(source, "nested", "asset.txt"), "asset");
        File.WriteAllText(Path.Combine(destination, "app.exe"), "old");
        UpdateService.CopyPayload(source, destination);
        Assert(File.ReadAllText(Path.Combine(destination, "app.exe")) == "new", "Existing update target was not replaced.");
        Assert(File.ReadAllText(Path.Combine(destination, "nested", "asset.txt")) == "asset", "Nested update file was not copied.");

        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        AssertThrows<InvalidOperationException>(() => UpdateService.ValidatedDestination(destinationRoot, @"..\escape.txt"));
    }
    finally
    {
        DeleteTestRoot(root);
    }
    return Task.CompletedTask;
}

static Task TestUpdateProcessShutdownAsync()
{
    var shell = PowerShellHost.Resolve("windows-powershell");
    var startInfo = new ProcessStartInfo(shell.Path)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("-NoLogo");
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-Command");
    startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Update shutdown test process was not started.");
    UpdateService.WaitForManagerExit(process.Id, timeoutMilliseconds: 300);
    process.Refresh();
    Assert(process.HasExited, "Update shutdown did not terminate the previous manager process.");
    return Task.CompletedTask;
}

static Task TestUpdateUrlsAsync()
{
    UpdateService.ValidateDownloadUrl("https://github.com/flensbursche/Codex_CLI_Manager/releases/download/v1.0.0/setup.zip");
    AssertThrows<InvalidOperationException>(() => UpdateService.ValidateDownloadUrl("https://example.com/setup.zip"));
    Assert(UpdateService.ReadReleaseTag(new Uri("https://github.com/flensbursche/Codex_CLI_Manager/releases/tag/v1.2.3")) == "v1.2.3", "Release tag redirect was not parsed.");
    Assert(UpdateService.ReadReleaseTag(new Uri("https://github.com/attacker/repository/releases/tag/v9.9.9")) is null, "Foreign repository redirect was accepted.");
    return Task.CompletedTask;
}

static Task TestCodexSchemaCompatibilityAsync()
{
    var reducedColumns = new HashSet<string>(new[] { "id", "cwd", "updated_at_ms" }, StringComparer.OrdinalIgnoreCase);
    var activeQuery = CodexStateStore.BuildThreadListQuery(reducedColumns, archived: false);
    Assert(activeQuery.Contains("NULL AS title", StringComparison.Ordinal), "Missing optional title column has no read-only fallback.");
    Assert(activeQuery.Contains("order by updated_at_ms desc", StringComparison.OrdinalIgnoreCase), "Available timestamp is not used for ordering.");
    Assert(!activeQuery.Contains("where archived", StringComparison.OrdinalIgnoreCase), "Missing archive column is still queried.");

    var archivedQuery = CodexStateStore.BuildThreadListQuery(reducedColumns, archived: true);
    Assert(archivedQuery.Contains("where 1 = 0", StringComparison.OrdinalIgnoreCase), "Archived view is not safely empty when the archive column is missing.");
    AssertThrows<InvalidOperationException>(() => CodexStateStore.BuildThreadListQuery(new HashSet<string>(StringComparer.OrdinalIgnoreCase), archived: false));
    return Task.CompletedTask;
}

static Task TestCodexStateCompatibilityAsync()
{
    var compatibility = CodexStateStore.GetCompatibility();
    var mode = compatibility["mode"]?.GetValue<string>();
    Assert(mode is "full" or "limited" or "unavailable", $"Unexpected Codex compatibility mode: {mode}");
    var canRead = compatibility["canReadSessions"]?.GetValue<bool>() == true;
    var sessions = CodexStateStore.QueryThreads(archived: false);
    if (!canRead) Assert(sessions.Count == 0, "Unreadable Codex schema unexpectedly returned sessions.");
    return Task.CompletedTask;
}

static void AssertHealth(SessionHealthSignals signals, string state, bool active, string openState)
{
    var result = SessionHealthEvaluator.Evaluate(signals);
    Assert(result.State == state && result.Active == active && result.OpenState == openState,
        $"Health result mismatch. Expected {state}/{active}/{openState}, got {result.State}/{result.Active}/{result.OpenState}.");
}

static string CreateTestRoot()
{
    var root = Path.Combine(Path.GetTempPath(), "CodexSessionManager.IntegrationTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static void DeleteTestRoot(string root)
{
    var fullRoot = Path.GetFullPath(root);
    var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexSessionManager.IntegrationTests")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!fullRoot.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Refusing to delete unexpected test path: {fullRoot}");
    if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}
