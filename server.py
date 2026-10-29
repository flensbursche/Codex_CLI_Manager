from __future__ import annotations

import json
import os
from pathlib import Path
import re
import secrets
import sqlite3
import subprocess
import time
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parent
CODEX_HOME = Path(os.environ.get("CODEX_HOME", Path.home() / ".codex"))
STATE_DB = CODEX_HOME / "state_5.sqlite"
THREAD_ID = re.compile(r"^[A-Za-z0-9_-]{8,128}$")
ACTION_TOKEN = secrets.token_urlsafe(32)


def connect() -> sqlite3.Connection:
    connection = sqlite3.connect(f"file:{STATE_DB.as_posix()}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def normalize_windows_path(value: str | None) -> str:
    if not value:
        return ""
    if value.startswith("\\\\?\\UNC\\"):
        return "\\\\" + value[8:]
    if value.startswith("\\\\?\\"):
        return value[4:]
    return value


def project_name(cwd: str | None) -> str:
    if not cwd:
        return "Ohne Projekt"
    cwd = normalize_windows_path(cwd)
    return Path(cwd.rstrip("\\/")).name or cwd


def read_sessions(archived: bool = False) -> list[dict]:
    query = """
        select id, rollout_path, cwd, name, title, preview, first_user_message,
               created_at_ms, updated_at_ms, recency_at_ms,
               source, thread_source, model_provider, model,
               git_branch, is_pinned, archived
          from threads
         where archived = ?
         order by coalesce(recency_at_ms, updated_at_ms, created_at_ms) desc
    """
    with connect() as connection:
        rows = connection.execute(query, (1 if archived else 0,)).fetchall()

    sessions = []
    for row in rows:
        cwd = normalize_windows_path(row["cwd"])
        title = row["name"] or row["title"] or row["first_user_message"] or "Unbenannte Session"
        preview = row["preview"] or row["first_user_message"] or ""
        source = row["thread_source"] or row["source"] or "cli"
        sessions.append(
            {
                "id": row["id"],
                "title": str(title).strip()[:160],
                "preview": str(preview).strip()[:360],
                "cwd": cwd,
                "storagePath": normalize_windows_path(row["rollout_path"]),
                "project": project_name(cwd),
                "branch": row["git_branch"],
                "model": row["model"] or row["model_provider"],
                "source": str(source),
                "createdAt": row["created_at_ms"] or 0,
                "updatedAt": row["recency_at_ms"] or row["updated_at_ms"] or row["created_at_ms"] or 0,
                "pinned": bool(row["is_pinned"]),
                "archived": bool(row["archived"]),
                "status": "not-loaded",
            }
        )
    return sessions


def get_thread(thread_id: str) -> dict | None:
    if not THREAD_ID.fullmatch(thread_id):
        return None
    with connect() as connection:
        row = connection.execute("select id, cwd from threads where id = ?", (thread_id,)).fetchone()
    if row is None:
        return None
    return {"id": row["id"], "cwd": normalize_windows_path(row["cwd"])}


def read_session(thread_id: str) -> dict | None:
    if not THREAD_ID.fullmatch(thread_id):
        return None
    with connect() as connection:
        row = connection.execute("select * from threads where id = ?", (thread_id,)).fetchone()
    if row is None:
        return None
    metadata = {}
    for key in row.keys():
        value = row[key]
        if isinstance(value, bytes):
            value = value.hex()
        if key in {"cwd", "rollout_path", "agent_path"} and isinstance(value, str):
            value = normalize_windows_path(value)
        metadata[key] = value
    metadata["project"] = project_name(row["cwd"])
    metadata["storage_path"] = normalize_windows_path(row["rollout_path"])
    return metadata


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(ROOT), **kwargs)

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def json_response(self, payload: object, status: HTTPStatus = HTTPStatus.OK) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def serve_app(self) -> None:
        body = (ROOT / "web" / "index.html").read_bytes()
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = parsed.path
        if path == "/api/health":
            self.json_response({"ok": True, "database": str(STATE_DB)})
            return
        if path == "/api/bootstrap":
            self.json_response({"data": {"token": ACTION_TOKEN, "version": "0.2.0", "runtime": "Python Companion", "supportsAutostart": False, "autostartEnabled": False}})
            return
        if path == "/api/sessions":
            try:
                archived = "archived=1" in parsed.query
                self.json_response({"data": read_sessions(archived)})
            except Exception as error:
                self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)
            return
        if path.startswith("/api/session/"):
            thread_id = path.removeprefix("/api/session/")
            session = read_session(thread_id)
            if session is None:
                self.json_response({"error": "Session nicht gefunden."}, HTTPStatus.NOT_FOUND)
            else:
                self.json_response({"data": session})
            return
        if path == "/" or (path.startswith("/session/") and THREAD_ID.fullmatch(path.removeprefix("/session/"))):
            self.serve_app()
            return
        super().do_GET()

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        if self.headers.get("X-Codex-Manager-Token") != ACTION_TOKEN:
            self.json_response({"error": "Ungültiger Aktionstoken."}, HTTPStatus.FORBIDDEN)
            return
        try:
            length = min(int(self.headers.get("Content-Length", "0")), 4096)
            payload = json.loads(self.rfile.read(length) or b"{}")
        except (ValueError, json.JSONDecodeError):
            self.json_response({"error": "Ungültige Anfrage."}, HTTPStatus.BAD_REQUEST)
            return

        thread = get_thread(str(payload.get("threadId", "")))
        if thread is None:
            self.json_response({"error": "Session nicht gefunden."}, HTTPStatus.NOT_FOUND)
            return

        try:
            if path == "/api/resume":
                cwd = Path(thread["cwd"])
                if not cwd.is_dir():
                    raise RuntimeError(f"Projektordner existiert nicht: {cwd}")
                process = subprocess.Popen(
                    [
                        r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                        "-NoLogo",
                        "-NoProfile",
                        "-NoExit",
                        "-Command",
                        f"codex --no-daemon resume '{thread['id']}'",
                    ],
                    cwd=str(cwd),
                    creationflags=subprocess.CREATE_NEW_CONSOLE | subprocess.CREATE_NEW_PROCESS_GROUP,
                    close_fds=True,
                )
                time.sleep(0.35)
                if process.poll() not in (None, 0):
                    raise RuntimeError(f"PowerShell wurde mit Exit-Code {process.returncode} beendet.")
            elif path == "/api/open-project":
                subprocess.Popen(
                    ["code", thread["cwd"]],
                    creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP,
                    close_fds=True,
                )
            elif path == "/api/archive":
                result = subprocess.run(
                    ["codex", "archive", thread["id"]],
                    capture_output=True,
                    text=True,
                    timeout=30,
                )
                if result.returncode != 0:
                    raise RuntimeError(result.stderr.strip() or "Archivieren fehlgeschlagen.")
            else:
                self.json_response({"error": "Unbekannte Aktion."}, HTTPStatus.NOT_FOUND)
                return
        except Exception as error:
            self.json_response({"error": str(error)}, HTTPStatus.INTERNAL_SERVER_ERROR)
            return
        self.json_response({"data": {"ok": True}})

    def log_message(self, format: str, *args: object) -> None:
        print(f"[codex-manager] {format % args}")


def main() -> None:
    if not STATE_DB.exists():
        raise SystemExit(f"Codex-State nicht gefunden: {STATE_DB}")
    port = int(os.environ.get("CODEX_MANAGER_PORT", "4179"))
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"Codex Session Manager: http://127.0.0.1:{port}/ ({len(read_sessions())} Sessions)", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
