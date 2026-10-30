import { readFile } from "node:fs/promises";
import vm from "node:vm";

const script = await readFile(new URL("../web/app.js", import.meta.url), "utf8");
new vm.Script(script);
for (const feature of ['session.internal === true', 'open-desktop', 'session-launch', 'session-details', 'session-actions', 'session-favorite', 'toggleFavorite(threadId, button)', 'project-create-form', 'project-name-clear', 'assignSessionToProject(threadId, projectId)', 'removeSessionFromProject(threadId, button)', 'session-project-remove', '/unassign', 'text/codex-thread', 'search-clear', 'sessionTime(session)', 'sessionBundles(sessions)', 'shadow-sessions', 'ChatGPT Desktop', 'session-context', 'new EventSource("/api/events")', '/api/terminal-settings', '/api/language', '/api/update-status', 'window.setTimeout(initialize, 1000)', 'await loadMessages(result.language)', 'header-menu', 'sidebar-collapsed', 'data-language', 'sessionSearchCountMany', 'bindCopyButtons()', '/title', 'terminalSettingsPage()', 'aboutPage()', 'gitIndicator(session.git)', 'role="switch"', 'share-repository', 'menu-update', 'prefers-color-scheme: dark', 'menuDismissBound']) {
  if (!script.includes(feature)) throw new Error(`Web-Feature fehlt: ${feature}`);
}
const agent = await readFile(new URL("../agent/Program.cs", import.meta.url), "utf8");
if (!agent.includes("thread-writer-locks")) throw new Error("Aktive Codex-Threads werden nicht über Writer-Locks erkannt.");
if (!agent.includes("AttachShadowSessions(items)")) throw new Error("Agent-erzeugte Schatten-Sessions werden keinem Master zugeordnet.");
if (!agent.includes("_metadata.SavePinned(threadId, pinned)")) throw new Error("Favoriten werden nicht in app-eigenen Metadaten gespeichert.");
if (agent.includes("OpenReadWrite") || agent.includes("CodexStateStore.SetPinned")) throw new Error("Die interne Codex-Datenbank wird weiterhin für Favoriten beschrieben.");
if (!agent.includes("projects.Assign(threadId, projectId)")) throw new Error("Sessions können keinem Projekt zugeordnet werden.");
if (!agent.includes("projects.Unassign(threadId)")) throw new Error("Sessions können nicht aus Projekten entfernt werden.");
if (!agent.includes("IsUnchangedAutoImport") || !agent.includes("JsonNode.DeepEquals")) throw new Error("Der fehlerhafte Projekt-Auto-Import wird nicht sicher bereinigt.");
if (!agent.includes("GitStatusService") || !agent.includes('"--porcelain=v2"') || !agent.includes("CacheDuration") || !agent.includes('["remote", "-v"]')) throw new Error("Der gecachte Git-Status samt Remote-Anbieter fehlt.");
if (!agent.includes('LanguageSettingsStore')) throw new Error("Die gemeinsame Sprachspeicherung fehlt.");
if (!agent.includes('app.MapGet("/api/language"')) throw new Error("Der Sprach-Endpunkt fehlt.");
for (const language of ["de", "en", "da"]) {
  const locale = JSON.parse(await readFile(new URL(`../web/locales/${language}.json`, import.meta.url), "utf8"));
  for (const key of ["sessions", "sessionSearchCountMany", "terminalSettings", "versionHistory", "gitStatus", "gitClean", "removeFromProject", "sessionOpen", "copyPath", "editTitle", "roadmap", "shareRepository", "aboutAndHistory", "tray.open", "tray.updates", "tray.aboutHistory"])
    if (!locale[key]) throw new Error(`Übersetzung fehlt: ${language}.${key}`);
}
if (!agent.includes("TerminalHost.OpenPowerShell(cwd, command)")) throw new Error("Session-Resume verwendet nicht Windows Terminal.");
if (!agent.includes('"Microsoft.WindowsTerminal_8wekyb3d8bbwe"')) throw new Error("Windows Terminal wird nicht über seinen App-Alias gestartet.");
if (!agent.includes('"{574e775e-4f2a-5b96-ac1e-a2962a402336}"')) throw new Error("Das normale PowerShell-Profil von Windows Terminal wird nicht explizit verwendet.");
if (!agent.includes('startInfo.ArgumentList.Add("new");') || agent.includes('startInfo.ArgumentList.Add("0");')) throw new Error("Session-Resume öffnet nicht immer ein neues Windows-Terminal-Fenster.");
if (!agent.includes('"Microsoft.PowerShell_8wekyb3d8bbwe"')) throw new Error("Die installierte PowerShell-7-App wird nicht bevorzugt.");
if (!agent.includes('Convert.ToBase64String(Encoding.Unicode.GetBytes(initialization))')) throw new Error("Der Resume-Befehl wird für Windows Terminal nicht sicher kodiert.");
if (!agent.includes('$env:NO_COLOR=$null; $env:TERM=$null; $env:COLORTERM=$null')) throw new Error("Geerbte Automations-Farbvariablen werden vor dem Terminal-Start nicht entfernt.");
if (!agent.includes("npx @openai/codex resume")) throw new Error("Session-Resume entspricht nicht dem normalen lokalen Codex-Start.");
if (agent.includes("codex --no-daemon resume")) throw new Error("Session-Resume verwendet weiterhin den abweichenden No-Daemon-Start.");
if (agent.includes('startInfo.ArgumentList.Add("-NoProfile")')) throw new Error("Session-Resume unterdrückt weiterhin das PowerShell-Profil.");
if (!agent.includes("ExtractAssociatedIcon(executable)")) throw new Error("Das Tray verwendet nicht das eigene App-Icon.");
if (!agent.includes('app.MapGet("/api/terminal-settings"')) throw new Error("Die Terminal-Erkennung und -Auswahl fehlt.");
if (!agent.includes('"wwwroot", "locales"')) throw new Error("Tray und Web verwenden nicht dieselben Locale-Dateien.");
if (!agent.includes('CheckForUpdateAsync')) throw new Error("Die GitHub-Release-Prüfung fehlt.");
if (!agent.includes('releases/latest') || agent.includes('api.github.com/repos/')) throw new Error("Die Update-Prüfung ist weiterhin vom anonymen GitHub-API-Limit abhängig.");
if (!agent.includes('releases/latest/download/') || !agent.includes('ReadReleaseTag')) throw new Error("Der API-freie Fallback für die Update-Prüfung fehlt.");
if (!agent.includes('SHA256.HashDataAsync')) throw new Error("Update-Pakete werden nicht per SHA-256 geprüft.");
if (!agent.includes('TryApplyStagedUpdate(args)')) throw new Error("Der separate Update-Helfer fehlt.");
if (!agent.includes('lifetime.ApplicationStopping') || !agent.includes('process.WaitForExit(20000)') || !agent.includes('process.Kill();')) throw new Error("Der Update-Neustart beendet Live-Verbindungen oder den alten Prozess nicht sicher.");
if (!agent.includes('autostartWasEnabled') || !agent.includes('else AutoStartManager.Disable()')) throw new Error("Updates bewahren die Autostart-Einstellung nicht.");
if (!agent.includes('UpdateProgressWindow') || !agent.includes('UpdateStage.Verifying') || !agent.includes('language.Text("restarting")') || !agent.includes('PictureBoxSizeMode.Zoom')) throw new Error("Der sichtbare Update-Fortschritt samt App-Icon fehlt.");
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
  if (!/@media\s*\(\s*max-width\s*:\s*760px\s*\)/i.test(source)) throw new Error("Responsive Web-Feature fehlt: Mobile Media Query");
  for (const selector of [".terminal-dialog", ".header-menu-panel", ".language-options", ".list-layout.sidebar-collapsed", ".path-copy", ".settings-form-page .terminal-detected", ".session-git", ".session-project-remove", ".git-tooltip", ".history-content", ".roadmap-grid"])
    if (!hasRule(source, selector)) throw new Error(`Responsive Web-Feature fehlt: ${selector}`);
  if (!hasRuleDeclaration(source, ".project-create", "margin-top", "8px")) throw new Error("Responsive Web-Feature fehlt: Projektabstand");
};
assertResponsiveStyles(styles);
assertResponsiveStyles(styles.replace(/\s+/g, " "));
const history = JSON.parse(await readFile(new URL("../web/version-history.json", import.meta.url), "utf8"));
if (!Array.isArray(history.releases) || !history.releases.some((release) => release.released && release.tag)) throw new Error("Die aus Git erzeugte Versionshistorie fehlt.");
const roadmap = JSON.parse(await readFile(new URL("../web/roadmap.json", import.meta.url), "utf8"));
if (roadmap.version !== "1.0" || !Array.isArray(roadmap.items) || roadmap.items.length < 5) throw new Error("Die erweiterbare 1.0-Roadmap fehlt.");
const appIcon = await readFile(new URL("../web/app-icon.svg", import.meta.url), "utf8");
if (!appIcon.includes('fill="#ff6537"') || !appIcon.includes('stroke="#fff"')) throw new Error("Das gemeinsame orange-weiße App-Icon fehlt.");
if (!script.includes('class="mark"><img src="/app-icon.svg?v=4"')) throw new Error("Das App-Icon fehlt im Seiten-Header.");
if (!script.includes("git: brandIcon(") || !styles.includes("color: #f05032")) throw new Error("Das echte Git-Markenlogo fehlt.");
if (!script.includes("github: brandIcon(") || !script.includes("gitlab: brandIcon(") || !script.includes("providerIcon")) throw new Error("Die Anbieterlogos für GitHub- und GitLab-Remotes fehlen.");
if (!script.includes('mark class="search-match"') || !styles.includes("mark.search-match")) throw new Error("Die Hervorhebung sichtbarer Suchtreffer fehlt.");
const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const project = await readFile(new URL("../agent/CodexSessionManager.Agent.csproj", import.meta.url), "utf8");
const projectVersion = project.match(/<Version>([^<]+)<\/Version>/)?.[1];
if (!projectVersion || projectVersion !== packageJson.version || !agent.includes(`public const string Version = "${projectVersion}"`)) throw new Error("Die Release-Versionen sind nicht synchron.");
if (script.includes('class="locations"')) throw new Error("Die Session-Liste enthält weiterhin ausführliche Pfadangaben.");
if (script.includes('class="session-pill')) throw new Error("Die Session-Liste enthält weiterhin Status-Pills.");
if (!script.includes('class="metadata-disclosure"')) throw new Error("Technische Metadaten sind nicht einklappbar.");
console.log("Web-App und Companion-Features sind syntaktisch valide.");
