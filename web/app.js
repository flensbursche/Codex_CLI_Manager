const app = document.querySelector("#app");
const state = { sessions: [], projects: [], project: null, view: "all" };
let actionToken = "";
let companionInfo = { supportsAutostart: false, autostartEnabled: false };
let language = "de";
let sessionEvents;
const languageLocales = { de: "de-DE", en: "en-GB", da: "da-DK" };
const messages = {
  de: {
    live: "Live", autostart: "Autostart", on: "An", off: "Aus", terminalSettings: "Terminal Settings", darkMode: "Dark Mode", lightMode: "Light Mode", language: "Sprache",
    localSetting: "Lokale Einstellung", terminal: "Terminal", close: "Schließen", terminalWindow: "Terminal-Fenster", automatic: "Automatisch", powershellConsole: "PowerShell-Konsole", powershellVersion: "PowerShell-Version", activeWith: "Aktiv: {0} mit {1}", found: "Gefunden", notFound: "Nicht gefunden", version: "Version {0}", cancel: "Abbrechen", save: "Speichern",
    sessions: "Sessions", favorites: "Favoriten", archive: "Archiv", projects: "Projekte", newProject: "Neues Projekt", projectName: "Projektname", clearProjectName: "Projektname leeren", createProject: "Projekt anlegen",
    sessionSearch: "Session suchen", clearSearch: "Suche leeren", filter: "Filter", allSources: "Alle Quellen", internalOff: "Interne aus", internalOn: "Interne an", anyPeriod: "Jeder Zeitraum", periodHours: "{0} Stunden", periodDays: "{0} Tage", allSessions: "Alle Sessions", active: "Aktiv", activity: "Aktivität", created: "Erstellt", project: "Projekt", title: "Titel", location: "Speicherort", loading: "Lädt …",
    emptyResponse: "Leere Antwort vom lokalen Companion ({0}). Bitte die Seite neu laden.", invalidResponse: "Ungültige Antwort vom lokalen Companion ({0}). Bitte die Seite neu laden.", requestFailed: "Anfrage fehlgeschlagen.", sessionsLoadFailed: "Sessions konnten nicht geladen werden", retry: "Erneut versuchen",
    justNow: "gerade eben", minutesAgo: "vor {0} Min.", hoursAgo: "vor {0} Std.", yesterday: "gestern", yesterdayGroup: "Gestern", daysAgo: "vor {0} Tagen", justEnded: "Gerade beendet", today: "Heute", thisWeek: "Diese Woche", lastWeek: "Letzte Woche", thisMonth: "Dieser Monat", older: "Älter",
    sessionCountOne: "{0} Session", sessionCountMany: "{0} Sessions", noSessions: "Keine Sessions für diese Filter gefunden.", noProject: "Ohne Projekt", details: "Details", openDesktop: "In Codex Desktop öffnen", alreadyOpen: "Session ist bereits geöffnet", resume: "Session fortsetzen", removeFavorite: "Aus Favoriten entfernen", addFavorite: "Zu Favoriten hinzufügen", running: "Läuft",
    backSessions: "← Sessions", sessionLoading: "Session wird geladen …", unnamedSession: "Unbenannte Session", status: "Status", ready: "Bereit", source: "Quelle", model: "Modell", branch: "Branch", archived: "Archiviert", yes: "Ja", no: "Nein", opened: "Geöffnet", continue: "Fortsetzen", archiveAction: "Archivieren", openInDesktop: "In Desktop öffnen", storageLocations: "Speicherorte", sessionFile: "Session-Datei", projectFolder: "Projektordner", overview: "Übersicht", technicalMetadata: "Technische Metadaten", connecting: "Verbindung zum lokalen Companion wird hergestellt …"
  },
  en: {
    live: "Live", autostart: "Autostart", on: "On", off: "Off", terminalSettings: "Terminal Settings", darkMode: "Dark Mode", lightMode: "Light Mode", language: "Language",
    localSetting: "Local setting", terminal: "Terminal", close: "Close", terminalWindow: "Terminal window", automatic: "Automatic", powershellConsole: "PowerShell console", powershellVersion: "PowerShell version", activeWith: "Active: {0} with {1}", found: "Found", notFound: "Not found", version: "Version {0}", cancel: "Cancel", save: "Save",
    sessions: "Sessions", favorites: "Favorites", archive: "Archive", projects: "Projects", newProject: "New project", projectName: "Project name", clearProjectName: "Clear project name", createProject: "Create project",
    sessionSearch: "Search sessions", clearSearch: "Clear search", filter: "Filter", allSources: "All sources", internalOff: "Internal off", internalOn: "Internal on", anyPeriod: "Any time", periodHours: "{0} hours", periodDays: "{0} days", allSessions: "All sessions", active: "Active", activity: "Activity", created: "Created", project: "Project", title: "Title", location: "Location", loading: "Loading …",
    emptyResponse: "Empty response from the local companion ({0}). Please reload the page.", invalidResponse: "Invalid response from the local companion ({0}). Please reload the page.", requestFailed: "Request failed.", sessionsLoadFailed: "Sessions could not be loaded", retry: "Try again",
    justNow: "just now", minutesAgo: "{0} min. ago", hoursAgo: "{0} hr. ago", yesterday: "yesterday", yesterdayGroup: "Yesterday", daysAgo: "{0} days ago", justEnded: "Just ended", today: "Today", thisWeek: "This week", lastWeek: "Last week", thisMonth: "This month", older: "Older",
    sessionCountOne: "{0} session", sessionCountMany: "{0} sessions", noSessions: "No sessions match these filters.", noProject: "No project", details: "Details", openDesktop: "Open in Codex Desktop", alreadyOpen: "Session is already open", resume: "Resume session", removeFavorite: "Remove from favorites", addFavorite: "Add to favorites", running: "Running",
    backSessions: "← Sessions", sessionLoading: "Loading session …", unnamedSession: "Unnamed session", status: "Status", ready: "Ready", source: "Source", model: "Model", branch: "Branch", archived: "Archived", yes: "Yes", no: "No", opened: "Open", continue: "Resume", archiveAction: "Archive", openInDesktop: "Open in Desktop", storageLocations: "Locations", sessionFile: "Session file", projectFolder: "Project folder", overview: "Overview", technicalMetadata: "Technical metadata", connecting: "Connecting to the local companion …"
  },
  da: {
    live: "Live", autostart: "Autostart", on: "Til", off: "Fra", terminalSettings: "Terminalindstillinger", darkMode: "Mørk tilstand", lightMode: "Lys tilstand", language: "Sprog",
    localSetting: "Lokal indstilling", terminal: "Terminal", close: "Luk", terminalWindow: "Terminalvindue", automatic: "Automatisk", powershellConsole: "PowerShell-konsol", powershellVersion: "PowerShell-version", activeWith: "Aktiv: {0} med {1}", found: "Fundet", notFound: "Ikke fundet", version: "Version {0}", cancel: "Annuller", save: "Gem",
    sessions: "Sessioner", favorites: "Favoritter", archive: "Arkiv", projects: "Projekter", newProject: "Nyt projekt", projectName: "Projektnavn", clearProjectName: "Ryd projektnavn", createProject: "Opret projekt",
    sessionSearch: "Søg sessioner", clearSearch: "Ryd søgning", filter: "Filter", allSources: "Alle kilder", internalOff: "Interne fra", internalOn: "Interne til", anyPeriod: "Alle perioder", periodHours: "{0} timer", periodDays: "{0} dage", allSessions: "Alle sessioner", active: "Aktiv", activity: "Aktivitet", created: "Oprettet", project: "Projekt", title: "Titel", location: "Placering", loading: "Indlæser …",
    emptyResponse: "Tomt svar fra den lokale companion ({0}). Genindlæs siden.", invalidResponse: "Ugyldigt svar fra den lokale companion ({0}). Genindlæs siden.", requestFailed: "Forespørgslen mislykkedes.", sessionsLoadFailed: "Sessionerne kunne ikke indlæses", retry: "Prøv igen",
    justNow: "lige nu", minutesAgo: "for {0} min. siden", hoursAgo: "for {0} timer siden", yesterday: "i går", yesterdayGroup: "I går", daysAgo: "for {0} dage siden", justEnded: "Lige afsluttet", today: "I dag", thisWeek: "Denne uge", lastWeek: "Sidste uge", thisMonth: "Denne måned", older: "Ældre",
    sessionCountOne: "{0} session", sessionCountMany: "{0} sessioner", noSessions: "Ingen sessioner matcher disse filtre.", noProject: "Intet projekt", details: "Detaljer", openDesktop: "Åbn i Codex Desktop", alreadyOpen: "Sessionen er allerede åben", resume: "Fortsæt session", removeFavorite: "Fjern fra favoritter", addFavorite: "Føj til favoritter", running: "Kører",
    backSessions: "← Sessioner", sessionLoading: "Indlæser session …", unnamedSession: "Unavngiven session", status: "Status", ready: "Klar", source: "Kilde", model: "Model", branch: "Branch", archived: "Arkiveret", yes: "Ja", no: "Nej", opened: "Åben", continue: "Fortsæt", archiveAction: "Arkivér", openInDesktop: "Åbn i Desktop", storageLocations: "Placeringer", sessionFile: "Sessionsfil", projectFolder: "Projektmappe", overview: "Oversigt", technicalMetadata: "Tekniske metadata", connecting: "Opretter forbindelse til den lokale companion …"
  }
};
const t = (key, ...values) => { const template = messages[language]?.[key] ?? messages.en[key] ?? key; return values.reduce((text, value, index) => text.replaceAll(`{${index}}`, value), template); };
const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
const relative = (timestamp) => { const ms = Date.now() - timestamp; if (ms < 60000) return t("justNow"); const minutes = Math.round(ms / 60000); if (minutes < 60) return t("minutesAgo", minutes); const hours = Math.round(minutes / 60); if (hours < 24) return t("hoursAgo", hours); const days = Math.round(hours / 24); if (days === 1) return t("yesterday"); if (days < 14) return t("daysAgo", days); return new Intl.DateTimeFormat(languageLocales[language], { dateStyle: "medium", timeStyle: "short" }).format(timestamp); };
const sessionTime = (session) => { const age = Date.now() - Number(session.updatedAt || 0); return !session.active && age >= 0 && age <= 3 * 60000 ? t("justEnded") : relative(session.updatedAt); };
const directoryLabel = (value) => { const path = String(value || t("noProject")); return value && !/[\\/]$/.test(path) ? `${path}\\` : path; };
const formatValue = (value) => { if (value === null || value === undefined || value === "") return "–"; if (typeof value === "boolean") return value ? t("yes") : t("no"); if (typeof value === "object") return JSON.stringify(value, null, 2); return String(value); };
const tablerIcon = (paths) => `<svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;
const icons = {
  sessions: tablerIcon('<path d="M9 6h11M9 12h11M9 18h11"/><path d="M5 6v.01M5 12v.01M5 18v.01"/>'),
  favorite: tablerIcon('<path d="M12 17.75l-6.172 3.245 1.179-6.873-5-4.867 6.9-1.002L12 2l3.086 6.253 6.9 1.002-4.993 4.867 1.179 6.873z"/>'),
  archive: tablerIcon('<path d="M3 6h18M5 6v14h14V6M9 10h6"/><path d="M4 3h16v3H4z"/>'),
  play: tablerIcon('<path d="M7 4v16l13-8z"/>'),
  desktop: tablerIcon('<path d="M3 4h18v12H3zM7 20h10M9 16v4M15 16v4"/>'),
  chrome: tablerIcon('<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="3"/><path d="M12 9h8.4M14.6 13.5l-4.2 7.3M9.4 13.5L5.2 6.2"/>'),
  terminal: tablerIcon('<path d="M5 7l5 5-5 5M12 17h7"/>'),
  settings: tablerIcon('<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.83 2.83-.06-.06a1.7 1.7 0 0 0-1.88-.34 1.7 1.7 0 0 0-1.03 1.55V21h-4v-.08A1.7 1.7 0 0 0 8.97 19.4a1.7 1.7 0 0 0-1.88.34l-.06.06-2.83-2.83.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.52-1.03H3v-4h.08A1.7 1.7 0 0 0 4.6 8.94a1.7 1.7 0 0 0-.34-1.88L4.2 7l2.83-2.83.06.06a1.7 1.7 0 0 0 1.88.34A1.7 1.7 0 0 0 10 3.05V3h4v.05a1.7 1.7 0 0 0 1.03 1.52 1.7 1.7 0 0 0 1.88-.34l.06-.06L19.8 7l-.06.06a1.7 1.7 0 0 0-.34 1.88A1.7 1.7 0 0 0 20.92 10H21v4h-.08A1.7 1.7 0 0 0 19.4 15z"/>'),
  moon: tablerIcon('<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>'),
  sun: tablerIcon('<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.42-1.42M17.66 6.34l1.41-1.41"/>'),
  globe: tablerIcon('<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18"/>'),
  filter: tablerIcon('<path d="M4 6h16M7 12h10M10 18h4"/>'),
  plus: tablerIcon('<path d="M12 5v14M5 12h14"/>'),
  close: tablerIcon('<path d="M6 6l12 12M18 6L6 18"/>')
};
const theme = localStorage.getItem("codex-manager-theme") || "light";
document.documentElement.dataset.theme = theme;

function header() {
  const autostart = companionInfo.supportsAutostart ? `<button class="quiet-button" id="autostart-toggle">${t("autostart")}: ${companionInfo.autostartEnabled ? t("on") : t("off")}</button>` : "";
  const darkMode = document.documentElement.dataset.theme === "light";
  const themeLabel = darkMode ? t("darkMode") : t("lightMode");
  return `<header class="top"><a class="brand" href="/"><span class="mark"><img src="/app-icon.svg?v=3" alt=""></span><strong>Codex CLI Manager</strong></a><span class="live"><i class="dot"></i>${t("live")}</span><span class="spacer"></span>${autostart}<label class="language-control" title="${t("language")}">${icons.globe}<select id="language-select" aria-label="${t("language")}"><option value="de" ${language === "de" ? "selected" : ""}>DE</option><option value="en" ${language === "en" ? "selected" : ""}>EN</option><option value="da" ${language === "da" ? "selected" : ""}>DK</option></select></label><button class="quiet-button terminal-settings-toggle" id="terminal-settings-toggle">${icons.settings}<span>${t("terminalSettings")}</span></button><button class="quiet-button icon-button" id="theme-toggle" aria-label="${themeLabel}" title="${themeLabel}">${darkMode ? icons.moon : icons.sun}</button></header>`;
}

function bindTheme() {
  document.querySelector("#theme-toggle").onclick = () => { const next = document.documentElement.dataset.theme === "light" ? "dark" : "light"; const button = document.querySelector("#theme-toggle"); const label = next === "light" ? t("darkMode") : t("lightMode"); document.documentElement.dataset.theme = next; localStorage.setItem("codex-manager-theme", next); button.innerHTML = next === "light" ? icons.moon : icons.sun; button.setAttribute("aria-label", label); button.title = label; };
  const autostart = document.querySelector("#autostart-toggle");
  if (autostart) autostart.onclick = async () => { autostart.disabled = true; try { const result = await fetchJson(`/api/autostart/${!companionInfo.autostartEnabled}`, { method: "POST" }); companionInfo.autostartEnabled = result.enabled; autostart.textContent = `${t("autostart")}: ${result.enabled ? t("on") : t("off")}`; } finally { autostart.disabled = false; } };
  document.querySelector("#language-select").onchange = changeLanguage;
  document.querySelector("#terminal-settings-toggle").onclick = openTerminalSettings;
}

async function changeLanguage(event) {
  const select = event.currentTarget; select.disabled = true;
  try {
    const result = await fetchJson("/api/language", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ language: select.value }) });
    language = result.language; document.documentElement.lang = language;
    const match = window.location.pathname.match(/^\/session\/([A-Za-z0-9_-]+)$/);
    if (match) void sessionPage(match[1]); else listPage();
  } catch (error) { select.disabled = false; window.alert(error.message); }
}

async function openTerminalSettings() {
  const existing = document.querySelector("#terminal-settings-dialog");
  if (existing) { existing.showModal(); return; }
  let settings;
  try { settings = await fetchJson("/api/terminal-settings"); }
  catch (error) { window.alert(error.message); return; }
  const detected = settings.detected;
  const dialog = document.createElement("dialog");
  dialog.className = "terminal-dialog";
  dialog.id = "terminal-settings-dialog";
  dialog.innerHTML = `<form class="terminal-settings-form">
    <header><div><span class="eyebrow">${t("localSetting")}</span><h2>${t("terminal")}</h2></div><button class="dialog-close" type="button" aria-label="${t("close")}">${icons.close}</button></header>
    <label><span>${t("terminalWindow")}</span><select id="terminal-choice"><option value="auto">${t("automatic")}</option><option value="windows-terminal" ${detected.windowsTerminal.available ? "" : "disabled"}>Windows Terminal</option><option value="console">${t("powershellConsole")}</option></select></label>
    <label><span>${t("powershellVersion")}</span><select id="shell-choice"><option value="auto">${t("automatic")}</option><option value="powershell7" ${detected.powershell7.available ? "" : "disabled"}>PowerShell 7</option><option value="windows-powershell" ${detected.windowsPowerShell.available ? "" : "disabled"}>Windows PowerShell 5.1</option></select></label>
    <div class="terminal-resolution">${t("activeWith", `<strong>${esc(settings.resolvedTerminal)}</strong>`, `<strong>${esc(settings.resolvedShell)}</strong>`)}</div>
    <div class="terminal-detected">${[detected.windowsTerminal, detected.powershell7, detected.windowsPowerShell].map(terminalDetectionRow).join("")}</div>
    <div class="terminal-settings-error" id="terminal-settings-error"></div>
    <footer><button class="secondary" type="button" data-close>${t("cancel")}</button><button class="primary" type="submit">${t("save")}</button></footer>
  </form>`;
  document.body.append(dialog);
  const terminalChoice = dialog.querySelector("#terminal-choice"); const shellChoice = dialog.querySelector("#shell-choice");
  terminalChoice.value = settings.terminal; shellChoice.value = settings.shell;
  dialog.querySelectorAll("[data-close], .dialog-close").forEach((button) => button.onclick = () => dialog.close());
  dialog.querySelector("form").onsubmit = async (event) => {
    event.preventDefault(); const errorBox = dialog.querySelector("#terminal-settings-error"); errorBox.textContent = "";
    const submit = dialog.querySelector('[type="submit"]'); submit.disabled = true;
    try {
      await fetchJson("/api/terminal-settings", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ terminal: terminalChoice.value, shell: shellChoice.value }) });
      dialog.close();
    } catch (error) { errorBox.textContent = error.message; submit.disabled = false; }
  };
  dialog.onclose = () => dialog.remove();
  dialog.onclick = (event) => { if (event.target === dialog) dialog.close(); };
  dialog.showModal();
}

function terminalDetectionRow(item) {
  const stateLabel = item.available ? item.version ? t("version", item.version) : t("found") : t("notFound");
  return `<div class="terminal-detection ${item.available ? "available" : "missing"}"><i></i><div><strong>${esc(item.name)}</strong><span>${esc(stateLabel)}</span>${item.path ? `<code title="${esc(item.path)}">${esc(item.path)}</code>` : ""}</div></div>`;
}

async function fetchJson(url, options) {
  const request = { ...(options || {}) };
  request.headers = { ...(request.headers || {}) };
  if ((request.method || "GET").toUpperCase() === "POST") request.headers["X-Codex-Manager-Token"] = actionToken;
  const response = await fetch(url, request);
  const body = await response.text();
  if (!body) throw new Error(t("emptyResponse", response.status));
  let result;
  try { result = JSON.parse(body); }
  catch { throw new Error(t("invalidResponse", response.status)); }
  if (!response.ok) throw new Error(result.error || result.detail || result.title || t("requestFailed"));
  return result.data;
}

function listPage() {
  app.innerHTML = `
    <div class="shell">
      ${header()}
      <main class="list-layout">
        <aside class="sidebar">
          <nav class="sidebar-primary" id="sidebar-primary">
            <button class="active" data-view="all"><span class="nav-icon">${icons.sessions}</span><span>${t("sessions")}</span></button>
            <button data-view="favorites"><span class="nav-icon">${icons.favorite}</span><span>${t("favorites")}</span></button>
            <button data-view="archived"><span class="nav-icon">${icons.archive}</span><span>${t("archive")}</span></button>
          </nav>
          <div class="rail-divider"></div>
          <div class="side-title">${t("projects")}</div>
          <div class="project-create">
            <button class="project-create-toggle" id="project-create-toggle" type="button"><span>${icons.plus}</span>${t("newProject")}</button>
            <form class="project-create-form" id="project-create-form" hidden>
              <label class="project-name-field"><input id="project-name" maxlength="64" placeholder="${t("projectName")}" autocomplete="off"><button class="input-clear project-name-clear" id="project-name-clear" type="button" aria-label="${t("clearProjectName")}" title="${t("clearProjectName")}" hidden>${icons.close}</button></label>
              <button type="submit" aria-label="${t("createProject")}">${icons.plus}</button>
            </form>
          </div>
          <nav class="projects" id="projects"></nav>
        </aside>
        <section class="main">
          <div class="searchbar">
            <label class="search">⌕<input id="search" placeholder="${t("sessionSearch")}" autofocus><button class="input-clear" id="search-clear" type="button" aria-label="${t("clearSearch")}" title="${t("clearSearch")}" hidden>${icons.close}</button></label>
            <details class="filter-disclosure">
              <summary><span class="filter-icon">${icons.filter}</span>${t("filter")}</summary>
              <div class="filters">
                <select id="source"><option value="all">${t("allSources")}</option></select>
                <select id="internal"><option value="hide">${t("internalOff")}</option><option value="show">${t("internalOn")}</option></select>
                <select id="period"><option value="0">${t("anyPeriod")}</option><option value="1">${t("periodHours", 24)}</option><option value="7">${t("periodDays", 7)}</option><option value="30">${t("periodDays", 30)}</option><option value="90">${t("periodDays", 90)}</option></select>
                <select id="pinned"><option value="all">${t("allSessions")}</option><option value="yes">${t("favorites")}</option></select>
                <select id="archive"><option value="active">${t("active")}</option><option value="archived">${t("archive")}</option></select>
                <select id="sort"><option value="updated">${t("activity")}</option><option value="created">${t("created")}</option><option value="project">${t("project")}</option><option value="title">${t("title")}</option><option value="path">${t("location")}</option></select>
              </div>
            </details>
          </div>
          <div class="count" id="count">${t("loading")}</div>
          <div class="sessions" id="sessions"></div>
        </section>
      </main>
    </div>`;
  bindTheme();
  const search = document.querySelector("#search"); const clearSearch = document.querySelector("#search-clear");
  search.oninput = () => { clearSearch.hidden = !search.value; renderSessions(); };
  clearSearch.onclick = () => { search.value = ""; clearSearch.hidden = true; search.focus(); renderSessions(); };
  for (const id of ["source", "period", "sort"]) document.querySelector(`#${id}`).onchange = renderSessions;
  document.querySelector("#pinned").onchange = () => { state.view = document.querySelector("#pinned").value === "yes" ? "favorites" : document.querySelector("#archive").value === "archived" ? "archived" : "all"; renderSidebarNav(); renderSessions(); };
  document.querySelector("#internal").onchange = () => { state.project = null; renderSources(); renderProjects(); renderSessions(); };
  document.querySelectorAll("#sidebar-primary button").forEach((button) => button.onclick = () => selectView(button.dataset.view));
  document.querySelector("#archive").onchange = () => { state.view = document.querySelector("#archive").value === "archived" ? "archived" : document.querySelector("#pinned").value === "yes" ? "favorites" : "all"; loadSessions(); };
  bindProjectCreator();
  void initializeList();
  connectSessionEvents();
}

