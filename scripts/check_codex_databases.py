from pathlib import Path
import sqlite3

codex_home = Path.home() / ".codex"
databases = ["state_5.sqlite", "memories_1.sqlite", "thread_history_1.sqlite"]

for name in databases:
    path = codex_home / name
    connection = sqlite3.connect(f"file:{path.as_posix()}?mode=ro&immutable=1", uri=True)
    result = connection.execute("pragma quick_check").fetchone()[0]
    connection.close()
    print(f"{name}: {result}")
