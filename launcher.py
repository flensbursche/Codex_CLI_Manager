from __future__ import annotations

import os
from pathlib import Path
import threading
import webbrowser


def verify_normal_user_access() -> None:
    codex_home = Path(os.environ.get("CODEX_HOME", Path.home() / ".codex"))
    probe = codex_home / f".codex-session-manager-access-{os.getpid()}"
    try:
        probe.write_text("access-check", encoding="utf-8")
        probe.unlink()
    except OSError as error:
        raise SystemExit(
            "\nDer Manager hat keine normalen Benutzerrechte auf ~/.codex.\n"
            "Bitte start-manager.cmd per Doppelklick im Windows Explorer starten.\n"
            f"Technisches Detail: {error}\n"
        ) from error


verify_normal_user_access()
os.environ.setdefault("CODEX_MANAGER_PORT", "4180")
threading.Timer(1.0, lambda: webbrowser.open("http://127.0.0.1:4180/")).start()

from server import main

main()
