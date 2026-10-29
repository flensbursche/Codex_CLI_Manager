from pathlib import Path
import sqlite3

database = Path.home() / ".codex" / "state_5.sqlite"
connection = sqlite3.connect(f"file:{database.as_posix()}?mode=ro", uri=True)

for (table,) in connection.execute("select name from sqlite_master where type='table' order by name"):
    columns = [row[1] for row in connection.execute(f'pragma table_info("{table}")')]
    print(f"{table}: {', '.join(columns)}")

print("thread counts:")
for row in connection.execute("select archived, has_user_event, count(*) from threads group by archived, has_user_event"):
    print(tuple(row))
