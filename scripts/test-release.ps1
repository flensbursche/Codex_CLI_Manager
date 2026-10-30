[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PackagePath,
    [string] $ChecksumPath,
    [Parameter(Mandatory = $true)] [string] $ExpectedVersion,
    [string] $PreviousPackagePath,
    [string] $PreviousChecksumPath,
    [switch] $InteractiveTerminal,
    [switch] $RequireSignature,
    [switch] $DisposableMachine,
    [string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "Der Release-Akzeptanztest benötigt Windows." }
if ($PreviousPackagePath -and -not $DisposableMachine) {
    throw "Ein vollständiger Upgrade-Test verändert die Benutzerinstallation. Verwende ihn nur auf einer zurücksetzbaren Test-VM mit -DisposableMachine."
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Resolve-RequiredFile([string] $Path, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label wurde nicht gefunden: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Test-PackageChecksum([string] $ZipPath, [string] $HashPath) {
    $expected = ((Get-Content -LiteralPath $HashPath -Raw) -split '\s+')[0].ToLowerInvariant()
    Assert-True ($expected -match '^[0-9a-f]{64}$') "Die SHA-256-Datei ist ungültig: $HashPath"
    $actual = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($actual -eq $expected) "SHA-256 stimmt nicht: erwartet $expected, erhalten $actual"
    return $actual
}

function Expand-VerifiedPackage([string] $ZipPath, [string] $HashPath, [string] $Destination, [bool] $SignatureRequired) {
    $hash = Test-PackageChecksum $ZipPath $HashPath
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $root $entry.FullName))
            Assert-True ($target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) "Das ZIP enthält einen unzulässigen Pfad: $($entry.FullName)"
        }
    }
    finally { $archive.Dispose() }
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination
    $executables = @(Get-ChildItem -LiteralPath $Destination -Filter CodexSessionManager.exe -File -Recurse)
    Assert-True ($executables.Count -eq 1) "Das Paket muss genau eine CodexSessionManager.exe enthalten."
    $signature = Get-AuthenticodeSignature -LiteralPath $executables[0].FullName
    if ($SignatureRequired) { Assert-True ($signature.Status -eq 'Valid') "Authenticode-Signatur ist nicht gültig: $($signature.Status)" }
    return [pscustomobject]@{
        Hash = $hash
        Root = $executables[0].Directory.FullName
        Executable = $executables[0].FullName
        SignatureStatus = [string] $signature.Status
        Signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
    }
}

function New-ManagerStartInfo([string] $Executable, [string[]] $Arguments, [string] $AcceptanceId, [string] $AcceptanceRoot) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    if ($AcceptanceId) {
        $start.Environment['CODEX_MANAGER_ACCEPTANCE_ID'] = $AcceptanceId
        $start.Environment['CODEX_MANAGER_ACCEPTANCE_ROOT'] = $AcceptanceRoot
    }
    return $start
}

function Wait-Manager([string] $EndpointPath, [string] $Version, [int] $TimeoutSeconds = 40) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if (Test-Path -LiteralPath $EndpointPath) {
                $url = (Get-Content -LiteralPath $EndpointPath -Raw).Trim().TrimEnd('/')
                $health = Invoke-RestMethod -UseBasicParsing -Uri "$url/api/health" -TimeoutSec 2
                $bootstrap = Invoke-RestMethod -UseBasicParsing -Uri "$url/api/bootstrap" -TimeoutSec 2
                if ($health.ok -and $bootstrap.data.version -eq $Version) {
                    return [pscustomobject]@{ Url = $url; Bootstrap = $bootstrap.data }
                }
            }
        }
        catch { $lastError = $_.Exception.Message }
        Start-Sleep -Milliseconds 250
    }
    throw "Manager-Version $Version wurde nicht rechtzeitig erreichbar. Letzter Fehler: $lastError"
}

