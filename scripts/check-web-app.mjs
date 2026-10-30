import { readFile } from "node:fs/promises";
import vm from "node:vm";

const script = await readFile(new URL("../web/app.js", import.meta.url), "utf8");
new vm.Script(script);
for (const feature of ['session.internal === true', 'open-desktop', 'resume-force', 'session-launch', 'session-details', 'session-actions', 'session-favorite', 'toggleFavorite(threadId, button)', 'project-create-form', 'project-name-clear', 'assignSessionToProject(threadId, projectId)', 'text/codex-thread', 'search-clear', 'sessionTime(session)', 'sessionBundles(sessions)', 'shadow-sessions', 'ChatGPT Desktop', 'session-context', 'new EventSource("/api/events")', '/api/terminal-settings', '/api/language', '/api/update-status', 'window.setTimeout(initialize, 1000)', 'await loadMessages(result.language)', 'header-menu', 'sidebar-collapsed', 'data-language', 'sessionSearchCountMany', 'bindCopyButtons()', '/title', 'terminalSettingsPage()', 'role="switch"', 'menu-about', 'menu-update']) {
  if (!script.includes(feature)) throw new Error(`Web-Feature fehlt: ${feature}`);
}
const agent = await readFile(new URL("../agent/Program.cs", import.meta.url), "utf8");
if (!agent.includes("thread-writer-locks")) throw new Error("Aktive Codex-Threads werden nicht über Writer-Locks erkannt.");
if (!agent.includes("AttachShadowSessions(items)")) throw new Error("Agent-erzeugte Schatten-Sessions werden keinem Master zugeordnet.");
if (!agent.includes("CodexStateStore.SetPinned(threadId, pinned)")) throw new Error("Favoriten werden nicht im Codex-State gespeichert.");
if (!agent.includes("projects.Assign(threadId, projectId)")) throw new Error("Sessions können keinem Projekt zugeordnet werden.");
if (!agent.includes('LanguageSettingsStore')) throw new Error("Die gemeinsame Sprachspeicherung fehlt.");
if (!agent.includes('app.MapGet("/api/language"')) throw new Error("Der Sprach-Endpunkt fehlt.");
for (const language of ["de", "en", "da"]) {
  const locale = JSON.parse(await readFile(new URL(`../web/locales/${language}.json`, import.meta.url), "utf8"));
  for (const key of ["sessions", "sessionSearchCountMany", "terminalSettings", "sessionOpen", "copyPath", "editTitle", "tray.open", "tray.updates"])
    if (!locale[key]) throw new Error(`Übersetzung fehlt: ${language}.${key}`);
}
if (!agent.includes("TerminalHost.OpenPowerShell(cwd, command)")) throw new Error("Session-Resume verwendet nicht Windows Terminal.");
if (!agent.includes('"Microsoft.WindowsTerminal_8wekyb3d8bbwe"')) throw new Error("Windows Terminal wird nicht über seinen App-Alias gestartet.");
if (!agent.includes('"{574e775e-4f2a-5b96-ac1e-a2962a402336}"')) throw new Error("Das normale PowerShell-Profil von Windows Terminal wird nicht explizit verwendet.");
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
if (!agent.includes('SHA256.HashDataAsync')) throw new Error("Update-Pakete werden nicht per SHA-256 geprüft.");
if (!agent.includes('TryApplyStagedUpdate(args)')) throw new Error("Der separate Update-Helfer fehlt.");
if (agent.includes("AllocConsole")) throw new Error("Die rohe Win32-Konsole ist weiterhin aktiv.");
if (!agent.includes('app.MapGet("/api/events"')) throw new Error("Der SSE-Endpunkt für Live-Updates fehlt.");
if (!agent.includes('AppEndpoint.SelectAndSave()')) throw new Error("Die automatische Ausweichwahl bei belegtem Port fehlt.");
if (!agent.includes('SessionMetadataStore')) throw new Error("Dauerhafte Session-Titel fehlen.");
const styles = await readFile(new URL("../web/app.css", import.meta.url), "utf8");
for (const feature of ["min-width: 0", "font-size: 0.875rem", "@media (max-width: 760px)", ".terminal-dialog", ".project-create {\n    margin-top: 8px;", ".header-menu-panel", ".language-options", ".list-layout.sidebar-collapsed", ".path-copy", ".settings-form-page .terminal-detected"]) {
  if (!styles.includes(feature)) throw new Error(`Responsive Web-Feature fehlt: ${feature}`);
}
const appIcon = await readFile(new URL("../web/app-icon.svg", import.meta.url), "utf8");
if (!appIcon.includes('fill="#ff6537"') || !appIcon.includes('stroke="#fff"')) throw new Error("Das gemeinsame orange-weiße App-Icon fehlt.");
if (!script.includes('class="mark"><img src="/app-icon.svg?v=3"')) throw new Error("Das App-Icon fehlt im Seiten-Header.");
const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const project = await readFile(new URL("../agent/CodexSessionManager.Agent.csproj", import.meta.url), "utf8");
const projectVersion = project.match(/<Version>([^<]+)<\/Version>/)?.[1];
if (!projectVersion || projectVersion !== packageJson.version || !agent.includes(`public const string Version = "${projectVersion}"`)) throw new Error("Die Release-Versionen sind nicht synchron.");
if (script.includes('class="locations"')) throw new Error("Die Session-Liste enthält weiterhin ausführliche Pfadangaben.");
if (script.includes('class="session-pill')) throw new Error("Die Session-Liste enthält weiterhin Status-Pills.");
if (!script.includes('class="metadata-disclosure"')) throw new Error("Technische Metadaten sind nicht einklappbar.");
console.log("Web-App und Companion-Features sind syntaktisch valide.");
