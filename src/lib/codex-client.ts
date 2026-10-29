import { invoke } from "@tauri-apps/api/core";
import { projectFromCwd } from "./format";
import type { EnvironmentStatus, Session, SessionStatus } from "../types";

type RawThread = {
  id: string;
  name?: string | null;
  preview?: string | null;
  cwd?: string | null;
  rolloutPath?: string | null;
  model?: string | null;
  modelProvider?: string | null;
  source?: string | { type?: string } | null;
  createdAt?: number | null;
  updatedAt?: number | null;
  isPinned?: boolean | null;
  gitInfo?: { branch?: string | null } | null;
  status?: { type?: string; activeFlags?: string[] } | null;
};

const isTauri = () => "__TAURI_INTERNALS__" in window;

function timestamp(value?: number | null): number {
  if (!value) return Date.now();
  return value < 10_000_000_000 ? value * 1000 : value;
}

function statusOf(raw?: RawThread["status"]): SessionStatus {
  if (!raw?.type || raw.type === "notLoaded") return "not-loaded";
  if (raw.type === "systemError") return "error";
  if (raw.type === "idle") return "idle";
  if (raw.activeFlags?.some((flag) => flag.toLowerCase().includes("approval"))) return "approval";
  return "active";
}

function normalize(raw: RawThread, archived: boolean): Session {
  const cwd = raw.cwd || "";
  const source = typeof raw.source === "string" ? raw.source : raw.source?.type || "cli";
  return {
    id: raw.id,
    title: raw.name || raw.preview || "Unbenannte Session",
    preview: raw.preview || "Noch keine Vorschau verfügbar.",
    cwd,
    storagePath: raw.rolloutPath || undefined,
    project: projectFromCwd(cwd),
    branch: raw.gitInfo?.branch || undefined,
    model: raw.model || raw.modelProvider || undefined,
    source,
    status: statusOf(raw.status),
    createdAt: timestamp(raw.createdAt),
    updatedAt: timestamp(raw.updatedAt || raw.createdAt),
    pinned: Boolean(raw.isPinned),
    archived,
  };
}

export async function listSessions(archived = false): Promise<Session[]> {
  if (!isTauri()) {
    const response = await fetch(`/api/sessions?archived=${archived ? "1" : "0"}`, { cache: "no-store" });
    const result = await response.json() as { data?: Session[]; error?: string };
    if (!response.ok || !result.data) throw new Error(result.error || "Codex-Sessions konnten nicht geladen werden.");
    return result.data;
  }
  const result = await invoke<{ data: RawThread[] }>("list_threads", { archived });
  return result.data.map((thread) => normalize(thread, archived));
}

export async function getEnvironmentStatus(): Promise<EnvironmentStatus> {
  if (!isTauri()) {
    const response = await fetch("/api/health", { cache: "no-store" });
    return { codexInstalled: response.ok, terminalInstalled: response.ok, vscodeInstalled: response.ok };
  }
  return invoke<EnvironmentStatus>("environment_status");
}

export async function resumeSession(session: Session): Promise<void> {
  if (!isTauri()) return postLocalAction("resume", session.id);
  await invoke("resume_session", { threadId: session.id, cwd: session.cwd });
}

export async function openProject(session: Session): Promise<void> {
  if (!isTauri()) return postLocalAction("open-project", session.id);
  await invoke("open_project", { cwd: session.cwd });
}

export async function archiveSession(session: Session): Promise<void> {
  if (!isTauri()) return postLocalAction("archive", session.id);
  await invoke("archive_session", { threadId: session.id });
}

export { isTauri };

async function postLocalAction(action: string, threadId: string): Promise<void> {
  const response = await fetch(`/api/${action}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ threadId }),
  });
  const result = await response.json() as { error?: string };
  if (!response.ok) throw new Error(result.error || "Aktion fehlgeschlagen.");
}
