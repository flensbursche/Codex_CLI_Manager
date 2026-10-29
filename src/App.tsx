import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  IconArchive,
  IconArrowsSort,
  IconBrandGithub,
  IconBrandVisualStudio,
  IconChevronDown,
  IconClock,
  IconCode,
  IconCommand,
  IconDots,
  IconFolder,
  IconGitBranch,
  IconLayoutSidebarLeftCollapse,
  IconPlayerPlay,
  IconRefresh,
  IconSearch,
  IconSettings,
  IconStar,
  IconTerminal2,
} from "@tabler/icons-react";
import {
  archiveSession,
  getEnvironmentStatus,
  isTauri,
  listSessions,
  openProject,
  resumeSession,
} from "./lib/codex-client";
import { relativeTime } from "./lib/format";
import type { Session, SortKey } from "./types";

const statusLabels = {
  active: "Aktiv",
  approval: "Freigabe nötig",
  idle: "Bereit",
  error: "Fehler",
  "not-loaded": "Inaktiv",
};

function App() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState("");
  const [project, setProject] = useState("Alle Sessions");
  const [sort, setSort] = useState<SortKey>("recency");
  const [selectedId, setSelectedId] = useState<string>();
  const [message, setMessage] = useState<string>();

  const sessionsQuery = useQuery({ queryKey: ["sessions"], queryFn: () => listSessions(false) });
  const environmentQuery = useQuery({ queryKey: ["environment"], queryFn: getEnvironmentStatus });
  const sessions = sessionsQuery.data ?? [];

  const projects = useMemo(() => {
    const counts = new Map<string, number>();
    sessions.forEach((session) => counts.set(session.project, (counts.get(session.project) ?? 0) + 1));
    return [...counts.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [sessions]);

  const visibleSessions = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase("de");
    const filtered = sessions.filter((session) => {
      const inProject = project === "Alle Sessions" || session.project === project;
      const searchable = `${session.title} ${session.preview} ${session.cwd} ${session.branch ?? ""}`.toLocaleLowerCase("de");
      return inProject && (!needle || searchable.includes(needle));
    });
    return filtered.toSorted((a, b) => {
      if (a.pinned !== b.pinned) return a.pinned ? -1 : 1;
      if (sort === "created") return b.createdAt - a.createdAt;
      if (sort === "project") return a.project.localeCompare(b.project);
      if (sort === "title") return a.title.localeCompare(b.title);
      return b.updatedAt - a.updatedAt;
    });
  }, [project, query, sessions, sort]);

  const selected = visibleSessions.find((session) => session.id === selectedId) ?? visibleSessions[0];

  const action = useMutation({
    mutationFn: async ({ type, session }: { type: "resume" | "project" | "archive"; session: Session }) => {
      setMessage(undefined);
      if (type === "resume") return resumeSession(session);
      if (type === "project") return openProject(session);
      return archiveSession(session);
    },
    onSuccess: (_, variables) => {
      if (variables.type === "archive") void queryClient.invalidateQueries({ queryKey: ["sessions"] });
    },
    onError: (error) => setMessage(error instanceof Error ? error.message : "Aktion fehlgeschlagen."),
  });

  return (
    <div className="app-shell">
      <header className="titlebar">
        <div className="brand-mark"><IconCommand size={17} stroke={2.2} /></div>
        <span className="brand-name">Codex Sessions</span>
        <span className="environment-pill">
          <span className={`status-dot ${isTauri() ? "online" : "demo"}`} />
          {isTauri() ? environmentQuery.data?.codexVersion ?? "Codex wird geprüft" : "Lokaler Codex-State"}
        </span>
        <div className="titlebar-drag" />
        <button className="icon-button" aria-label="Einstellungen"><IconSettings size={18} /></button>
      </header>

      <main className="workspace">
        <aside className="sidebar">
          <div className="sidebar-heading">
            <span>Projekte</span>
            <button className="ghost-icon" aria-label="Sidebar einklappen"><IconLayoutSidebarLeftCollapse size={17} /></button>
          </div>
          <nav className="project-nav">
            <button className={project === "Alle Sessions" ? "active" : ""} onClick={() => setProject("Alle Sessions")}>
              <span><IconCode size={17} /> Alle Sessions</span><b>{sessions.length}</b>
            </button>
            {projects.map(([name, count]) => (
              <button key={name} className={project === name ? "active" : ""} onClick={() => setProject(name)}>
                <span><IconFolder size={17} /> {name}</span><b>{count}</b>
              </button>
            ))}
          </nav>
          <div className="sidebar-spacer" />
          <button className="sidebar-action"><IconArchive size={17} /> Archiv</button>
          <button className="sidebar-action"><IconBrandGithub size={17} /> Repository</button>
        </aside>

        <section className="session-column">
          <div className="toolbar">
            <label className="search-box">
              <IconSearch size={18} />
              <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Sessions durchsuchen …" />
              <kbd>Ctrl K</kbd>
            </label>
            <label className="sort-control">
              <IconArrowsSort size={17} />
              <select value={sort} onChange={(event) => setSort(event.target.value as SortKey)}>
                <option value="recency">Zuletzt verwendet</option>
                <option value="created">Erstellt</option>
                <option value="project">Projekt</option>
                <option value="title">Titel</option>
              </select>
              <IconChevronDown size={14} />
            </label>
            <button className="icon-button refresh" onClick={() => void sessionsQuery.refetch()} aria-label="Aktualisieren">
              <IconRefresh className={sessionsQuery.isFetching ? "spin" : ""} size={18} />
            </button>
          </div>

          <div className="list-meta"><span>{visibleSessions.length} Sessions</span><span>Projekt · Branch · Status</span></div>
          <div className="session-list">
            {visibleSessions.map((session) => (
              <button
                className={`session-row ${selected?.id === session.id ? "selected" : ""}`}
                key={session.id}
                onClick={() => setSelectedId(session.id)}
                onDoubleClick={() => action.mutate({ type: "resume", session })}
              >
                <span className={`session-status ${session.status}`} />
                <span className="session-copy">
                  <span className="session-title-line">
                    {session.pinned && <IconStar size={14} fill="currentColor" />}
                    <strong>{session.title}</strong>
                    <time>{relativeTime(session.updatedAt)}</time>
                  </span>
                  <span className="session-preview">{session.preview}</span>
                  <span className="session-tags">
                    <span><IconFolder size={13} />{session.project}</span>
                    {session.branch && <span><IconGitBranch size={13} />{session.branch}</span>}
                    <span className={`status-label ${session.status}`}>{statusLabels[session.status]}</span>
                  </span>
                </span>
                <IconDots className="row-menu" size={18} />
              </button>
            ))}
            {!sessionsQuery.isLoading && visibleSessions.length === 0 && (
              <div className="empty-state"><IconSearch size={28} /><strong>Keine Sessions gefunden</strong><span>Versuche einen anderen Filter.</span></div>
            )}
          </div>
        </section>

        <aside className="detail-panel">
          {selected ? (
            <>
              <div className="detail-header">
                <span className="eyebrow">SESSION</span>
                <button className="ghost-icon"><IconDots size={19} /></button>
                <h1>{selected.title}</h1>
                <p>{selected.preview}</p>
              </div>
              <div className="primary-actions">
                <button className="primary" onClick={() => action.mutate({ type: "resume", session: selected })}>
                  <IconPlayerPlay size={17} fill="currentColor" /> Fortsetzen
                </button>
                <button className="secondary" onClick={() => action.mutate({ type: "project", session: selected })}>
                  <IconBrandVisualStudio size={18} /> VS Code
                </button>
              </div>
              {message && <div className="action-message">{message}</div>}
              <dl className="facts">
                <div><dt>Status</dt><dd><span className={`session-status ${selected.status}`} />{statusLabels[selected.status]}</dd></div>
                <div><dt>Projekt</dt><dd>{selected.project}</dd></div>
                <div><dt>Branch</dt><dd><IconGitBranch size={15} />{selected.branch ?? "–"}</dd></div>
                <div><dt>Quelle</dt><dd>{selected.source}</dd></div>
                <div><dt>Modell</dt><dd>{selected.model ?? "Standard"}</dd></div>
                <div><dt>Aktualisiert</dt><dd><IconClock size={15} />{relativeTime(selected.updatedAt)}</dd></div>
              </dl>
              <div className="path-card"><span>ARBEITSVERZEICHNIS</span><code>{selected.cwd}</code></div>
              <div className="detail-footer">
                <button onClick={() => action.mutate({ type: "project", session: selected })}><IconTerminal2 size={16} /> Projekt öffnen</button>
                <button onClick={() => action.mutate({ type: "archive", session: selected })}><IconArchive size={16} /> Archivieren</button>
              </div>
            </>
          ) : <div className="empty-state"><IconCommand size={30} /><strong>Session auswählen</strong></div>}
        </aside>
      </main>
    </div>
  );
}

export default App;
