export type SessionStatus = "active" | "approval" | "idle" | "error" | "not-loaded";

export interface Session {
  id: string;
  title: string;
  preview: string;
  cwd: string;
  storagePath?: string;
  project: string;
  branch?: string;
  model?: string;
  source: string;
  status: SessionStatus;
  createdAt: number;
  updatedAt: number;
  pinned: boolean;
  archived: boolean;
}

export type SortKey = "recency" | "created" | "project" | "title";

export interface EnvironmentStatus {
  codexInstalled: boolean;
  codexVersion?: string;
  terminalInstalled: boolean;
  vscodeInstalled: boolean;
}
