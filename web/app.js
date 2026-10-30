const app = document.querySelector("#app");
const state = { sessions: [], projects: [], project: null, view: "all" };
let actionToken = "";
let companionInfo = { supportsAutostart: false, autostartEnabled: false };
let sessionEvents;
const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
const relative = (timestamp) => { const ms = Date.now() - timestamp; if (ms < 60000) return "gerade eben"; const minutes = Math.round(ms / 60000); if (minutes < 60) return `vor ${minutes} Min.`; const hours = Math.round(minutes / 60); if (hours < 24) return `vor ${hours} Std.`; const days = Math.round(hours / 24); if (days === 1) return "gestern"; if (days < 14) return `vor ${days} Tagen`; return new Intl.DateTimeFormat("de-DE", { dateStyle: "medium", timeStyle: "short" }).format(timestamp); };
const sessionTime = (session) => { const age = Date.now() - Number(session.updatedAt || 0); return !session.active && age >= 0 && age <= 3 * 60000 ? "Gerade beendet" : relative(session.updatedAt); };
const directoryLabel = (value) => { const path = String(value || "Ohne Projekt"); return value && !/[\\/]$/.test(path) ? `${path}\\` : path; };
const formatValue = (value) => { if (value === null || value === undefined || value === "") return "–"; if (typeof value === "boolean") return value ? "Ja" : "Nein"; if (typeof value === "object") return JSON.stringify(value, null, 2); return String(value); };
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
  filter: tablerIcon('<path d="M4 6h16M7 12h10M10 18h4"/>'),
  plus: tablerIcon('<path d="M12 5v14M5 12h14"/>'),
  close: tablerIcon('<path d="M6 6l12 12M18 6L6 18"/>')
};
const theme = localStorage.getItem("codex-manager-theme") || "light";
document.documentElement.dataset.theme = theme;

function header() {
  const autostart = companionInfo.supportsAutostart ? `<button class="quiet-button" id="autostart-toggle">Autostart: ${companionInfo.autostartEnabled ? "An" : "Aus"}</button>` : "";
  return `<header class="top"><a class="brand" href="/"><strong>Codex CLI Manager</strong></a><span class="live"><i class="dot"></i>Live</span><span class="spacer"></span>${autostart}<button class="quiet-button terminal-settings-toggle" id="terminal-settings-toggle">${icons.settings}<span>Terminal</span></button><button class="quiet-button" id="theme-toggle">${document.documentElement.dataset.theme === "light" ? "Dark Mode" : "Light Mode"}</button></header>`;
}

function bindTheme() {
  document.querySelector("#theme-toggle").onclick = () => { const next = document.documentElement.dataset.theme === "light" ? "dark" : "light"; document.documentElement.dataset.theme = next; localStorage.setItem("codex-manager-theme", next); document.querySelector("#theme-toggle").textContent = next === "light" ? "Dark Mode" : "Light Mode"; };
  const autostart = document.querySelector("#autostart-toggle");
  if (autostart) autostart.onclick = async () => { autostart.disabled = true; try { const result = await fetchJson(`/api/autostart/${!companionInfo.autostartEnabled}`, { method: "POST" }); companionInfo.autostartEnabled = result.enabled; autostart.textContent = `Autostart: ${result.enabled ? "An" : "Aus"}`; } finally { autostart.disabled = false; } };
  document.querySelector("#terminal-settings-toggle").onclick = openTerminalSettings;
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
    <header><div><span class="eyebrow">Lokale Einstellung</span><h2>Terminal</h2></div><button class="dialog-close" type="button" aria-label="Schließen">${icons.close}</button></header>
    <label><span>Terminal-Fenster</span><select id="terminal-choice"><option value="auto">Automatisch</option><option value="windows-terminal" ${detected.windowsTerminal.available ? "" : "disabled"}>Windows Terminal</option><option value="console">PowerShell-Konsole</option></select></label>
    <label><span>PowerShell-Version</span><select id="shell-choice"><option value="auto">Automatisch</option><option value="powershell7" ${detected.powershell7.available ? "" : "disabled"}>PowerShell 7</option><option value="windows-powershell" ${detected.windowsPowerShell.available ? "" : "disabled"}>Windows PowerShell 5.1</option></select></label>
    <div class="terminal-resolution">Aktiv: <strong>${esc(settings.resolvedTerminal)}</strong> mit <strong>${esc(settings.resolvedShell)}</strong></div>
    <div class="terminal-detected">${[detected.windowsTerminal, detected.powershell7, detected.windowsPowerShell].map(terminalDetectionRow).join("")}</div>
    <div class="terminal-settings-error" id="terminal-settings-error"></div>
    <footer><button class="secondary" type="button" data-close>Abbrechen</button><button class="primary" type="submit">Speichern</button></footer>
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
  const stateLabel = item.available ? item.version ? `Version ${item.version}` : "Gefunden" : "Nicht gefunden";
  return `<div class="terminal-detection ${item.available ? "available" : "missing"}"><i></i><div><strong>${esc(item.name)}</strong><span>${esc(stateLabel)}</span>${item.path ? `<code title="${esc(item.path)}">${esc(item.path)}</code>` : ""}</div></div>`;
}