async function initializeList() {
  await loadSessions();
  await loadProjects();
}

async function loadProjects() {
  try { state.projects = await fetchJson("/api/projects"); renderProjects(); }
  catch (error) { document.querySelector("#count").textContent = error.message; }
}

async function loadSessions({ preserveProject = false, silent = false } = {}) {
  try {
    const archived = document.querySelector("#archive").value === "archived";
    state.sessions = await fetchJson(`/api/sessions?archived=${archived ? "true" : "false"}`);
    if (!preserveProject) state.project = null;
    else if (state.project && !state.sessions.some((session) => session.projectId === state.project) && !state.projects.some((project) => project.id === state.project)) state.project = null;
    renderSidebarNav(); renderSources(); renderProjects(); renderSessions();
  } catch (error) {
    if (silent) return;
    document.querySelector("#count").textContent = t("sessionsLoadFailed");
    document.querySelector("#sessions").innerHTML = `<div class="empty">${esc(error.message)}<br><button class="retry-load" id="retry-load">${t("retry")}</button></div>`;
    document.querySelector("#retry-load").onclick = () => loadSessions();
  }
}

function connectSessionEvents() {
  sessionEvents?.close();
  sessionEvents = new EventSource("/api/events");
  sessionEvents.addEventListener("sessions", () => {
    if (document.querySelector("#sessions")) void loadSessions({ preserveProject: true, silent: true });
  });
  sessionEvents.onopen = () => document.querySelector(".live")?.classList.add("connected");
  sessionEvents.onerror = () => document.querySelector(".live")?.classList.remove("connected");
}

