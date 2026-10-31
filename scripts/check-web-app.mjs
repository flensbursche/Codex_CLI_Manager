import { readFile } from "node:fs/promises";
import vm from "node:vm";

const script = await readFile(new URL("../web/app.js", import.meta.url), "utf8");
const html = await readFile(new URL("../web/index.html", import.meta.url), "utf8");
new vm.Script(script);
for (const asset of ["/app-icon.svg?v=22", "/app.css?v=22", "/app.js?v=22"])
  if (!html.includes(asset)) throw new Error(`Web-Asset verwendet nicht die aktuelle Cache-Version: ${asset}`);
for (const feature of ['session.internal === true', 'open-desktop', 'session-launch', 'session-details', 'session-actions', 'session-favorite', 'toggleFavorite(threadId, button)', 'project-create-form', 'project-name-clear', 'pendingProjectSessionId', 'newProjectForSession', 'assignSessionToProject(threadId, projectId)', 'removeSessionFromProject(threadId, button)', 'session-project-remove', '/unassign', 'text/codex-thread', 'search-clear', 'sessionTime(session)', 'sessionBundles(sessions)', 'shadow-sessions', 'ChatGPT Desktop', 'session-context', 'new EventSource("/api/events")', '/api/terminal-settings', '/api/language', '/api/update-status', 'window.setTimeout(initialize, 1000)', 'await loadMessages(result.language)', 'header-menu', 'sidebar-collapsed', 'data-language', 'sessionSearchCountMany', 'bindCopyButtons()', '/title', 'terminalSettingsPage()', 'aboutPage()', 'gitIndicator(session.git)', 'sessionHealthIndicator(session.health)', 'healthStateLabel(session.health)', 'compatibility-warning', 'compatibilityLimited', 'role="switch"', 'share-repository', 'menu-update', 'prefers-color-scheme: dark', 'menuDismissBound', 'latest.version !== companionInfo.version', 'window.location.reload()']) {
  if (!script.includes(feature)) throw new Error(`Web-Feature fehlt: ${feature}`);
}
for (const feature of ['session-restore', 'sessionsLoading', 'aria-label="${t("filterSource")}"', 'aria-label="${t("sessions")}"', 'terminalStarted', 'draggedSessionId', 'sessionAssigned', 'project-assignment', 'updateSessionProject(session', 'project-delete', 'deleteProject(projectId', 'project-delete-dialog', 'deleteProjectDescription', 'event.key === "Escape"', 'closeCreator(true)']) {
  if (!script.includes(feature)) throw new Error(`Web-Feedback-Feature fehlt: ${feature}`);
}
if (script.includes('window.confirm(t("deleteProjectConfirm"')) throw new Error("Projektlöschung verwendet weiterhin den nativen Browser-Dialog.");
const agent = await readFile(new URL("../agent/Program.cs", import.meta.url), "utf8");
if (!agent.includes("thread-writer-locks")) throw new Error("Aktive Codex-Threads werden nicht über Writer-Locks erkannt.");
if (!agent.includes("AttachShadowSessions(items)")) throw new Error("Agent-erzeugte Schatten-Sessions werden keinem Master zugeordnet.");
if (!agent.includes("_metadata.SavePinned(threadId, pinned)")) throw new Error("Favoriten werden nicht in app-eigenen Metadaten gespeichert.");
if (agent.includes("OpenReadWrite") || agent.includes("CodexStateStore.SetPinned")) throw new Error("Die interne Codex-Datenbank wird weiterhin für Favoriten beschrieben.");
if (!agent.includes("projects.Assign(threadId, projectId)")) throw new Error("Sessions können keinem Projekt zugeordnet werden.");
if (!agent.includes("projects.Unassign(threadId)")) throw new Error("Sessions können nicht aus Projekten entfernt werden.");
if (!agent.includes("projects.Delete(projectId)")) throw new Error("Leere Projekte können nicht gelöscht werden.");
if (!agent.includes("IsUnchangedAutoImport") || !agent.includes("JsonNode.DeepEquals")) throw new Error("Der fehlerhafte Projekt-Auto-Import wird nicht sicher bereinigt.");
if (!agent.includes("GitStatusService") || !agent.includes('"--porcelain=v2"') || !agent.includes("CacheDuration") || !agent.includes('["remote", "-v"]')) throw new Error("Der gecachte Git-Status samt Remote-Anbieter fehlt.");
if (!agent.includes("FindRepositoryRoot(directory)") || !agent.includes('["unavailable"] = true')) throw new Error("Repositories mit blockiertem Git-Status werden nicht als Warnung erkannt.");
if (!agent.includes("ReadRemoteFromConfig(repositoryRoot)") || !agent.includes('["provider"] = provider')) throw new Error("Der Remote-Anbieter blockierter Repositories wird nicht aus der Git-Konfiguration erkannt.");
if (!agent.includes("ApplySessionHealth(item, processSnapshot)") || !agent.includes('["processScanAvailable"] = processes.Available') || !agent.includes("SessionHealthEvaluator.Evaluate") || !agent.includes('signals.ProcessCount > 1 ? "duplicate"')) throw new Error("Der mehrsignalige Session Health Check fehlt.");
if (!agent.includes("CodexStateStore.QueryThreads(archived)") || !agent.includes("BuildThreadListQuery") || !agent.includes("codexCompatibility = CodexStateStore.GetCompatibility()")) throw new Error("Der kontrollierte Codex-Kompatibilitätsmodus fehlt.");
if (!agent.includes("AcceptanceEnvironment.InstanceMutexName") || !agent.includes("AcceptanceEnvironment.UpdateSignalName") || !agent.includes("AcceptanceEnvironment.DataRoot") || !agent.includes("AcceptanceEnvironment.ApplyTo(restart)")) throw new Error("Der isolierte Release-Acceptance-Modus fehlt.");
if (!agent.includes('LanguageSettingsStore')) throw new Error("Die gemeinsame Sprachspeicherung fehlt.");
if (!agent.includes("TrayMenuRenderer") || !agent.includes("TrayMenuPalette.Current()") || !agent.includes("TrayMenuGlyphs.Terminal") || !agent.includes("ApplyRoundedRegion(menu, 12)")) throw new Error("Das gestaltete, abgerundete Tray-Menü mit eigenen Symbolen fehlt.");
if (!agent.includes('app.MapGet("/api/language"')) throw new Error("Der Sprach-Endpunkt fehlt.");
for (const language of ["de", "en", "da"]) {
  const locale = JSON.parse(await readFile(new URL(`../web/locales/${language}.json`, import.meta.url), "utf8"));
  for (const key of ["sessions", "sessionSearchCountMany", "terminalSettings", "versionHistory", "newProjectForSession", "gitStatus", "gitClean", "gitUnavailable", "gitLocalNoRemote", "healthHealthy", "healthUncertain", "healthDuplicate", "healthSignals", "compatibilityLimited", "removeFromProject", "sessionOpen", "copyPath", "editTitle", "roadmap", "shareRepository", "aboutAndHistory", "updateBadge", "restoreAction", "sessionsLoading", "filterSource", "terminalStarted", "archivingAction", "sessionAssigned", "projectAssignment", "deleteProject", "deleteProjectTitle", "deleteProjectDescription", "deleteProjectAction", "tray.open", "tray.updates", "tray.aboutHistory"])
    if (!locale[key]) throw new Error(`Übersetzung fehlt: ${language}.${key}`);
}
if (!agent.includes("TerminalHost.OpenPowerShell(cwd, command)")) throw new Error("Session-Resume verwendet nicht Windows Terminal.");
if (!agent.includes('case "unarchive"') || !agent.includes('RunCodexCommandAsync(["unarchive", threadId]')) throw new Error("Archivierte Sessions können nicht sicher wiederhergestellt werden.");
if (!agent.includes("if (string.IsNullOrWhiteSpace(command)) return startInfo;")) throw new Error("Der reine Tray-Terminalstart überschreibt weiterhin den konfigurierten Windows-Terminal-Befehl.");
if (!agent.includes('"Microsoft.WindowsTerminal_8wekyb3d8bbwe"')) throw new Error("Windows Terminal wird nicht über seinen App-Alias gestartet.");
if (!agent.includes('"{574e775e-4f2a-5b96-ac1e-a2962a402336}"')) throw new Error("Das normale PowerShell-Profil von Windows Terminal wird nicht explizit verwendet.");
if (!agent.includes('startInfo.ArgumentList.Add("new");') || agent.includes('startInfo.ArgumentList.Add("0");')) throw new Error("Session-Resume öffnet nicht immer ein neues Windows-Terminal-Fenster.");
if (agent.includes('startInfo.ArgumentList.Add("--");') || !agent.includes('startInfo.ArgumentList.Add("powershell.exe");') || agent.includes('startInfo.ArgumentList.Add("--appendCommandLine");')) throw new Error("Windows Terminal erhält den positionalen, leerzeichenfreien System-Launcher nicht in der dokumentierten Syntax.");
if (!agent.includes('BuildTerminalBridgeCommand(shell.Path, encodedInitialization)') || !agent.includes("& '{escapedExecutable}' -NoLogo -NoExit -EncodedCommand")) throw new Error("Der exakte PowerShell-Paketpfad wird nicht sicher über den codierten System-Launcher gestartet.");
if (!agent.includes("ResolvePackagedPowerShell7()") || !agent.includes('GetValue("PackageRootFolder")')) throw new Error("Die echte PowerShell-Paketdatei wird nicht vor dem WindowsApps-Alias aufgelöst.");
if (!agent.includes('"Microsoft.PowerShell_8wekyb3d8bbwe"')) throw new Error("Die installierte PowerShell-7-App wird nicht bevorzugt.");
if (!agent.includes('Convert.ToBase64String(Encoding.Unicode.GetBytes(initialization))')) throw new Error("Der Resume-Befehl wird für Windows Terminal nicht sicher kodiert.");
if (!agent.includes('$env:NO_COLOR=$null; $env:TERM=$null; $env:COLORTERM=$null')) throw new Error("Geerbte Automations-Farbvariablen werden vor dem Terminal-Start nicht entfernt.");
if (!agent.includes("npx @openai/codex resume")) throw new Error("Session-Resume entspricht nicht dem normalen lokalen Codex-Start.");
if (agent.includes("codex --no-daemon resume")) throw new Error("Session-Resume verwendet weiterhin den abweichenden No-Daemon-Start.");
if (agent.includes("& '{escapedExecutable}' -NoLogo -NoProfile")) throw new Error("Der finale PowerShell-Prozess unterdrückt weiterhin das Benutzerprofil.");
if (!agent.includes("ExtractAssociatedIcon(executable)")) throw new Error("Das Tray verwendet nicht das eigene App-Icon.");
if (!agent.includes('app.MapGet("/api/terminal-settings"')) throw new Error("Die Terminal-Erkennung und -Auswahl fehlt.");
if (!agent.includes('"wwwroot", "locales"')) throw new Error("Tray und Web verwenden nicht dieselben Locale-Dateien.");
if (!agent.includes('CheckForUpdateAsync')) throw new Error("Die GitHub-Release-Prüfung fehlt.");
if (!agent.includes('releases/latest') || agent.includes('api.github.com/repos/')) throw new Error("Die Update-Prüfung ist weiterhin vom anonymen GitHub-API-Limit abhängig.");
if (!agent.includes('releases/latest/download/') || !agent.includes('ReadReleaseTag')) throw new Error("Der API-freie Fallback für die Update-Prüfung fehlt.");
if (!agent.includes('SHA256.HashDataAsync')) throw new Error("Update-Pakete werden nicht per SHA-256 geprüft.");
if (!agent.includes('TryApplyStagedUpdate(args)')) throw new Error("Der separate Update-Helfer fehlt.");
if (!agent.includes('lifetime.ApplicationStopping') || !agent.includes('WaitForManagerExit(processId)') || !agent.includes('process.WaitForExit(timeoutMilliseconds)') || !agent.includes('process.Kill();') || !agent.includes('UpdateShutdownListener.RequestStop()')) throw new Error("Der Update-Neustart beendet Live-Verbindungen oder den alten Prozess nicht sicher.");
if (!agent.includes('autostartWasEnabled') || !agent.includes('else AutoStartManager.Disable()')) throw new Error("Updates bewahren die Autostart-Einstellung nicht.");
if (!agent.includes('UpdateProgressWindow') || !agent.includes('UpdateStage.Verifying') || !agent.includes('language.Text("restarting")') || !agent.includes('PictureBoxSizeMode.Zoom')) throw new Error("Der sichtbare Update-Fortschritt samt App-Icon fehlt.");
if (!agent.includes('EnsureMinimumStageAsync') || !agent.includes('EnsureMinimumStage(stageDuration, 1100)')) throw new Error("Kurze Update-Phasen bleiben nicht lange genug sichtbar.");
if (agent.includes("AllocConsole")) throw new Error("Die rohe Win32-Konsole ist weiterhin aktiv.");
if (!agent.includes('app.MapGet("/api/events"')) throw new Error("Der SSE-Endpunkt für Live-Updates fehlt.");
if (!agent.includes('AppEndpoint.SelectAndSave()')) throw new Error("Die automatische Ausweichwahl bei belegtem Port fehlt.");
if (!agent.includes('SessionMetadataStore')) throw new Error("Dauerhafte Session-Titel fehlen.");
if (agent.includes('resume-force')) throw new Error("Der unsichere manuelle Doppelstart ist weiterhin aktiv.");
const styles = await readFile(new URL("../web/app.css", import.meta.url), "utf8");
const regexEscape = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const hasDeclaration = (source, property, value) => new RegExp(`${regexEscape(property)}\\s*:\\s*${regexEscape(value)}(?:\\s*;|\\s*[;}]|\\s*$)`, "i").test(source);
const hasRule = (source, selector) => {
  const selectorPattern = regexEscape(selector).replace(/\\ /g, "\\s+");
  return new RegExp(`${selectorPattern}\\s*\\{`, "i").test(source);
};
const hasRuleDeclaration = (source, selector, property, value) => {
  const selectorPattern = regexEscape(selector).replace(/\\ /g, "\\s+");
  const matches = source.matchAll(new RegExp(`${selectorPattern}\\s*\\{([^}]*)\\}`, "gis"));
  return [...matches].some((match) => hasDeclaration(match[1], property, value));
};
const assertResponsiveStyles = (source) => {
  if (!hasDeclaration(source, "min-width", "0")) throw new Error("Responsive Web-Feature fehlt: min-width: 0");
  if (!hasDeclaration(source, "font-size", "0.875rem")) throw new Error("Responsive Web-Feature fehlt: font-size: 0.875rem");
  if (!hasRuleDeclaration(source, ".projects button", "font-size", "0.75rem")) throw new Error("Responsive Web-Feature fehlt: kompakte Projekttypografie");
  if (!/@media\s*\(\s*max-width\s*:\s*760px\s*\)/i.test(source)) throw new Error("Responsive Web-Feature fehlt: Mobile Media Query");
  for (const selector of [".terminal-dialog", ".confirm-dialog", ".header-menu-panel", ".language-options", ".list-layout.sidebar-collapsed", ".path-copy", ".settings-form-page .terminal-detected", ".session-git", ".session-project-remove", ".git-tooltip", ".history-content", ".roadmap-grid"])
    if (!hasRule(source, selector)) throw new Error(`Responsive Web-Feature fehlt: ${selector}`);
  if (!hasRuleDeclaration(source, ".project-create", "margin-top", "8px")) throw new Error("Responsive Web-Feature fehlt: Projektabstand");
  if (!hasRuleDeclaration(source, ".project-create-form", "margin-top", "8px")) throw new Error("Responsive Web-Feature fehlt: Abstand des Projektformulars");
};
assertResponsiveStyles(styles);
assertResponsiveStyles(styles.replace(/\s+/g, " "));
const history = JSON.parse(await readFile(new URL("../web/version-history.json", import.meta.url), "utf8"));
if (!Array.isArray(history.releases) || !history.releases.some((release) => release.released && release.tag)) throw new Error("Die aus Git erzeugte Versionshistorie fehlt.");
const roadmap = JSON.parse(await readFile(new URL("../web/roadmap.json", import.meta.url), "utf8"));
if (roadmap.version !== "1.0" || !Array.isArray(roadmap.items) || roadmap.items.length < 5) throw new Error("Die erweiterbare 1.0-Roadmap fehlt.");
for (const item of ["release-hardening", "code-signing"])
  if (!roadmap.items.some((entry) => entry.id === item)) throw new Error(`1.0-Roadmap-Eintrag fehlt: ${item}`);
