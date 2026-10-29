const app = document.querySelector("#app");
const state = { sessions: [], project: "Alle Sessions", view: "all" };
let actionToken = "";
let companionInfo = { supportsAutostart: false, autostartEnabled: false };
const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
const relative = (timestamp) => { const ms = Date.now() - timestamp; if (ms < 60000) return "gerade eben"; const minutes = Math.round(ms / 60000); if (minutes < 60) return `vor ${minutes} Min.`; const hours = Math.round(minutes / 60); if (hours < 24) return `vor ${hours} Std.`; const days = Math.round(hours / 24); if (days === 1) return "gestern"; if (days < 14) return `vor ${days} Tagen`; return new Intl.DateTimeFormat("de-DE", { dateStyle: "medium", timeStyle: "short" }).format(timestamp); };
const formatValue = (value) => { if (value === null || value === undefined || value === "") return "–"; if (typeof value === "boolean") return value ? "Ja" : "Nein"; if (typeof value === "object") return JSON.stringify(value, null, 2); return String(value); };
const tablerIcon = (paths) => `<svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;
const icons = {
  sessions: tablerIcon('<path d="M9 6h11M9 12h11M9 18h11"/><path d="M5 6v.01M5 12v.01M5 18v.01"/>'),
  favorite: tablerIcon('<path d="M12 17.75l-6.172 3.245 1.179-6.873-5-4.867 6.9-1.002L12 2l3.086 6.253 6.9 1.002-4.993 4.867 1.179 6.873z"/>'),
  archive: tablerIcon('<path d="M3 6h18M5 6v14h14V6M9 10h6"/><path d="M4 3h16v3H4z"/>'),
  play: tablerIcon('<path d="M7 4v16l13-8z"/>'),
  desktop: tablerIcon('<path d="M3 4h18v12H3zM7 20h10M9 16v4M15 16v4"/>')
};
const theme = localStorage.getItem("codex-manager-theme") || "light";
document.documentElement.dataset.theme = theme;

function header() {
  const autostart = companionInfo.supportsAutostart ? `<button class="quiet-button" id="autostart-toggle">Autostart: ${companionInfo.autostartEnabled ? "An" : "Aus"}</button>` : "";
  return `<header class="top"><a class="brand" href="/"><span class="mark">⌘</span><strong>Codex Sessions</strong></a><span class="live"><i class="dot"></i>Lokaler Codex-State</span><span class="spacer"></span>${autostart}<button class="quiet-button" id="theme-toggle">${document.documentElement.dataset.theme === "light" ? "Dark Mode" : "Light Mode"}</button></header>`;
}

function bindTheme() {
  document.querySelector("#theme-toggle").onclick = () => { const next = document.documentElement.dataset.theme === "light" ? "dark" : "light"; document.documentElement.dataset.theme = next; localStorage.setItem("codex-manager-theme", next); document.querySelector("#theme-toggle").textContent = next === "light" ? "Dark Mode" : "Light Mode"; };
  const autostart = document.querySelector("#autostart-toggle");
  if (autostart) autostart.onclick = async () => { autostart.disabled = true; try { const result = await fetchJson(`/api/autostart/${!companionInfo.autostartEnabled}`, { method: "POST" }); companionInfo.autostartEnabled = result.enabled; autostart.textContent = `Autostart: ${result.enabled ? "An" : "Aus"}`; } finally { autostart.disabled = false; } };
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
  app.innerHTML = `<div class="shell">${header()}<main class="list-layout"><aside class="sidebar"><nav class="sidebar-primary" id="sidebar-primary"><button class="active" data-view="all"><span class="nav-icon">${icons.sessions}</span><span>Alle Sessions</span></button><button data-view="favorites"><span class="nav-icon">${icons.favorite}</span><span>Favoriten</span></button><button data-view="archived"><span class="nav-icon">${icons.archive}</span><span>Archiv</span></button></nav><div class="rail-divider"></div><div class="side-title">Projekte</div><nav class="projects" id="projects"></nav><div class="side-note">Codex Sessions auf diesem Computer</div></aside><section class="main"><div class="searchbar"><label class="search">⌕<input id="search" placeholder="Titel, Projekt, Branch oder Pfad durchsuchen …" autofocus></label><button class="refresh" id="refresh">↻</button></div><div class="filters"><select id="source"><option value="all">Alle Quellen</option></select><select id="internal"><option value="hide">Codex-interne ausgeblendet</option><option value="show">Codex-interne anzeigen</option></select><select id="period"><option value="0">Jeder Zeitraum</option><option value="1">Letzte 24 Stunden</option><option value="7">Letzte 7 Tage</option><option value="30">Letzte 30 Tage</option><option value="90">Letzte 90 Tage</option></select><select id="pinned"><option value="all">Alle Sessions</option><option value="yes">Nur Favoriten</option></select><select id="archive"><option value="active">Aktiv</option><option value="archived">Archiviert</option></select><select id="sort"><option value="updated">Zuletzt verwendet</option><option value="created">Erstellt</option><option value="project">Projekt</option><option value="title">Titel</option><option value="path">Speicherort</option></select></div><div class="count" id="count">Sessions werden geladen …</div><div class="sessions" id="sessions"></div></section></main></div>`;
  bindTheme();
  document.querySelector("#search").oninput = renderSessions;
  for (const id of ["source", "period", "sort"]) document.querySelector(`#${id}`).onchange = renderSessions;
  document.querySelector("#pinned").onchange = () => { state.view = document.querySelector("#pinned").value === "yes" ? "favorites" : document.querySelector("#archive").value === "archived" ? "archived" : "all"; renderSidebarNav(); renderSessions(); };
  document.querySelector("#internal").onchange = () => { state.project = "Alle Sessions"; renderSources(); renderProjects(); renderSessions(); };
  document.querySelectorAll("#sidebar-primary button").forEach((button) => button.onclick = () => selectView(button.dataset.view));
  document.querySelector("#archive").onchange = () => { state.view = document.querySelector("#archive").value === "archived" ? "archived" : document.querySelector("#pinned").value === "yes" ? "favorites" : "all"; loadSessions(); };
  document.querySelector("#refresh").onclick = loadSessions;
  loadSessions();
}