function renderSources() {
  const select = document.querySelector("#source"); const selected = select.value;
  const sources = [...new Set(sessionsWithoutInternalFilter().map((session) => session.source).filter(Boolean))].sort();
  select.innerHTML = `<option value="all">${t("allSources")}</option>${sources.map((source) => `<option value="${esc(source)}">${esc(source)}</option>`).join("")}`;
  select.value = sources.includes(selected) ? selected : "all";
}

function renderProjects() {
  const visible = sessionsWithoutInternalFilter().filter((session) => !session.parentId);
  const stats = new Map();
  visible.forEach((session) => { const current = stats.get(session.projectId) || { count: 0, updatedAt: 0 }; current.count += 1; current.updatedAt = Math.max(current.updatedAt, Number(session.updatedAt) || 0); stats.set(session.projectId, current); });
  const entries = state.projects.map((project) => ({ ...project, ...(stats.get(project.id) || { count: 0, updatedAt: 0 }) })).sort((a, b) => b.updatedAt - a.updatedAt || a.name.localeCompare(b.name));
  const projects = document.querySelector("#projects");
  projects.innerHTML = entries.map(projectButton).join("");
  projects.querySelectorAll("button").forEach((button) => {
    button.onclick = () => { state.project = button.dataset.projectId; state.view = "project"; renderSidebarNav(); renderProjects(); renderSessions(); };
    button.ondragover = (event) => { event.preventDefault(); button.classList.add("drop-target"); };
    button.ondragleave = () => button.classList.remove("drop-target");
    button.ondrop = (event) => { event.preventDefault(); button.classList.remove("drop-target"); const threadId = event.dataTransfer.getData("text/codex-thread"); if (threadId) void assignSessionToProject(threadId, button.dataset.projectId); };
  });
}