function Invoke-ApiChecks($Manager, [string] $ProjectName) {
    $sessions = Invoke-RestMethod -UseBasicParsing -Uri "$($Manager.Url)/api/sessions?archived=false" -TimeoutSec 20
    Assert-True ($null -ne $sessions.data) "Session-Endpunkt lieferte keine Datenstruktur."
    $headers = @{ 'X-Codex-Manager-Token' = $Manager.Bootstrap.token }
    $body = @{ name = $ProjectName } | ConvertTo-Json
    $created = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/projects" -Headers $headers -ContentType 'application/json' -Body $body
    Assert-True ($created.data.name -eq $ProjectName) "Testprojekt wurde nicht erstellt."
    if (@($sessions.data).Count -gt 0) {
        $threadId = [string] @($sessions.data)[0].id
        $assigned = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/projects/$($created.data.id)/sessions/$threadId" -Headers $headers
        Assert-True ($assigned.data.projectId -eq $created.data.id) "Session wurde dem Testprojekt nicht zugeordnet."
        $assignedSession = Invoke-RestMethod -UseBasicParsing -Uri "$($Manager.Url)/api/session/$threadId" -TimeoutSec 20
        Assert-True ($assignedSession.data.projectId -eq $created.data.id) "Projektzuordnung ist in den Sessiondetails nicht sichtbar."
        $null = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/projects/sessions/$threadId/unassign" -Headers $headers
        $unassignedSession = Invoke-RestMethod -UseBasicParsing -Uri "$($Manager.Url)/api/session/$threadId" -TimeoutSec 20
        Assert-True ($null -eq $unassignedSession.data.PSObject.Properties['projectId']) "Session wurde nicht wieder aus dem Projekt gelöst."
    }
    $deleteName = "$ProjectName-delete"
    $deleteBody = @{ name = $deleteName } | ConvertTo-Json
    $deleteProject = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/projects" -Headers $headers -ContentType 'application/json' -Body $deleteBody
    $null = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/projects/$($deleteProject.data.id)/delete" -Headers $headers
    $projectsAfterDelete = Invoke-RestMethod -UseBasicParsing -Uri "$($Manager.Url)/api/projects" -TimeoutSec 10
    Assert-True (@($projectsAfterDelete.data | Where-Object name -eq $deleteName).Count -eq 0) "Leeres Testprojekt wurde nicht gelöscht."
    $language = Invoke-RestMethod -UseBasicParsing -Method Post -Uri "$($Manager.Url)/api/language" -Headers $headers -ContentType 'application/json' -Body '{"language":"en"}'
    Assert-True ($language.data.language -eq 'en') "Spracheinstellung wurde nicht gespeichert."
    return [pscustomobject]@{
        SessionCount = @($sessions.data).Count
        Compatibility = $Manager.Bootstrap.codexCompatibility
        ProjectName = $ProjectName
    }
}

function Stop-OwnedProcess([Diagnostics.Process] $Process, [string] $ExpectedExecutable) {
    if ($null -eq $Process) { return }
    try { $Process.Refresh() } catch { return }
    if ($Process.HasExited) { return }
    Assert-True ([string]::Equals($Process.MainModule.FileName, $ExpectedExecutable, [StringComparison]::OrdinalIgnoreCase)) "Unerwarteter Prozess soll beendet werden."
    Stop-Process -Id $Process.Id -Force
    $Process.WaitForExit(10000) | Out-Null
}