async function loadSessions() {
  try {
    const archived = document.querySelector("#archive").value === "archived";
    state.sessions = await fetchJson(`/api/sessions?archived=${archived ? "true" : "false"}`);
    state.project = "Alle Sessions";
    renderSidebarNav(); renderSources(); renderProjects(); renderSessions();
  } catch (error) {
    document.querySelector("#count").textContent = "Sessions konnten nicht geladen werden";
    document.querySelector("#sessions").innerHTML = `<div class="empty">${esc(error.message)}<br><button class="refresh" onclick="location.reload()">Erneut versuchen</button></div>`;
  }
}

function renderSources() {
  const select = document.querySelector("#source"); const selected = select.value;
  const sources = [...new Set(sessionsWithoutInternalFilter().map((session) => session.source).filter(Boolean))].sort();
  select.innerHTML = `<option value="all">Alle Quellen</option>${sources.map((source) => `<option value="${esc(source)}">${esc(source)}</option>`).join("")}`;
  select.value = sources.includes(selected) ? selected : "all";
}

function renderProjects() {
  const visible = sessionsWithoutInternalFilter();
  const stats = new Map();
  visible.forEach((session) => { const current = stats.get(session.project) || { count: 0, updatedAt: 0 }; current.count += 1; current.updatedAt = Math.max(current.updatedAt, Number(session.updatedAt) || 0); stats.set(session.project, current); });
  const entries = [...stats].map(([name, value]) => ({ name, ...value })).sort((a, b) => b.updatedAt - a.updatedAt || a.name.localeCompare(b.name));
  const projects = document.querySelector("#projects");
  projects.innerHTML = entries.map((entry) => projectButton(entry.name)).join("");
  projects.querySelectorAll("button").forEach((button) => button.onclick = () => { state.project = button.dataset.project; state.view = "project"; renderSidebarNav(); renderProjects(); renderSessions(); });
}

function projectButton(name) {
  return `<button class="${state.project === name ? "active" : ""}" data-project="${esc(name)}"><span class="project-dot"></span><span>${esc(name)}</span></button>`;
}

function renderSidebarNav() {
  document.querySelectorAll("#sidebar-primary button").forEach((button) => button.classList.toggle("active", button.dataset.view === state.view));
}

function selectView(view) {
  state.view = view; state.project = "Alle Sessions";
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
  return sessionsWithoutInternalFilter().filter((session) => (state.project === "Alle Sessions" || session.project === state.project) && (source === "all" || session.source === source) && (pinned === "all" || session.pinned) && (!cutoff || session.updatedAt >= cutoff) && (!query || `${session.title} ${session.preview} ${session.project} ${session.branch || ""} ${session.cwd} ${session.storagePath || ""}`.toLowerCase().includes(query))).sort((a, b) => sort === "created" ? b.createdAt - a.createdAt : sort === "project" ? a.project.localeCompare(b.project) : sort === "title" ? a.title.localeCompare(b.title) : sort === "path" ? (a.storagePath || "").localeCompare(b.storagePath || "") : b.updatedAt - a.updatedAt);
}