function projectButton(project) {
  return `<button class="${state.project === project.id ? "active" : ""}" data-project-id="${project.id}"><span class="project-dot"></span><span>${esc(project.name)}</span><small>${project.count}</small></button>`;
}

function bindProjectCreator() {
  const toggle = document.querySelector("#project-create-toggle"); const form = document.querySelector("#project-create-form"); const input = document.querySelector("#project-name"); const clearInput = document.querySelector("#project-name-clear");
  toggle.onclick = () => { form.hidden = !form.hidden; if (!form.hidden) input.focus(); };
  input.oninput = () => { clearInput.hidden = !input.value; };
  clearInput.onclick = () => { input.value = ""; clearInput.hidden = true; input.focus(); };
  form.onsubmit = async (event) => { event.preventDefault(); const name = input.value.trim(); if (!name) return; const project = await fetchJson("/api/projects", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name }) }); state.projects.push(project); input.value = ""; clearInput.hidden = true; form.hidden = true; renderProjects(); };
}

async function assignSessionToProject(threadId, projectId) {
  const result = await fetchJson(`/api/projects/${encodeURIComponent(projectId)}/sessions/${encodeURIComponent(threadId)}`, { method: "POST" });
  const project = state.projects.find((item) => item.id === result.projectId); const session = state.sessions.find((item) => item.id === threadId);
  if (session && project) { session.projectId = project.id; session.project = project.name; }
  renderProjects(); renderSessions();
}