function Find-OwnedProcess([string] $ExpectedExecutable) {
    return Get-Process -Name CodexSessionManager -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and [string]::Equals($_.Path, $ExpectedExecutable, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
}

function Get-AutostartValue([string] $RegistryPath) {
    $entry = Get-ItemProperty -LiteralPath $RegistryPath -Name CodexSessionManager -ErrorAction SilentlyContinue
    if ($null -eq $entry) { return $null }
    return $entry.CodexSessionManager
}

$package = Resolve-RequiredFile $PackagePath "Release-Paket"
if (-not $ChecksumPath) { $ChecksumPath = "$package.sha256" }
$checksum = Resolve-RequiredFile $ChecksumPath "SHA-256-Datei"
if ($ReportPath) { $ReportPath = [IO.Path]::GetFullPath($ReportPath) }

$tempParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'CodexSessionManager.ReleaseAcceptance'))
$testRoot = Join-Path $tempParent ([Guid]::NewGuid().ToString('N'))
$markerParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'CodexSessionManager.IntegrationTests'))
$markerRoot = Join-Path $markerParent ([Guid]::NewGuid().ToString('N'))
$candidateRoot = Join-Path $testRoot 'candidate'
$acceptanceRoot = Join-Path $testRoot 'isolated-data'
$acceptanceId = [Guid]::NewGuid().ToString('D')
$projectName = "Acceptance-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$started = $null
$startedExecutable = $null
$installedProcess = $null
$disposableInstallRoot = Join-Path $env:LOCALAPPDATA 'CodexSessionManager'
$normalInstalledExecutable = Join-Path $disposableInstallRoot 'CodexSessionManager.exe'
$normalInstallExistedBefore = Test-Path -LiteralPath $normalInstalledExecutable -PathType Leaf
$normalInstallHashBefore = if ($normalInstallExistedBefore) { (Get-FileHash -LiteralPath $normalInstalledExecutable -Algorithm SHA256).Hash } else { $null }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$autostartBefore = Get-AutostartValue $runKey
$result = [ordered]@{
    Passed = $false
    Version = $ExpectedVersion
    StartedAt = [DateTimeOffset]::UtcNow.ToString('O')
    Machine = $env:COMPUTERNAME
    OS = [Environment]::OSVersion.VersionString
    CodexVersion = $null
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    PackageHash = $null
    SignatureStatus = $null
    Signer = $null
    InteractiveTerminal = [bool] $InteractiveTerminal
    UpgradeTested = [bool] $PreviousPackagePath
    SessionCount = $null
    Compatibility = $null
    Checks = @()
    FinishedAt = $null
}