function renderSessions() {
  const sessions = filteredSessions(); const sort = document.querySelector("#sort").value;
  document.querySelector("#count").textContent = `${sessions.length} Session${sessions.length === 1 ? "" : "s"}${sort === "updated" ? " · nach Aktivität gruppiert" : ""}`;
  const groups = ["Gerade eben", "Heute", "Gestern", "Diese Woche", "Letzte Woche", "Dieser Monat", "Älter"];
  document.querySelector("#sessions").innerHTML = !sessions.length ? '<div class="empty">Keine Sessions für diese Filter gefunden.</div>' : sort === "updated" ? groups.map((group) => { const items = sessions.filter((session) => activityGroup(session.updatedAt) === group); return items.length ? `<section class="session-group"><h2>${group}</h2>${items.map(sessionRow).join("")}</section>` : ""; }).join("") : sessions.map(sessionRow).join("");
  document.querySelectorAll(".session-row").forEach((row) => { row.onclick = () => { window.location.href = `/session/${row.dataset.id}`; }; row.onkeydown = (event) => { if (event.key === "Enter") row.click(); }; });
  document.querySelectorAll(".session-play").forEach((button) => button.onclick = (event) => { event.stopPropagation(); openFromList(button.dataset.id, button.dataset.action, button); });
}

function sessionRow(session) {
  const action = session.desktop ? "open-desktop" : "resume"; const disabled = session.active && !session.desktop; const label = session.desktop ? "In Codex Desktop öffnen" : disabled ? "Session ist bereits geöffnet" : "Session fortsetzen";
  return `<div class="session-row ${session.active ? "active-session" : ""}" data-id="${session.id}" role="link" tabindex="0"><i class="state"></i><span class="session-copy"><span class="session-line">${session.pinned ? '<span class="pin">★</span>' : ""}<strong>${esc(session.title)}</strong>${session.active ? '<span class="active-label">Aktiv</span>' : ""}${session.desktop ? '<span class="desktop-label">Desktop</span>' : ""}<time>${relative(session.updatedAt)}</time></span><span class="preview">${esc(session.preview || "Keine Vorschau")}</span><span class="meta"><span>${esc(session.project)}</span>${session.branch ? `<span>Branch: ${esc(session.branch)}</span>` : ""}<span>${esc(session.source)}</span></span><span class="locations"><span class="location"><b>Projektordner</b><code>${esc(session.cwd || "–")}</code></span><span class="location"><b>Session-Datei</b><code>${esc(session.storagePath || "–")}</code></span></span></span><button class="session-play ${session.desktop ? "desktop" : ""}" data-id="${session.id}" data-action="${action}" ${disabled ? "disabled" : ""} aria-label="${label}" title="${label}">${session.desktop ? icons.desktop : icons.play}</button></div>`;
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
  const summary = [["Status", session.active ? "Aktiv" : "Nicht geöffnet"], ["Session-ID", session.id], ["Projekt", session.project], ["Quelle", session.threadSource || session.thread_source || session.source], ["Modell", session.model || session.modelProvider || session.model_provider], ["Reasoning", session.reasoningEffort || session.reasoning_effort], ["Git-Branch", session.branch || session.git_branch], ["CLI-Version", session.cliVersion || session.cli_version], ["Tokens", session.tokens_used], ["Archiviert", session.archived ? "Ja" : "Nein"]];
  const excluded = new Set(["first_user_message", "preview"]); const metadata = Object.entries(session).filter(([key]) => !excluded.has(key)).sort(([a], [b]) => a.localeCompare(b));
  content.innerHTML = `<a class="back" href="/">← Alle Sessions</a><section class="session-head"><div><span class="eyebrow">Codex Session</span><h1>${esc(title)}</h1><p>${esc(description)}</p><div class="action-error" id="action-error"></div></div><div class="actions"><button class="primary" data-action="resume" ${session.active ? "disabled" : ""}>${session.active ? "Bereits geöffnet" : "Fortsetzen"}</button><button class="secondary" data-action="open-project">VS Code</button><button class="danger" data-action="archive">Archivieren</button></div></section><section class="section"><h2>Speicherorte</h2><div class="path-grid"><div class="path-card"><span>Codex Session-Datei</span><code>${esc(session.storagePath || session.path || session.rollout_path || session.storage_path || "–")}</code></div><div class="path-card"><span>Projektordner</span><code>${esc(session.cwd || "–")}</code></div></div></section><section class="section"><h2>Übersicht</h2><div class="summary-grid">${summary.map(([key, value]) => `<div class="summary-item"><span>${esc(key)}</span><strong>${esc(formatValue(value))}</strong></div>`).join("")}</div></section>${session.first_user_message ? `<section class="section"><h2>Erste Nachricht</h2><pre class="first-message">${esc(session.first_user_message)}</pre></section>` : ""}<section class="section"><h2>Alle Metadaten</h2><div class="metadata">${metadata.map(([key, value]) => `<div class="metadata-row"><div class="metadata-key">${esc(key)}</div><div class="metadata-value"><pre>${esc(formatValue(value))}</pre></div></div>`).join("")}</div></section>`;
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
    app.innerHTML = `<div class="empty">Der lokale Companion ist nicht erreichbar: ${esc(error.message)}</div>`;
  }
}

initialize();