function bindSessionDragging() {
  document.querySelectorAll("article.session-row").forEach((row) => {
    row.ondragstart = (event) => { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/codex-thread", row.dataset.id); row.classList.add("dragging"); };
    row.ondragend = () => { row.classList.remove("dragging"); document.querySelectorAll(".projects .drop-target").forEach((target) => target.classList.remove("drop-target")); };
  });
}

function renderSidebarNav() {
  document.querySelectorAll("#sidebar-primary button").forEach((button) => button.classList.toggle("active", button.dataset.view === state.view));
}

function selectView(view) {
  state.view = view; state.project = null;
  document.querySelector("#pinned").value = view === "favorites" ? "yes" : "all";
  const archive = document.querySelector("#archive"); const nextArchive = view === "archived" ? "archived" : "active";
  renderSidebarNav();
  if (archive.value !== nextArchive) { archive.value = nextArchive; loadSessions(); return; }
  renderProjects(); renderSessions();
}

function activityGroup(timestamp) {
  const value = Number(timestamp) || 0; const now = new Date(); const today = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const day = 86400000; const weekStart = today - ((now.getDay() + 6) % 7) * day; const monthStart = new Date(now.getFullYear(), now.getMonth(), 1).getTime();
  if (value >= Date.now() - 3600000) return "justNow";
  if (value >= today) return "today";
  if (value >= today - day) return "yesterdayGroup";
  if (value >= weekStart) return "thisWeek";
  if (value >= weekStart - 7 * day) return "lastWeek";
  if (value >= monthStart) return "thisMonth";
  return "older";
}

function isInternalSession(session) {
  if (session.internal === true) return true;
  const source = String(session.threadSource || session.thread_source || session.source || "").trim().toLowerCase();
  return source === "guardian" || source === "guardian_review" || source === "memory_consolidation" || source.includes("guardian") || source.includes("memory consolidation");
}

function sessionsWithoutInternalFilter() {
  const showInternal = document.querySelector("#internal")?.value === "show";
  return showInternal ? state.sessions : state.sessions.filter((session) => !isInternalSession(session));
}

function filteredSessions() {
  const query = document.querySelector("#search").value.trim().toLowerCase(); const source = document.querySelector("#source").value; const pinned = document.querySelector("#pinned").value; const days = Number(document.querySelector("#period").value); const sort = document.querySelector("#sort").value; const cutoff = days ? Date.now() - days * 86400000 : 0;
  const byId = new Map(state.sessions.map((session) => [session.id, session]));
  return sessionsWithoutInternalFilter().filter((session) => (!state.project || session.projectId === state.project || (session.parentId && byId.get(session.parentId)?.projectId === state.project)) && (source === "all" || session.source === source) && (pinned === "all" || session.pinned) && (!cutoff || session.updatedAt >= cutoff) && (!query || `${session.title} ${session.preview} ${session.project} ${session.branch || ""} ${session.cwd} ${session.storagePath || ""}`.toLowerCase().includes(query))).sort((a, b) => sort === "created" ? b.createdAt - a.createdAt : sort === "project" ? a.project.localeCompare(b.project) : sort === "title" ? a.title.localeCompare(b.title) : sort === "path" ? (a.storagePath || "").localeCompare(b.storagePath || "") : b.updatedAt - a.updatedAt);
}

function sessionBundles(sessions) {
  const ids = new Set(sessions.map((session) => session.id)); const shadows = new Map();
  sessions.forEach((session) => { if (session.parentId && ids.has(session.parentId)) { const list = shadows.get(session.parentId) || []; list.push(session); shadows.set(session.parentId, list); } });
  return sessions.filter((session) => !session.parentId || !ids.has(session.parentId)).map((master) => { const children = shadows.get(master.id) || []; children.sort((a, b) => a.createdAt - b.createdAt); return { master, shadows: children, updatedAt: Math.max(master.updatedAt || 0, ...children.map((child) => child.updatedAt || 0)) }; });
}

function renderSessions() {
  const sessions = filteredSessions(); const sort = document.querySelector("#sort").value; const bundles = sessionBundles(sessions);
  if (sort === "updated") bundles.sort((a, b) => b.updatedAt - a.updatedAt);
  document.querySelector("#count").textContent = t(bundles.length === 1 ? "sessionCountOne" : "sessionCountMany", bundles.length);
  const groups = ["justNow", "today", "yesterdayGroup", "thisWeek", "lastWeek", "thisMonth", "older"];
  document.querySelector("#sessions").innerHTML = !bundles.length ? `<div class="empty">${t("noSessions")}</div>` : sort === "updated" ? groups.map((group) => { const items = bundles.filter((bundle) => activityGroup(bundle.updatedAt) === group); return items.length ? `<section class="session-group"><h2>${t(group)}</h2>${items.map((bundle) => sessionRow(bundle.master, bundle.shadows)).join("")}</section>` : ""; }).join("") : bundles.map((bundle) => sessionRow(bundle.master, bundle.shadows)).join("");
  document.querySelectorAll(".session-favorite").forEach((button) => button.onclick = () => toggleFavorite(button.dataset.id, button));
  document.querySelectorAll(".session-launch").forEach((button) => button.onclick = () => openFromList(button.dataset.id, button.dataset.action, button));
  bindSessionDragging();
}

function sessionRow(session, shadows = []) {
  const action = session.desktop ? "open-desktop" : "resume"; const disabled = session.active && !session.desktop; const label = session.desktop ? t("openDesktop") : disabled ? t("alreadyOpen") : t("resume");
  const source = session.desktop ? "ChatGPT Desktop" : "CLI";
  const sourceIcon = session.desktop ? icons.desktop : icons.terminal;
  const shadowList = shadows.length ? `<div class="shadow-sessions">${shadows.map(shadowSessionRow).join("")}</div>` : "";
  const favoriteLabel = session.pinned ? t("removeFavorite") : t("addFavorite");
  return `<article class="session-row ${session.active ? "active-session" : ""}" data-id="${session.id}" draggable="true"><div class="session-copy"><div class="session-context"><code title="${esc(session.cwd)}">${esc(directoryLabel(session.cwd))}</code><div class="session-meta"><time>${sessionTime(session)}</time><button class="session-favorite ${session.pinned ? "active" : ""}" data-id="${session.id}" aria-label="${favoriteLabel}" aria-pressed="${session.pinned ? "true" : "false"}" title="${favoriteLabel}">${icons.favorite}</button></div></div><div class="session-body"><div class="session-summary"><strong class="session-title" title="${esc(session.title)}">${esc(session.title)}</strong>${session.preview ? `<span class="session-prompt">${esc(session.preview)}</span>` : ""}</div><div class="session-actions"><a class="session-details" href="/session/${encodeURIComponent(session.id)}">${t("details")}</a><button class="session-launch ${session.desktop ? "desktop" : ""}" data-id="${session.id}" data-action="${action}" ${disabled ? "disabled" : ""} aria-label="${label}" title="${source}: ${label}"><span class="launch-source">${sourceIcon}</span><span class="launch-divider"></span><span class="launch-play">${icons.play}</span></button></div></div>${shadowList}</div></article>`;
}

function shadowSessionRow(session) {
  const usesChrome = /(^|\s)@Chrome\b/i.test(session.preview || ""); const sourceIcon = usesChrome ? icons.chrome : session.desktop ? icons.desktop : icons.terminal; const sourceLabel = usesChrome ? "Chrome" : session.desktop ? "Desktop" : "CLI";
  const status = session.active ? `<span class="shadow-status active"><i></i>${t("running")}</span>` : `<span class="shadow-status">${sessionTime(session)}</span>`;
  return `<div class="shadow-session"><span class="shadow-branch">↳</span><span class="shadow-title" title="${esc(session.title)}">${esc(session.title)}</span><span class="shadow-kind">${sourceIcon}${sourceLabel}</span>${status}</div>`;
}

async function toggleFavorite(threadId, button) {
  const session = state.sessions.find((item) => item.id === threadId); if (!session) return;
  const next = !session.pinned; button.disabled = true;
  try {
    const result = await fetchJson(`/api/session/${encodeURIComponent(threadId)}/pinned/${next}`, { method: "POST" });
    session.pinned = result.pinned; renderSessions();
  } catch (error) {
    button.disabled = false; button.title = error.message;
    document.querySelector("#count").textContent = error.message;
  }
}

async function openFromList(threadId, action, button) {
  button.disabled = true;
  try {
    await fetchJson(`/api/${action}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ threadId }) });
    if (action === "resume") { const session = state.sessions.find((item) => item.id === threadId); if (session) session.active = true; renderSessions(); }
    else button.disabled = false;
  } catch (error) {
    button.disabled = false; button.title = error.message;
    document.querySelector("#count").textContent = error.message;
  }
}

async function sessionPage(id) {
  app.innerHTML = `<div class="shell">${header()}<main class="session-page"><div class="session-content"><a class="back" href="/">${t("backSessions")}</a><div class="page-loading">${t("sessionLoading")}</div></div></main></div>`; bindTheme();
  try { const session = await fetchJson(`/api/session/${encodeURIComponent(id)}`); renderSessionDetail(session); }
  catch (error) { document.querySelector(".session-content").innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="empty">${esc(error.message)}</div>`; }
}

function renderSessionDetail(session) {
  const content = document.querySelector(".session-content"); const title = session.name || session.title || session.first_user_message || t("unnamedSession"); const description = session.preview || session.first_user_message || "";
  const summary = [[t("status"), session.active ? t("active") : t("ready")], [t("project"), session.project], [t("source"), session.threadSource || session.thread_source || session.source], [t("model"), session.model || session.modelProvider || session.model_provider], [t("branch"), session.branch || session.git_branch], [t("archived"), session.archived ? t("yes") : t("no")]];
  const excluded = new Set(["first_user_message", "preview"]); const metadata = Object.entries(session).filter(([key]) => !excluded.has(key)).sort(([a], [b]) => a.localeCompare(b));
  content.innerHTML = `<a class="back" href="/">${t("backSessions")}</a><section class="session-head"><div><span class="eyebrow">Codex Session</span><h1>${esc(title)}</h1><p>${esc(description)}</p><div class="action-error" id="action-error"></div></div><div class="actions"><button class="primary" data-action="resume" ${session.active ? "disabled" : ""}>${session.active ? t("opened") : t("continue")}</button><button class="secondary" data-action="open-project">VS Code</button><button class="danger" data-action="archive">${t("archiveAction")}</button></div></section><section class="section"><h2>${t("storageLocations")}</h2><div class="path-grid"><div class="path-card"><span>${t("sessionFile")}</span><code>${esc(session.storagePath || session.path || session.rollout_path || session.storage_path || "–")}</code></div><div class="path-card"><span>${t("projectFolder")}</span><code>${esc(session.cwd || "–")}</code></div></div></section><section class="section"><h2>${t("overview")}</h2><div class="summary-grid">${summary.map(([key, value]) => `<div class="summary-item"><span>${esc(key)}</span><strong>${esc(formatValue(value))}</strong></div>`).join("")}</div></section><details class="metadata-disclosure"><summary>${t("technicalMetadata")}</summary><div class="metadata">${metadata.map(([key, value]) => `<div class="metadata-row"><div class="metadata-key">${esc(key)}</div><div class="metadata-value"><pre>${esc(formatValue(value))}</pre></div></div>`).join("")}</div></details>`;
  if (session.desktop) { const desktopButton = content.querySelector('[data-action="resume"]'); desktopButton.dataset.action = "open-desktop"; desktopButton.disabled = false; desktopButton.textContent = t("openInDesktop"); }
  content.querySelectorAll("[data-action]").forEach((button) => button.onclick = () => runAction(button.dataset.action, session.id));
}

async function runAction(action, threadId) {
  const errorBox = document.querySelector("#action-error"); if (errorBox) errorBox.style.display = "none";
  try { await fetchJson(`/api/${action}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ threadId }) }); if (action === "archive") window.location.href = "/"; }
  catch (error) { if (errorBox) { errorBox.textContent = error.message; errorBox.style.display = "block"; } }
}

async function initialize() {
  try {
    const bootstrap = await fetchJson("/api/bootstrap");
    actionToken = bootstrap.token;
    companionInfo = bootstrap;
    const languageSettings = await fetchJson("/api/language");
    language = languageSettings.language; document.documentElement.lang = language;
    const match = window.location.pathname.match(/^\/session\/([A-Za-z0-9_-]+)$/);
    if (match) sessionPage(match[1]); else listPage();
  } catch (error) {
    app.innerHTML = `<div class="empty">${t("connecting")}</div>`;
    window.setTimeout(initialize, 1000);
  }
}

initialize();