const acceptance = await readFile(new URL("./test-release.ps1", import.meta.url), "utf8");
for (const feature of ["Test-PackageChecksum", "Get-AuthenticodeSignature", "CODEX_MANAGER_TERMINAL_SMOKE_MARKER", "--apply-update", "-DisposableMachine"])
  if (!acceptance.includes(feature)) throw new Error(`Release-Akzeptanztest fehlt: ${feature}`);
const releaseWorkflow = await readFile(new URL("../.github/workflows/windows-build.yml", import.meta.url), "utf8");
if (!releaseWorkflow.includes("azure/artifact-signing-action@v2") || !releaseWorkflow.includes("REQUIRE_SIGNED_RELEASES") || !releaseWorkflow.includes("Get-AuthenticodeSignature")) throw new Error("Die optionale, prüfbare Artifact-Signing-Pipeline fehlt.");
const acceptanceWorkflow = await readFile(new URL("../.github/workflows/windows-acceptance.yml", import.meta.url), "utf8");
if (!acceptanceWorkflow.includes("codex-manager-ui") || !acceptanceWorkflow.includes("test-release.ps1")) throw new Error("Der interaktive Windows-Acceptance-Workflow fehlt.");
const appIcon = await readFile(new URL("../web/app-icon.svg", import.meta.url), "utf8");
if (!appIcon.includes('fill="#ff6537"') || !appIcon.includes('stroke="#fff"')) throw new Error("Das gemeinsame orange-weiße App-Icon fehlt.");
if (!script.includes('class="mark"><img src="/app-icon.svg?v=22"')) throw new Error("Das App-Icon fehlt im Seiten-Header.");
const shareMenuIndex = script.indexOf('id="share-repository"');
const aboutMenuIndex = script.indexOf('href="/about"', shareMenuIndex);
const updateMenuIndex = script.indexOf('id="menu-update"', aboutMenuIndex);
if (shareMenuIndex < 0 || aboutMenuIndex < shareMenuIndex || updateMenuIndex < aboutMenuIndex || script.includes('t("aboutAndHistory",')) throw new Error("Über & Historie steht nicht ohne redundante Version direkt über dem Versionsstatus.");
if (!script.includes('id="header-update"') || !script.includes('headerUpdate.hidden = result.status !== "available"')) throw new Error("Der sichtbare Update-Hinweis neben Live fehlt.");
if (!script.includes("git: brandIcon(") || !styles.includes("color: #f05032")) throw new Error("Das echte Git-Markenlogo fehlt.");
if (!styles.includes("@keyframes session-active-border-pulse") || !styles.includes(".session-row.health-healthy") || !styles.includes("prefers-reduced-motion: reduce")) throw new Error("Das dezente, barrierearme Pulsieren aktiver Sessions fehlt.");
if (!script.includes("github: brandIcon(") || !script.includes("gitlab: brandIcon(") || !script.includes("providerIcon")) throw new Error("Die Anbieterlogos für GitHub- und GitLab-Remotes fehlen.");
if (!script.includes('mark class="search-match"') || !styles.includes("mark.search-match")) throw new Error("Die Hervorhebung sichtbarer Suchtreffer fehlt.");
if (!script.includes('navigator.clipboard.writeText(url)') || script.includes('navigator.share')) throw new Error("Copy Link kopiert den Repository-Link nicht direkt.");
const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const project = await readFile(new URL("../agent/CodexSessionManager.Agent.csproj", import.meta.url), "utf8");
const projectVersion = project.match(/<Version>([^<]+)<\/Version>/)?.[1];
if (!projectVersion || projectVersion !== packageJson.version || !agent.includes(`public const string Version = "${projectVersion}"`)) throw new Error("Die Release-Versionen sind nicht synchron.");
if (script.includes('class="locations"')) throw new Error("Die Session-Liste enthält weiterhin ausführliche Pfadangaben.");
if (script.includes('class="session-pill')) throw new Error("Die Session-Liste enthält weiterhin Status-Pills.");
if (!script.includes('class="metadata-disclosure"')) throw new Error("Technische Metadaten sind nicht einklappbar.");
console.log("Web-App und Companion-Features sind syntaktisch valide.");
