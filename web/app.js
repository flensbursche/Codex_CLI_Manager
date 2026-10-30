const app = document.querySelector("#app");
const state = { sessions: [], projects: [], project: null, view: "all" };
let actionToken = "";
let companionInfo = { supportsAutostart: false, autostartEnabled: false };
let language = "de";
let sessionEvents;
let sidebarCollapsed = localStorage.getItem("codex-sidebar-collapsed") === "true";
let menuDismissBound = false;
const languageLocales = { de: "de-DE", en: "en-GB", da: "da-DK" };
let messages = {};
const loadMessages = async (code) => {
  const response = await fetch(`/locales/${encodeURIComponent(code)}.json`, { cache: "no-store" });
  if (!response.ok) throw new Error(`Could not load locale ${code}.`);
  messages = await response.json();
  language = code;
  document.documentElement.lang = code;
};
const t = (key, ...values) => { const template = messages[key] ?? key; return values.reduce((text, value, index) => text.replaceAll(`{${index}}`, value), template); };
const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
const highlight = (value, query) => {
  const text = String(value ?? ""); const needle = String(query ?? "").trim();
  if (!needle) return esc(text);
  const lowerText = text.toLocaleLowerCase(languageLocales[language]); const lowerNeedle = needle.toLocaleLowerCase(languageLocales[language]);
  let result = ""; let offset = 0; let index = lowerText.indexOf(lowerNeedle);
  while (index >= 0) {
    result += `${esc(text.slice(offset, index))}<mark class="search-match">${esc(text.slice(index, index + needle.length))}</mark>`;
    offset = index + needle.length; index = lowerText.indexOf(lowerNeedle, offset);
  }
  return result + esc(text.slice(offset));
};
const relative = (timestamp) => { const ms = Date.now() - timestamp; if (ms < 60000) return t("justNow"); const minutes = Math.round(ms / 60000); if (minutes < 60) return t("minutesAgo", minutes); const hours = Math.round(minutes / 60); if (hours < 24) return t("hoursAgo", hours); const days = Math.round(hours / 24); if (days === 1) return t("yesterday"); if (days < 14) return t("daysAgo", days); return new Intl.DateTimeFormat(languageLocales[language], { dateStyle: "medium", timeStyle: "short" }).format(timestamp); };
const sessionTime = (session) => { const age = Date.now() - Number(session.updatedAt || 0); return !session.active && age >= 0 && age <= 3 * 60000 ? t("justEnded") : relative(session.updatedAt); };
const directoryLabel = (value) => { const path = String(value || t("noProject")); return value && !/[\\/]$/.test(path) ? `${path}\\` : path; };
const gitStateLabel = (git) => git.conflicts ? t("gitConflicts", git.conflicts) : git.dirty ? t("gitDirty", git.changedFiles) : t("gitClean");
const gitSummary = (git) => git?.linked ? [git.branch, gitStateLabel(git)].filter(Boolean).join(" · ") : t("notGitRepository");
const formatValue = (value) => { if (value === null || value === undefined || value === "") return "–"; if (typeof value === "boolean") return value ? t("yes") : t("no"); if (typeof value === "object") return JSON.stringify(value, null, 2); return String(value); };
const tablerIcon = (paths) => `<svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;
const brandIcon = (path) => `<svg viewBox="0 0 24 24" width="19" height="19" fill="currentColor" aria-hidden="true"><path d="${path}"/></svg>`;
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
  close: tablerIcon('<path d="M6 6l12 12M18 6L6 18"/>'),
  menu: tablerIcon('<path d="M4 6h16M4 12h16M4 18h16"/>'),
  sidebarClose: tablerIcon('<path d="M4 5h16v14H4zM9 5v14M14 9l-3 3 3 3"/>'),
  sidebarOpen: tablerIcon('<path d="M4 5h16v14H4zM9 5v14M11 9l3 3-3 3"/>'),
  copy: tablerIcon('<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>'),
  edit: tablerIcon('<path d="M4 20h4l10.5-10.5a2.8 2.8 0 0 0-4-4L4 16v4zM13.5 6.5l4 4"/>'),
  unlink: tablerIcon('<path d="M10 13a5 5 0 0 0 7.07.07l2-2a5 5 0 0 0-7.07-7.07l-1.15 1.15"/><path d="M14 11a5 5 0 0 0-7.07-.07l-2 2A5 5 0 0 0 12 20l1.15-1.15"/><path d="M4 4l16 16"/>'),
  git: brandIcon('M23.546 10.93L13.067.452a1.55 1.55 0 0 0-2.188 0L8.708 2.625l2.76 2.76a1.84 1.84 0 0 1 2.327 2.34l2.658 2.66a1.84 1.84 0 1 1-1.104 1.044l-2.477-2.478v6.525a1.84 1.84 0 1 1-1.525-.05V8.835a1.84 1.84 0 0 1-.992-2.402L7.638 3.716.453 10.93a1.55 1.55 0 0 0 0 2.188l10.477 10.43a1.55 1.55 0 0 0 2.186 0l10.43-10.43a1.55 1.55 0 0 0 0-2.188z'),
  github: brandIcon('M12 .297C5.37.297 0 5.67 0 12.297c0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.418-1.305.762-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297 24 5.67 18.627.297 12 .297z'),
  gitlab: brandIcon('M23.955 13.587l-1.342-4.135-2.66-8.19a.455.455 0 0 0-.867 0l-2.66 8.19H7.574l-2.66-8.19a.455.455 0 0 0-.867 0l-2.66 8.19L.045 13.587a.924.924 0 0 0 .331 1.023L12 23.783l11.625-9.173a.924.924 0 0 0 .33-1.023z'),
  history: tablerIcon('<path d="M12 8v4l2 2"/><path d="M3.05 11a9 9 0 1 0 .5-3M3 4v4h4"/>'),
  info: tablerIcon('<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8v.01"/>'),
  share: tablerIcon('<circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><path d="M8.6 10.5l6.8-4M8.6 13.5l6.8 4"/>'),
  refresh: tablerIcon('<path d="M20 11a8 8 0 1 0-2.34 5.66M20 4v7h-7"/>')
};
const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");
let themePreference = localStorage.getItem("codex-manager-theme") || "auto";
const applyThemePreference = () => { document.documentElement.dataset.theme = themePreference === "auto" ? colorScheme.matches ? "dark" : "light" : themePreference; };
applyThemePreference();
colorScheme.addEventListener("change", () => { if (themePreference === "auto") applyThemePreference(); });

function header() {
  const autostart = companionInfo.supportsAutostart ? `<button class="menu-row switch-row" id="autostart-toggle" role="switch" aria-checked="${companionInfo.autostartEnabled}"><span>${t("autostart")}</span><i class="toggle-switch"><b></b></i></button>` : "";
  const darkMode = document.documentElement.dataset.theme === "dark";
  return `<header class="top"><a class="brand" href="/"><span class="mark"><img src="/app-icon.svg?v=5" alt=""></span><strong>Codex CLI Manager</strong></a><span class="live"><i class="dot"></i>${t("live")}</span><span class="spacer"></span><details class="header-menu"><summary class="quiet-button icon-button" aria-label="${t("menu")}" title="${t("menu")}">${icons.menu}</summary><div class="header-menu-panel">${autostart}<button class="menu-row switch-row" id="theme-toggle" role="switch" aria-checked="${darkMode}">${darkMode ? icons.moon : icons.sun}<span>${t("darkMode")}</span><i class="toggle-switch"><b></b></i></button><div class="menu-block"><span class="menu-label">${icons.globe}${t("language")}</span><div class="language-options" role="group" aria-label="${t("language")}">${["de", "en", "da"].map((code) => `<button type="button" data-language="${code}" class="${language === code ? "active" : ""}" aria-pressed="${language === code}">${code === "da" ? "DK" : code.toUpperCase()}</button>`).join("")}</div></div><a class="menu-row" href="/settings/terminal">${icons.settings}<span>${t("terminalSettings")}</span></a><a class="menu-row" href="/about">${icons.info}<span>${esc(t("aboutAndHistory", `v${companionInfo.version || ""}`))}</span></a><button class="menu-row" id="share-repository" type="button">${icons.share}<span>${t("shareRepository")}</span></button><div class="menu-update" id="menu-update"><span>${icons.refresh}<span>${t("updateChecking")}</span></span><button type="button" id="update-check">${t("checkNow")}</button></div></div></details></header>`;
}

function bindTheme() {
  if (!menuDismissBound) {
    document.addEventListener("pointerdown", (event) => document.querySelectorAll(".header-menu[open]").forEach((menu) => { if (!menu.contains(event.target)) menu.removeAttribute("open"); }));
    document.addEventListener("keydown", (event) => { if (event.key === "Escape") document.querySelectorAll(".header-menu[open]").forEach((menu) => menu.removeAttribute("open")); });
    menuDismissBound = true;
  }
  document.querySelector("#theme-toggle").onclick = (event) => { const next = document.documentElement.dataset.theme === "light" ? "dark" : "light"; themePreference = next; localStorage.setItem("codex-manager-theme", next); applyThemePreference(); event.currentTarget.setAttribute("aria-checked", String(next === "dark")); event.currentTarget.querySelector("svg")?.replaceWith(svgNode(next === "dark" ? icons.moon : icons.sun)); };
  const autostart = document.querySelector("#autostart-toggle");
  if (autostart) autostart.onclick = async () => { autostart.disabled = true; try { const result = await fetchJson(`/api/autostart/${!companionInfo.autostartEnabled}`, { method: "POST" }); companionInfo.autostartEnabled = result.enabled; autostart.setAttribute("aria-checked", String(result.enabled)); } finally { autostart.disabled = false; } };
  document.querySelectorAll("[data-language]").forEach((button) => button.onclick = changeLanguage);
  const shareRepository = document.querySelector("#share-repository");
  if (shareRepository) shareRepository.onclick = async () => {
    const url = "https://github.com/flensbursche/Codex_CLI_Manager";
    try {
      if (navigator.share) await navigator.share({ title: "Codex CLI Manager", text: t("aboutDescription"), url });
      else { await navigator.clipboard.writeText(url); shareRepository.querySelector("span").textContent = t("repoLinkCopied"); window.setTimeout(() => { const label = shareRepository.querySelector("span"); if (label) label.textContent = t("shareRepository"); }, 1800); }
    } catch (error) { if (error.name !== "AbortError") window.prompt(t("copyPathFallback"), url); }
  };
  document.querySelector("#update-check").onclick = () => loadUpdateStatus(true);
  void loadUpdateStatus(false);
  const sidebarToggle = document.querySelector("#sidebar-toggle");
  if (sidebarToggle) sidebarToggle.onclick = () => { sidebarCollapsed = !sidebarCollapsed; localStorage.setItem("codex-sidebar-collapsed", String(sidebarCollapsed)); document.querySelector(".list-layout")?.classList.toggle("sidebar-collapsed", sidebarCollapsed); const label = sidebarCollapsed ? t("expandSidebar") : t("collapseSidebar"); sidebarToggle.innerHTML = `${sidebarCollapsed ? icons.sidebarOpen : icons.sidebarClose}<span>${label}</span>`; sidebarToggle.setAttribute("aria-label", label); sidebarToggle.title = label; };
}

function svgNode(markup) {
  const template = document.createElement("template"); template.innerHTML = markup.trim(); return template.content.firstElementChild;
}

async function loadUpdateStatus(refresh) {
  const box = document.querySelector("#menu-update"); if (!box) return;
  const button = box.querySelector("button"); const label = box.querySelector("span span"); button.disabled = true; label.textContent = t("updateChecking");
  try {
    const result = await fetchJson(`/api/update-status?refresh=${refresh ? "true" : "false"}`);
    label.textContent = result.status === "available" ? t("updateAvailable", result.version) : result.status === "error" ? t("updateCheckFailed") : t("upToDate", result.currentVersion);
    box.classList.toggle("available", result.status === "available");
    box.title = result.error || "";
  } catch (error) { label.textContent = t("updateCheckFailed"); box.title = error.message; }
  finally { button.disabled = false; }
}

async function changeLanguage(event) {
  const button = event.currentTarget; const buttons = document.querySelectorAll("[data-language]"); buttons.forEach((item) => item.disabled = true);
  try {
    const result = await fetchJson("/api/language", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ language: button.dataset.language }) });
    await loadMessages(result.language);
    renderCurrentRoute();
  } catch (error) { buttons.forEach((item) => item.disabled = false); window.alert(error.message); }
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

async function terminalSettingsPage() {
  app.innerHTML = `<div class="shell">${header()}<main class="session-page"><div class="settings-content"><a class="back" href="/">${t("backSessions")}</a><div class="page-loading">${t("loading")}</div></div></main></div>`;
  bindTheme();
  try { renderTerminalSettingsPage(await fetchJson("/api/terminal-settings")); }
  catch (error) { document.querySelector(".settings-content").innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="empty">${esc(error.message)}</div>`; }
}

