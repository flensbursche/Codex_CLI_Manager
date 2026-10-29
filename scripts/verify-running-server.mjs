const baseUrl = process.env.CODEX_MANAGER_URL || "http://127.0.0.1:4179";
const health = await fetch(`${baseUrl}/api/health`).then((response) => response.json());
const sessions = await fetch(`${baseUrl}/api/sessions`).then((response) => response.json());
const activeSessions = await fetch(`${baseUrl}/api/sessions?archived=false`).then((response) => response.json());
const archivedSessions = await fetch(`${baseUrl}/api/sessions?archived=true`).then((response) => response.json());

if (!health.ok || !Array.isArray(sessions.data) || !Array.isArray(activeSessions.data) || !Array.isArray(archivedSessions.data)) {
  throw new Error("Der lokale Session-Server liefert keine gültigen Daten.");
}

if (sessions.data.length === 0) {
  throw new Error("Es wurden keine Codex-Sessions geladen.");
}

if (sessions.data.some((session) => String(session.id).startsWith("thr_01J_"))) {
  throw new Error("Die Session-Liste enthält noch Demo-Daten.");
}

if (sessions.data.some((session) => !session.id || !session.title || !("cwd" in session))) {
  throw new Error("Mindestens eine Session enthält keine echten Codex-Metadaten.");
}

const sessionsWithStoragePath = sessions.data.filter((session) => session.storagePath).length;
if (sessionsWithStoragePath === 0) {
  throw new Error("Keine Session enthält ihren Codex-Speicherort.");
}

const firstSession = sessions.data[0];
const detailResponse = await fetch(`${baseUrl}/api/session/${encodeURIComponent(firstSession.id)}`);
const detail = await detailResponse.json();
if (!detailResponse.ok || detail.data?.id !== firstSession.id || !detail.data?.rollout_path) {
  throw new Error("Die Session-Detailseite erhält keine vollständigen Metadaten.");
}

const pageResponse = await fetch(`${baseUrl}/session/${encodeURIComponent(firstSession.id)}`);
if (!pageResponse.ok || !(await pageResponse.text()).includes("/app.js")) {
  throw new Error("Die eigenständige Sessionseite ist nicht erreichbar.");
}

const projects = new Set(sessions.data.map((session) => session.project));
console.log(`${sessions.data.length} Sessions, ${projects.size} Projekte, Speicherorte und Detailseiten OK.`);