try {
    if (Get-Command codex -ErrorAction SilentlyContinue) {
        $result.CodexVersion = ((& codex --version 2>$null) | Select-Object -First 1)
    }
    New-Item -ItemType Directory -Path $testRoot, $markerRoot | Out-Null
    $candidate = Expand-VerifiedPackage $package $checksum $candidateRoot ([bool] $RequireSignature)
    $result.PackageHash = $candidate.Hash
    $result.SignatureStatus = $candidate.SignatureStatus
    $result.Signer = $candidate.Signer
    $result.Checks += 'package-and-checksum'

    if ($InteractiveTerminal) {
        $marker = Join-Path $markerRoot 'terminal-started.txt'
        $terminalStart = New-ManagerStartInfo $candidate.Executable @() $acceptanceId $acceptanceRoot
        $terminalStart.Environment['CODEX_MANAGER_TERMINAL_SMOKE_MARKER'] = $marker
        $terminalProcess = [Diagnostics.Process]::Start($terminalStart)
        Assert-True ($terminalProcess.WaitForExit(15000)) "Terminal-Smoke-Helfer wurde nicht beendet."
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        Assert-True (Test-Path -LiteralPath $marker) "Das echte Windows-Terminal hat den Marker nicht geschrieben."
        Assert-True ((Get-Content -LiteralPath $marker -Raw) -eq 'ok') "Der Terminal-Marker ist ungültig."
        $result.Checks += 'interactive-windows-terminal'
    }

    if ($PreviousPackagePath) {
        Assert-True (-not (Get-Process -Name CodexSessionManager -ErrorAction SilentlyContinue)) "Auf der Disposable-VM läuft bereits ein Codex CLI Manager."
        Assert-True (-not (Test-Path -LiteralPath $disposableInstallRoot)) "Auf der Disposable-VM existiert bereits eine Manager-Installation."
        Assert-True (-not $autostartBefore) "Auf der Disposable-VM existiert bereits ein Manager-Autostarteintrag."
        $previousPackage = Resolve-RequiredFile $PreviousPackagePath "Vorheriges Release-Paket"
        if (-not $PreviousChecksumPath) { $PreviousChecksumPath = "$previousPackage.sha256" }
        $previousChecksum = Resolve-RequiredFile $PreviousChecksumPath "SHA-256-Datei des vorherigen Releases"
        $previous = Expand-VerifiedPackage $previousPackage $previousChecksum (Join-Path $testRoot 'previous') $false
        $bootstrapInstaller = [Diagnostics.Process]::Start((New-ManagerStartInfo $previous.Executable @('--no-browser') '' ''))
        Assert-True ($bootstrapInstaller.WaitForExit(20000)) "Die Erstinstallation des vorherigen Releases wurde nicht beendet."
        $endpoint = Join-Path $disposableInstallRoot 'endpoint.txt'
        $installedExecutable = Join-Path $disposableInstallRoot 'CodexSessionManager.exe'
        $oldVersion = (Wait-Manager $endpoint ([Diagnostics.FileVersionInfo]::GetVersionInfo($installedExecutable).ProductVersion)).Bootstrap.version
        $installedProcess = Find-OwnedProcess $installedExecutable
        Assert-True ($null -ne $installedProcess) "Installierter vorheriger Manager-Prozess wurde nicht gefunden."
        $oldManager = Wait-Manager $endpoint $oldVersion
        $api = Invoke-ApiChecks $oldManager $projectName

        $helperStart = New-ManagerStartInfo $candidate.Executable @('--apply-update', $disposableInstallRoot, [string] $installedProcess.Id) '' ''
        $helper = [Diagnostics.Process]::Start($helperStart)
        Assert-True ($helper.WaitForExit(60000)) "Update-Helfer wurde nicht rechtzeitig beendet."
        $newManager = Wait-Manager $endpoint $ExpectedVersion 60
        $projects = Invoke-RestMethod -UseBasicParsing -Uri "$($newManager.Url)/api/projects" -TimeoutSec 10
        Assert-True (@($projects.data | Where-Object name -eq $projectName).Count -eq 1) "Projekte wurden beim Upgrade nicht erhalten."
        $installedProcess = Find-OwnedProcess $installedExecutable
        Assert-True ($null -ne $installedProcess) "Aktualisierter Manager-Prozess wurde nicht gefunden."
        $result.Checks += 'first-install'
        $result.Checks += "upgrade-$oldVersion-to-$ExpectedVersion"
        $result.Checks += 'state-preserved'
        $result.Checks += 'api-and-codex-compatibility'
        $result.Checks += 'project-assign-unassign-delete'
        $result.SessionCount = $api.SessionCount
        $result.Compatibility = $api.Compatibility
    }
    else {
        New-Item -ItemType Directory -Path $acceptanceRoot | Out-Null
        Get-ChildItem -LiteralPath $candidate.Root -Force | Copy-Item -Destination $acceptanceRoot -Recurse -Force
        $endpoint = Join-Path $acceptanceRoot 'endpoint.txt'
        $installedExecutable = Join-Path $acceptanceRoot 'CodexSessionManager.exe'
        $startedExecutable = $installedExecutable
        $started = [Diagnostics.Process]::Start((New-ManagerStartInfo $installedExecutable @('--no-browser') $acceptanceId $acceptanceRoot))
        $manager = Wait-Manager $endpoint $ExpectedVersion
        $api = Invoke-ApiChecks $manager $projectName

        $helperStart = New-ManagerStartInfo $candidate.Executable @('--apply-update', $acceptanceRoot, [string] $started.Id) $acceptanceId $acceptanceRoot
        $helper = [Diagnostics.Process]::Start($helperStart)
        Assert-True ($helper.WaitForExit(60000)) "Isolierter Update-Helfer wurde nicht rechtzeitig beendet."
        $manager = Wait-Manager $endpoint $ExpectedVersion 60
        $projects = Invoke-RestMethod -UseBasicParsing -Uri "$($manager.Url)/api/projects" -TimeoutSec 10
        Assert-True (@($projects.data | Where-Object name -eq $projectName).Count -eq 1) "Projekte wurden beim isolierten Update nicht erhalten."
        $language = Invoke-RestMethod -UseBasicParsing -Uri "$($manager.Url)/api/language" -TimeoutSec 10
        Assert-True ($language.data.language -eq 'en') "Spracheinstellung wurde beim isolierten Update nicht erhalten."
        $started = Find-OwnedProcess $installedExecutable
        Assert-True ($null -ne $started) "Neu gestarteter isolierter Manager-Prozess wurde nicht gefunden."
        $result.Checks += 'isolated-install-update-and-restart'
        $result.Checks += 'api-and-codex-compatibility'
        $result.Checks += 'project-assign-unassign-delete'
        $result.Checks += 'state-preserved'
        $result.SessionCount = $api.SessionCount
        $result.Compatibility = $api.Compatibility
    }

    $autostartAfter = Get-AutostartValue $runKey
    if (-not $PreviousPackagePath) {
        Assert-True ($autostartBefore -eq $autostartAfter) "Der isolierte Test hat den Autostart verändert."
        $result.Checks += 'autostart-unchanged'
        $normalInstallExistsAfter = Test-Path -LiteralPath $normalInstalledExecutable -PathType Leaf
        $normalInstallHashAfter = if ($normalInstallExistsAfter) { (Get-FileHash -LiteralPath $normalInstalledExecutable -Algorithm SHA256).Hash } else { $null }
        Assert-True ($normalInstallExistedBefore -eq $normalInstallExistsAfter -and $normalInstallHashBefore -eq $normalInstallHashAfter) "Der isolierte Test hat die normale Installation verändert."
        $result.Checks += 'normal-installation-unchanged'
    }
    $result.Passed = $true
}
finally {
    if ($started) { Stop-OwnedProcess $started $startedExecutable }
    if ($installedProcess) { Stop-OwnedProcess $installedProcess (Join-Path $disposableInstallRoot 'CodexSessionManager.exe') }
    if ($startedExecutable) {
        $remainingIsolated = Find-OwnedProcess $startedExecutable
        if ($remainingIsolated) { Stop-OwnedProcess $remainingIsolated $startedExecutable }
    }
    if ($PreviousPackagePath -and $DisposableMachine) {
        $disposableExecutable = Join-Path $disposableInstallRoot 'CodexSessionManager.exe'
        $remainingInstalled = Find-OwnedProcess $disposableExecutable
        if ($remainingInstalled) { Stop-OwnedProcess $remainingInstalled $disposableExecutable }
        if (-not $autostartBefore) { Remove-ItemProperty -LiteralPath $runKey -Name CodexSessionManager -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $disposableInstallRoot) {
            $resolvedInstall = (Resolve-Path -LiteralPath $disposableInstallRoot).Path
            $expectedInstall = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexSessionManager'))
            Assert-True ([string]::Equals($resolvedInstall, $expectedInstall, [StringComparison]::OrdinalIgnoreCase)) "Unerwarteter Installationspfad wird nicht entfernt."
            Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
        }
    }
    foreach ($cleanup in @($testRoot, $markerRoot)) {
        if (-not (Test-Path -LiteralPath $cleanup)) { continue }
        $resolved = (Resolve-Path -LiteralPath $cleanup).Path
        $allowed = if ($cleanup -eq $testRoot) { $tempParent } else { $markerParent }
        Assert-True ($resolved.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) "Unerwarteter Testpfad wird nicht entfernt: $resolved"
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $result.FinishedAt = [DateTimeOffset]::UtcNow.ToString('O')
    if ($ReportPath) {
        $reportDirectory = Split-Path -Parent $ReportPath
        if ($reportDirectory) { New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null }
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
}

if (-not $result.Passed) { throw "Release-Akzeptanztest ist fehlgeschlagen." }
$result | ConvertTo-Json -Depth 8