function renderTerminalSettingsPage(settings) {
  const detected = settings.detected; const content = document.querySelector(".settings-content");
  content.innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="settings-heading"><span class="eyebrow">${t("localSetting")}</span><h1>${t("terminalSettings")}</h1><p>${t("terminalSettingsDescription")}</p></div><form class="terminal-settings-form settings-form-page"><label><span>${t("terminalWindow")}</span><select id="terminal-choice"><option value="auto">${t("automatic")}</option><option value="windows-terminal" ${detected.windowsTerminal.available ? "" : "disabled"}>Windows Terminal</option><option value="console">${t("powershellConsole")}</option></select></label><label><span>${t("powershellVersion")}</span><select id="shell-choice"><option value="auto">${t("automatic")}</option><option value="powershell7" ${detected.powershell7.available ? "" : "disabled"}>PowerShell 7</option><option value="windows-powershell" ${detected.windowsPowerShell.available ? "" : "disabled"}>Windows PowerShell 5.1</option></select></label><div class="terminal-resolution">${t("activeWith", `<strong>${esc(settings.resolvedTerminal)}</strong>`, `<strong>${esc(settings.resolvedShell)}</strong>`)}</div><div class="terminal-detected">${[detected.windowsTerminal, detected.powershell7, detected.windowsPowerShell].map(terminalDetectionRow).join("")}</div><div class="terminal-settings-error" id="terminal-settings-error"></div><footer><span class="settings-saved" id="settings-saved"></span><button class="primary" type="submit">${t("save")}</button></footer></form>`;
  const form = content.querySelector("form"); const terminalChoice = content.querySelector("#terminal-choice"); const shellChoice = content.querySelector("#shell-choice");
  terminalChoice.value = settings.terminal; shellChoice.value = settings.shell;
  form.onsubmit = async (event) => {
    event.preventDefault(); const errorBox = content.querySelector("#terminal-settings-error"); const saved = content.querySelector("#settings-saved"); const submit = form.querySelector('[type="submit"]'); errorBox.textContent = ""; saved.textContent = ""; submit.disabled = true;
    try { await fetchJson("/api/terminal-settings", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ terminal: terminalChoice.value, shell: shellChoice.value }) }); saved.textContent = t("settingsSaved"); }
    catch (error) { errorBox.textContent = error.message; }
    finally { submit.disabled = false; }
  };
}

async function aboutPage() {
  app.innerHTML = `<div class="shell">${header()}<main class="session-page"><div class="history-content"><a class="back" href="/">${t("backSessions")}</a><div class="page-loading">${t("loading")}</div></div></main></div>`;
  bindTheme();
  try {
    const [historyResponse, roadmapResponse] = await Promise.all([
      fetch("/version-history.json", { cache: "no-store" }),
      fetch("/roadmap.json", { cache: "no-store" })
    ]);
    if (!historyResponse.ok || !roadmapResponse.ok) throw new Error(t("historyLoadFailed"));
    renderAbout(await historyResponse.json(), await roadmapResponse.json());
  } catch (error) {
    document.querySelector(".history-content").innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="empty">${esc(error.message)}</div>`;
  }
}