async function fetchJson(url, options) {
  const request = { ...(options || {}) };
  request.headers = { ...(request.headers || {}) };
  if ((request.method || "GET").toUpperCase() === "POST") request.headers["X-Codex-Manager-Token"] = actionToken;
  const response = await fetch(url, request);
  const body = await response.text();
  if (!body) throw new Error(`Leere Antwort vom lokalen Companion (${response.status}). Bitte die Seite neu laden.`);
  let result;
  try { result = JSON.parse(body); }
  catch { throw new Error(`Ungültige Antwort vom lokalen Companion (${response.status}). Bitte die Seite neu laden.`); }
  if (!response.ok) throw new Error(result.error || result.detail || result.title || "Anfrage fehlgeschlagen.");
  return result.data;
}

function listPage() {
  app.innerHTML = `
    <div class="shell">
      ${header()}
      <main class="list-layout">
        <aside class="sidebar">
          <nav class="sidebar-primary" id="sidebar-primary">
            <button class="active" data-view="all"><span class="nav-icon">${icons.sessions}</span><span>Sessions</span></button>
            <button data-view="favorites"><span class="nav-icon">${icons.favorite}</span><span>Favoriten</span></button>
            <button data-view="archived"><span class="nav-icon">${icons.archive}</span><span>Archiv</span></button>
          </nav>
          <div class="rail-divider"></div>
          <div class="side-title">Projekte</div>
          <div class="project-create">
            <button class="project-create-toggle" id="project-create-toggle" type="button"><span>${icons.plus}</span>Neues Projekt</button>
            <form class="project-create-form" id="project-create-form" hidden>
              <label class="project-name-field"><input id="project-name" maxlength="64" placeholder="Projektname" autocomplete="off"><button class="input-clear project-name-clear" id="project-name-clear" type="button" aria-label="Projektname leeren" title="Projektname leeren" hidden>${icons.close}</button></label>
              <button type="submit" aria-label="Projekt anlegen">${icons.plus}</button>
            </form>
          </div>
          <nav class="projects" id="projects"></nav>
        </aside>
        <section class="main">
          <div class="searchbar">
            <label class="search">⌕<input id="search" placeholder="Session suchen" autofocus><button class="input-clear" id="search-clear" type="button" aria-label="Suche leeren" title="Suche leeren" hidden>${icons.close}</button></label>
            <details class="filter-disclosure">
              <summary><span class="filter-icon">${icons.filter}</span>Filter</summary>
              <div class="filters">
                <select id="source"><option value="all">Alle Quellen</option></select>
                <select id="internal"><option value="hide">Interne aus</option><option value="show">Interne an</option></select>
                <select id="period"><option value="0">Jeder Zeitraum</option><option value="1">24 Stunden</option><option value="7">7 Tage</option><option value="30">30 Tage</option><option value="90">90 Tage</option></select>
                <select id="pinned"><option value="all">Alle Sessions</option><option value="yes">Favoriten</option></select>
                <select id="archive"><option value="active">Aktiv</option><option value="archived">Archiv</option></select>
                <select id="sort"><option value="updated">Aktivität</option><option value="created">Erstellt</option><option value="project">Projekt</option><option value="title">Titel</option><option value="path">Speicherort</option></select>
              </div>
            </details>
          </div>
          <div class="count" id="count">Lädt …</div>
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
    document.querySelector("#count").textContent = "Sessions konnten nicht geladen werden";
    document.querySelector("#sessions").innerHTML = `<div class="empty">${esc(error.message)}<br><button class="retry-load" id="retry-load">Erneut versuchen</button></div>`;
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
  select.innerHTML = `<option value="all">Alle Quellen</option>${sources.map((source) => `<option value="${esc(source)}">${esc(source)}</option>`).join("")}`;
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
  if (value >= Date.now() - 3600000) return "Gerade eben";
  if (value >= today) return "Heute";
  if (value >= today - day) return "Gestern";
  if (value >= weekStart) return "Diese Woche";
  if (value >= weekStart - 7 * day) return "Letzte Woche";
  if (value >= monthStart) return "Dieser Monat";
  return "Älter";
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
  document.querySelector("#count").textContent = `${bundles.length} Session${bundles.length === 1 ? "" : "s"}`;
  const groups = ["Gerade eben", "Heute", "Gestern", "Diese Woche", "Letzte Woche", "Dieser Monat", "Älter"];
  document.querySelector("#sessions").innerHTML = !bundles.length ? '<div class="empty">Keine Sessions für diese Filter gefunden.</div>' : sort === "updated" ? groups.map((group) => { const items = bundles.filter((bundle) => activityGroup(bundle.updatedAt) === group); return items.length ? `<section class="session-group"><h2>${group}</h2>${items.map((bundle) => sessionRow(bundle.master, bundle.shadows)).join("")}</section>` : ""; }).join("") : bundles.map((bundle) => sessionRow(bundle.master, bundle.shadows)).join("");
  document.querySelectorAll(".session-favorite").forEach((button) => button.onclick = () => toggleFavorite(button.dataset.id, button));
  document.querySelectorAll(".session-launch").forEach((button) => button.onclick = () => openFromList(button.dataset.id, button.dataset.action, button));
  bindSessionDragging();
}

function sessionRow(session, shadows = []) {
  const action = session.desktop ? "open-desktop" : "resume"; const disabled = session.active && !session.desktop; const label = session.desktop ? "In Codex Desktop öffnen" : disabled ? "Session ist bereits geöffnet" : "Session fortsetzen";
  const source = session.desktop ? "ChatGPT Desktop" : "CLI";
  const sourceIcon = session.desktop ? icons.desktop : icons.terminal;
  const shadowList = shadows.length ? `<div class="shadow-sessions">${shadows.map(shadowSessionRow).join("")}</div>` : "";
  const favoriteLabel = session.pinned ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen";
  return `<article class="session-row ${session.active ? "active-session" : ""}" data-id="${session.id}" draggable="true"><div class="session-copy"><div class="session-context"><code title="${esc(session.cwd)}">${esc(directoryLabel(session.cwd))}</code><div class="session-meta"><time>${sessionTime(session)}</time><button class="session-favorite ${session.pinned ? "active" : ""}" data-id="${session.id}" aria-label="${favoriteLabel}" aria-pressed="${session.pinned ? "true" : "false"}" title="${favoriteLabel}">${icons.favorite}</button></div></div><div class="session-body"><div class="session-summary"><strong class="session-title" title="${esc(session.title)}">${esc(session.title)}</strong>${session.preview ? `<span class="session-prompt">${esc(session.preview)}</span>` : ""}</div><div class="session-actions"><a class="session-details" href="/session/${encodeURIComponent(session.id)}">Details</a><button class="session-launch ${session.desktop ? "desktop" : ""}" data-id="${session.id}" data-action="${action}" ${disabled ? "disabled" : ""} aria-label="${label}" title="${source}: ${label}"><span class="launch-source">${sourceIcon}</span><span class="launch-divider"></span><span class="launch-play">${icons.play}</span></button></div></div>${shadowList}</div></article>`;
}

function shadowSessionRow(session) {
  const usesChrome = /(^|\s)@Chrome\b/i.test(session.preview || ""); const sourceIcon = usesChrome ? icons.chrome : session.desktop ? icons.desktop : icons.terminal; const sourceLabel = usesChrome ? "Chrome" : session.desktop ? "Desktop" : "CLI";
  const status = session.active ? '<span class="shadow-status active"><i></i>Läuft</span>' : `<span class="shadow-status">${sessionTime(session)}</span>`;
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
  app.innerHTML = `<div class="shell">${header()}<main class="session-page"><div class="session-content"><a class="back" href="/">← Alle Sessions</a><div class="page-loading">Session wird geladen …</div></div></main></div>`; bindTheme();
  try { const session = await fetchJson(`/api/session/${encodeURIComponent(id)}`); renderSessionDetail(session); }
  catch (error) { document.querySelector(".session-content").innerHTML = `<a class="back" href="/">← Alle Sessions</a><div class="empty">${esc(error.message)}</div>`; }
}

function renderSessionDetail(session) {
  const content = document.querySelector(".session-content"); const title = session.name || session.title || session.first_user_message || "Unbenannte Session"; const description = session.preview || session.first_user_message || "";
  const summary = [["Status", session.active ? "Aktiv" : "Bereit"], ["Projekt", session.project], ["Quelle", session.threadSource || session.thread_source || session.source], ["Modell", session.model || session.modelProvider || session.model_provider], ["Branch", session.branch || session.git_branch], ["Archiviert", session.archived ? "Ja" : "Nein"]];
  const excluded = new Set(["first_user_message", "preview"]); const metadata = Object.entries(session).filter(([key]) => !excluded.has(key)).sort(([a], [b]) => a.localeCompare(b));
  content.innerHTML = `<a class="back" href="/">← Sessions</a><section class="session-head"><div><span class="eyebrow">Codex Session</span><h1>${esc(title)}</h1><p>${esc(description)}</p><div class="action-error" id="action-error"></div></div><div class="actions"><button class="primary" data-action="resume" ${session.active ? "disabled" : ""}>${session.active ? "Geöffnet" : "Fortsetzen"}</button><button class="secondary" data-action="open-project">VS Code</button><button class="danger" data-action="archive">Archivieren</button></div></section><section class="section"><h2>Speicherorte</h2><div class="path-grid"><div class="path-card"><span>Session-Datei</span><code>${esc(session.storagePath || session.path || session.rollout_path || session.storage_path || "–")}</code></div><div class="path-card"><span>Projektordner</span><code>${esc(session.cwd || "–")}</code></div></div></section><section class="section"><h2>Übersicht</h2><div class="summary-grid">${summary.map(([key, value]) => `<div class="summary-item"><span>${esc(key)}</span><strong>${esc(formatValue(value))}</strong></div>`).join("")}</div></section><details class="metadata-disclosure"><summary>Technische Metadaten</summary><div class="metadata">${metadata.map(([key, value]) => `<div class="metadata-row"><div class="metadata-key">${esc(key)}</div><div class="metadata-value"><pre>${esc(formatValue(value))}</pre></div></div>`).join("")}</div></details>`;
  if (session.desktop) { const desktopButton = content.querySelector('[data-action="resume"]'); desktopButton.dataset.action = "open-desktop"; desktopButton.disabled = false; desktopButton.textContent = "In Desktop öffnen"; }
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
    const match = window.location.pathname.match(/^\/session\/([A-Za-z0-9_-]+)$/);
    if (match) sessionPage(match[1]); else listPage();
  } catch (error) {
    app.innerHTML = `<div class="empty">Verbindung zum lokalen Companion wird hergestellt …</div>`;
    window.setTimeout(initialize, 1000);
  }
}

initialize();