function renderAbout(history, roadmap) {
  const content = document.querySelector(".history-content");
  const date = (value) => value ? new Intl.DateTimeFormat(languageLocales[language], { dateStyle: "medium" }).format(new Date(value)) : "";
  const localized = (value) => value?.[language] || value?.en || "";
  const statusLabel = (status) => t(status === "done" ? "roadmapDone" : status === "next" ? "roadmapNext" : "roadmapPlanned");
  const roadmapItems = (roadmap.items || []).map((item) => `<article class="roadmap-item ${esc(item.status)}"><header><span>${esc(statusLabel(item.status))}</span><code>${esc(item.id)}</code></header><h3>${esc(localized(item.title))}</h3><p>${esc(localized(item.description))}</p></article>`).join("");
  const releases = (history.releases || []).map((release) => `<section class="history-release ${release.released ? "" : "unreleased"}"><header><div><span>${release.released ? t("version", esc(release.version)) : t("unreleased")}</span><time>${date(release.date)}</time></div><code>${esc(String(release.commit || "").slice(0, 7))}</code></header><ol>${(release.commits || []).map((commit) => `<li><a href="https://github.com/flensbursche/Codex_CLI_Manager/commit/${encodeURIComponent(commit.hash)}" target="_blank" rel="noreferrer"><code>${esc(commit.shortHash)}</code><span>${esc(commit.subject)}</span><time>${date(commit.date)}</time></a></li>`).join("")}</ol></section>`).join("");
  content.innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="about-heading"><div><span class="eyebrow">Codex CLI Manager</span><h1>${t("about")}</h1><p>${t("aboutDescription")}</p></div><div class="about-version"><span>${t("currentVersion")}</span><strong>${esc(companionInfo.version || "")}</strong></div></div><div class="about-links"><a href="https://github.com/flensbursche/Codex_CLI_Manager" target="_blank" rel="noreferrer">${icons.github}<span>${t("openRepository")}</span></a></div><section class="roadmap-section"><header><div><span class="eyebrow">${esc(roadmap.version || "1.0")}</span><h2>${t("roadmap")}</h2></div><p>${t("roadmapDescription")}</p></header><div class="roadmap-grid">${roadmapItems}</div></section><section class="history-section"><header><h2>${t("versionHistory")}</h2><p>${t("versionHistoryDescription")}</p></header><div class="history-list">${releases}</div></section>`;
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
      <main class="list-layout ${sidebarCollapsed ? "sidebar-collapsed" : ""}">
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
          <div class="project-unassign-drop" id="project-unassign-drop" hidden>${icons.unlink}<span>${t("removeFromProject")}</span></div>
          <footer class="sidebar-footer"><button type="button" id="sidebar-toggle" aria-label="${sidebarCollapsed ? t("expandSidebar") : t("collapseSidebar")}" title="${sidebarCollapsed ? t("expandSidebar") : t("collapseSidebar")}">${sidebarCollapsed ? icons.sidebarOpen : icons.sidebarClose}<span>${sidebarCollapsed ? t("expandSidebar") : t("collapseSidebar")}</span></button></footer>
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
          <div class="list-status" id="list-status" hidden></div>
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
  catch (error) { showListError(error.message); }
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
    showListError(t("sessionsLoadFailed"));
    document.querySelector("#sessions").innerHTML = `<div class="empty">${esc(error.message)}<br><button class="retry-load" id="retry-load">${t("retry")}</button></div>`;
    document.querySelector("#retry-load").onclick = () => loadSessions();
  }
}

function showListError(message) {
  const status = document.querySelector("#list-status");
  if (!status) return;
  status.hidden = !message;
  status.textContent = message || "";
}

function connectSessionEvents() {
  sessionEvents?.close();
  sessionEvents = new EventSource("/api/events");
  sessionEvents.addEventListener("sessions", () => {
    if (document.querySelector("#sessions")) void loadSessions({ preserveProject: true, silent: true });
  });
  sessionEvents.onopen = async () => {
    document.querySelector(".live")?.classList.add("connected");
    try {
      const latest = await fetchJson("/api/bootstrap");
      if (companionInfo.version && latest.version && latest.version !== companionInfo.version) {
        window.location.reload();
      }
    } catch {
      // A temporary bootstrap failure must not interrupt the SSE connection.
    }
  };
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
  const unassign = document.querySelector("#project-unassign-drop");
  unassign.ondragover = (event) => { event.preventDefault(); unassign.classList.add("drop-target"); };
  unassign.ondragleave = () => unassign.classList.remove("drop-target");
  unassign.ondrop = (event) => { event.preventDefault(); unassign.classList.remove("drop-target"); unassign.hidden = true; const threadId = event.dataTransfer.getData("text/codex-thread"); if (threadId) void removeSessionFromProject(threadId); };
  document.querySelectorAll("article.session-row").forEach((row) => {
    row.ondragstart = (event) => { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/codex-thread", row.dataset.id); row.classList.add("dragging"); unassign.hidden = !state.sessions.find((session) => session.id === row.dataset.id)?.projectId; };
    row.ondragend = () => { row.classList.remove("dragging"); unassign.hidden = true; unassign.classList.remove("drop-target"); document.querySelectorAll(".projects .drop-target").forEach((target) => target.classList.remove("drop-target")); };
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
  return sessionsWithoutInternalFilter().filter((session) => (!state.project || session.projectId === state.project || (session.parentId && byId.get(session.parentId)?.projectId === state.project)) && (source === "all" || session.source === source) && (pinned === "all" || session.pinned) && (!cutoff || session.updatedAt >= cutoff) && (!query || `${session.title} ${session.originalTitle || ""} ${session.preview} ${session.project} ${session.branch || ""} ${session.git?.branch || ""} ${session.git?.lastCommitSubject || ""} ${session.cwd} ${session.storagePath || ""}`.toLowerCase().includes(query))).sort((a, b) => sort === "created" ? b.createdAt - a.createdAt : sort === "project" ? a.project.localeCompare(b.project) : sort === "title" ? a.title.localeCompare(b.title) : sort === "path" ? (a.storagePath || "").localeCompare(b.storagePath || "") : b.updatedAt - a.updatedAt);
}

function sessionBundles(sessions) {
  const ids = new Set(sessions.map((session) => session.id)); const shadows = new Map();
  sessions.forEach((session) => { if (session.parentId && ids.has(session.parentId)) { const list = shadows.get(session.parentId) || []; list.push(session); shadows.set(session.parentId, list); } });
  return sessions.filter((session) => !session.parentId || !ids.has(session.parentId)).map((master) => { const children = shadows.get(master.id) || []; children.sort((a, b) => a.createdAt - b.createdAt); return { master, shadows: children, updatedAt: Math.max(master.updatedAt || 0, ...children.map((child) => child.updatedAt || 0)) }; });
}

function renderSessions() {
  const sessions = filteredSessions(); const sort = document.querySelector("#sort").value; const bundles = sessionBundles(sessions);
  const query = document.querySelector("#search").value.trim();
  if (sort === "updated") bundles.sort((a, b) => b.updatedAt - a.updatedAt);
  document.querySelector("#search").placeholder = t(bundles.length === 1 ? "sessionSearchCountOne" : "sessionSearchCountMany", bundles.length);
  showListError("");
  const groups = ["justNow", "today", "yesterdayGroup", "thisWeek", "lastWeek", "thisMonth", "older"];
  document.querySelector("#sessions").innerHTML = !bundles.length ? `<div class="empty">${t("noSessions")}</div>` : sort === "updated" ? groups.map((group) => { const items = bundles.filter((bundle) => activityGroup(bundle.updatedAt) === group); return items.length ? `<section class="session-group"><h2>${t(group)}</h2>${items.map((bundle) => sessionRow(bundle.master, bundle.shadows, query)).join("")}</section>` : ""; }).join("") : bundles.map((bundle) => sessionRow(bundle.master, bundle.shadows, query)).join("");
  document.querySelectorAll(".session-favorite").forEach((button) => button.onclick = () => toggleFavorite(button.dataset.id, button));
  document.querySelectorAll(".session-project-remove").forEach((button) => button.onclick = () => removeSessionFromProject(button.dataset.id, button));
  document.querySelectorAll(".session-launch").forEach((button) => button.onclick = () => openFromList(button.dataset.id, button.dataset.action, button));
  bindCopyButtons();
  bindSessionDragging();
}

function sessionRow(session, shadows = [], query = "") {
  const action = session.desktop ? "open-desktop" : "resume"; const disabled = session.active && !session.desktop; const label = session.desktop ? t("openDesktop") : disabled ? t("alreadyOpen") : t("resume");
  const source = session.desktop ? "ChatGPT Desktop" : "CLI";
  const sourceIcon = session.desktop ? icons.desktop : icons.terminal;
  const shadowList = shadows.length ? `<div class="shadow-sessions">${shadows.map((shadow) => shadowSessionRow(shadow, query)).join("")}</div>` : "";
  const favoriteLabel = session.pinned ? t("removeFavorite") : t("addFavorite");
  const projectRemove = session.projectId ? `<button class="session-project-remove" data-id="${session.id}" aria-label="${t("removeFromProject")}" title="${t("removeFromProject")}">${icons.unlink}</button>` : "";
  const utility = `<div class="session-utility">${projectRemove}<button class="session-favorite ${session.pinned ? "active" : ""}" data-id="${session.id}" aria-label="${favoriteLabel}" aria-pressed="${session.pinned ? "true" : "false"}" title="${favoriteLabel}">${icons.favorite}</button></div>`;
  return `<article class="session-row ${session.active ? "active-session" : ""}" data-id="${session.id}" draggable="true"><div class="session-copy"><div class="session-context"><button class="session-path-copy copy-value" type="button" data-copy="${esc(session.cwd)}" title="${t("copyPath")}"><code>${highlight(directoryLabel(session.cwd), query)}</code>${icons.copy}</button><time class="session-time">${sessionTime(session)}</time></div><div class="session-body"><div class="session-summary"><div class="session-title-line"><strong class="session-title" title="${esc(session.title)}">${highlight(session.title, query)}</strong>${gitIndicator(session.git)}</div>${session.customTitle ? `<span class="session-original-title">${t("originalTitle")}: ${highlight(session.originalTitle, query)}</span>` : ""}${session.preview ? `<span class="session-prompt">${highlight(session.preview, query)}</span>` : ""}</div><div class="session-actions">${utility}<a class="session-details" href="/session/${encodeURIComponent(session.id)}">${t("details")}</a><button class="session-launch ${session.desktop ? "desktop" : ""}" data-id="${session.id}" data-action="${action}" ${disabled ? "disabled" : ""} aria-label="${label}" title="${source}: ${label}"><span class="launch-source">${sourceIcon}</span><span class="launch-divider"></span><span class="launch-play">${icons.play}</span></button></div></div>${shadowList}</div></article>`;
}

function gitIndicator(git) {
  if (!git?.linked) return "";
  const stateLabel = gitStateLabel(git);
  const overview = [git.branch, stateLabel, git.ahead ? t("gitAhead", git.ahead) : "", git.behind ? t("gitBehind", git.behind) : ""].filter(Boolean).join(" · ");
  const providerLabel = git.provider === "github" ? `GitHub${git.remoteHost ? ` · ${git.remoteHost}` : ""}` : git.provider === "gitlab" ? `GitLab${git.remoteHost ? ` · ${git.remoteHost}` : ""}` : git.remoteHost || "";
  const changed = (git.changes || []).map((change) => `${change.status}  ${change.path}`);
  if (git.changedFiles > changed.length) changed.push(t("gitMoreChanges", git.changedFiles - changed.length));
  const details = [providerLabel, overview, ...changed, git.lastCommitShort && git.lastCommitSubject ? `${t("lastCommit")}: ${git.lastCommitShort} ${git.lastCommitSubject}` : ""].filter(Boolean).join("\n");
  const stateClass = git.conflicts ? "conflict" : git.dirty ? "dirty" : "clean";
  const providerClass = git.provider === "github" ? "provider-github" : git.provider === "gitlab" ? "provider-gitlab" : "";
  const providerIcon = git.provider === "github" ? icons.github : git.provider === "gitlab" ? icons.gitlab : icons.git;
  return `<span class="session-git ${stateClass} ${providerClass}" tabindex="0" aria-label="${esc(details)}">${providerIcon}<span class="git-tooltip" role="tooltip">${esc(details)}</span></span>`;
}

function shadowSessionRow(session, query = "") {
  const usesChrome = /(^|\s)@Chrome\b/i.test(session.preview || ""); const sourceIcon = usesChrome ? icons.chrome : session.desktop ? icons.desktop : icons.terminal; const sourceLabel = usesChrome ? "Chrome" : session.desktop ? "Desktop" : "CLI";
  const status = session.active ? `<span class="shadow-status active"><i></i>${t("sessionOpen")}</span>` : `<span class="shadow-status">${sessionTime(session)}</span>`;
  return `<div class="shadow-session"><span class="shadow-branch">↳</span><span class="shadow-title" title="${esc(session.title)}">${highlight(session.title, query)}</span><span class="shadow-kind">${sourceIcon}${sourceLabel}</span>${status}</div>`;
}

async function toggleFavorite(threadId, button) {
  const session = state.sessions.find((item) => item.id === threadId); if (!session) return;
  const next = !session.pinned; button.disabled = true;
  try {
    const result = await fetchJson(`/api/session/${encodeURIComponent(threadId)}/pinned/${next}`, { method: "POST" });
    session.pinned = result.pinned; renderSessions();
  } catch (error) {
    button.disabled = false; button.title = error.message;
    showListError(error.message);
  }
}

async function removeSessionFromProject(threadId, button) {
  const session = state.sessions.find((item) => item.id === threadId); if (!session) return;
  if (button) button.disabled = true;
  try {
    await fetchJson(`/api/projects/sessions/${encodeURIComponent(threadId)}/unassign`, { method: "POST" });
    session.projectId = null;
    session.project = session.baseProject || t("noProject");
    renderProjects(); renderSessions();
  } catch (error) {
    if (button) button.disabled = false;
    showListError(error.message);
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
    showListError(error.message);
  }
}

async function sessionPage(id) {
  app.innerHTML = `<div class="shell">${header()}<main class="session-page"><div class="session-content"><a class="back" href="/">${t("backSessions")}</a><div class="page-loading">${t("sessionLoading")}</div></div></main></div>`; bindTheme();
  try { const session = await fetchJson(`/api/session/${encodeURIComponent(id)}`); renderSessionDetail(session); }
  catch (error) { document.querySelector(".session-content").innerHTML = `<a class="back" href="/">${t("backSessions")}</a><div class="empty">${esc(error.message)}</div>`; }
}

function renderSessionDetail(session) {
  const content = document.querySelector(".session-content"); const title = session.title || session.name || session.first_user_message || t("unnamedSession"); const description = session.preview || session.first_user_message || "";
  const sessionStatus = session.openState === "uncertain" ? t("statusUncertain") : session.active ? t("sessionOpen") : t("ready");
  const summary = [[t("status"), sessionStatus], [t("project"), session.project], [t("source"), session.threadSource || session.thread_source || session.source], [t("model"), session.model || session.modelProvider || session.model_provider], [t("branch"), session.git?.branch || session.branch || session.git_branch], [t("gitStatus"), gitSummary(session.git)], [t("lastCommit"), session.git?.lastCommitShort ? `${session.git.lastCommitShort} · ${session.git.lastCommitSubject || ""}` : ""], [t("archived"), session.archived ? t("yes") : t("no")]];
  const excluded = new Set(["first_user_message", "preview"]); const metadata = Object.entries(session).filter(([key]) => !excluded.has(key)).sort(([a], [b]) => a.localeCompare(b));
  const sessionPath = session.storagePath || session.path || session.rollout_path || session.storage_path || "";
  const projectPath = session.cwd || "";
  content.innerHTML = `<a class="back" href="/">${t("backSessions")}</a><section class="session-head"><div><span class="eyebrow">Codex Session</span><div class="detail-title-row"><h1>${esc(title)}</h1><button class="edit-title" id="edit-title" type="button" aria-label="${t("editTitle")}" title="${t("editTitle")}">${icons.edit}</button></div>${session.customTitle ? `<p class="original-title"><span>${t("originalTitle")}</span>${esc(session.originalTitle)}</p>` : ""}<form class="title-editor" id="title-editor" hidden><input id="title-input" maxlength="160" value="${esc(title)}" aria-label="${t("titlePlaceholder")}"><button class="primary" type="submit">${t("save")}</button>${session.customTitle ? `<button class="secondary" id="reset-title" type="button">${t("resetTitle")}</button>` : ""}<button class="secondary" id="cancel-title" type="button">${t("cancel")}</button></form><p>${esc(description)}</p><div class="action-error" id="action-error"></div></div><div class="actions"><button class="primary" data-action="resume" ${session.active ? "disabled" : ""}>${session.active ? t("opened") : t("continue")}</button><button class="secondary" data-action="open-project">VS Code</button><button class="danger" data-action="archive">${t("archiveAction")}</button></div></section><section class="section"><h2>${t("storageLocations")}</h2><div class="path-grid">${pathCard(t("sessionFile"), sessionPath)}${pathCard(t("projectFolder"), projectPath)}</div></section><section class="section"><h2>${t("overview")}</h2><div class="summary-grid">${summary.map(([key, value]) => `<div class="summary-item"><span>${esc(key)}</span><strong>${esc(formatValue(value))}</strong></div>`).join("")}</div></section><details class="metadata-disclosure"><summary>${t("technicalMetadata")}</summary><div class="metadata">${metadata.map(([key, value]) => `<div class="metadata-row"><div class="metadata-key">${esc(key)}</div><div class="metadata-value"><pre>${esc(formatValue(value))}</pre></div></div>`).join("")}</div></details>`;
  if (session.desktop) { const desktopButton = content.querySelector('[data-action="resume"]'); desktopButton.dataset.action = "open-desktop"; desktopButton.disabled = false; desktopButton.textContent = t("openInDesktop"); }
  content.querySelectorAll("[data-action]").forEach((button) => button.onclick = () => runAction(button.dataset.action, session.id));
  bindCopyButtons();
  const editor = content.querySelector("#title-editor"); const input = content.querySelector("#title-input");
  content.querySelector("#edit-title").onclick = () => { editor.hidden = false; input.focus(); input.select(); };
  content.querySelector("#cancel-title").onclick = () => { editor.hidden = true; input.value = title; };
  content.querySelector("#reset-title")?.addEventListener("click", () => saveSessionTitle(session, ""));
  editor.onsubmit = (event) => { event.preventDefault(); void saveSessionTitle(session, input.value); };
}

function pathCard(label, value) {
  const shown = value || "–";
  return `<div class="path-card"><span>${label}</span><button class="path-copy copy-value" type="button" data-copy="${esc(value)}" ${value ? "" : "disabled"} title="${t("copyPath")}"><code>${esc(shown)}</code>${icons.copy}</button></div>`;
}

async function saveSessionTitle(session, title) {
  const editor = document.querySelector("#title-editor"); const errorBox = document.querySelector("#action-error");
  editor.querySelectorAll("button, input").forEach((element) => element.disabled = true);
  try {
    const result = await fetchJson(`/api/session/${encodeURIComponent(session.id)}/title`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title }) });
    Object.assign(session, result);
    renderSessionDetail(session);
  } catch (error) {
    errorBox.textContent = error.message; errorBox.style.display = "block";
    editor.querySelectorAll("button, input").forEach((element) => element.disabled = false);
  }
}

function bindCopyButtons() {
  document.querySelectorAll(".copy-value").forEach((button) => button.onclick = async () => {
    const value = button.dataset.copy; if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      const previous = button.title; button.title = t("copied"); button.classList.add("copied");
      window.setTimeout(() => { button.title = previous; button.classList.remove("copied"); }, 1200);
    } catch {
      window.prompt(t("copyPathFallback"), value);
    }
  });
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
    await loadMessages(languageSettings.language);
    renderCurrentRoute();
  } catch (error) {
    app.innerHTML = `<div class="empty">${messages.connecting || "Connecting to the local companion …"}</div>`;
    window.setTimeout(initialize, 1000);
  }
}

function renderCurrentRoute() {
  const match = window.location.pathname.match(/^\/session\/([A-Za-z0-9_-]+)$/);
  if (match) { void sessionPage(match[1]); return; }
  if (window.location.pathname === "/about" || window.location.pathname === "/history") { void aboutPage(); return; }
  if (window.location.pathname === "/settings/terminal") { void terminalSettingsPage(); return; }
  listPage();
}

initialize();
