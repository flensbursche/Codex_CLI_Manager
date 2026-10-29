import { spawn } from "node:child_process";
import readline from "node:readline";

const child = spawn("codex", ["app-server", "--listen", "stdio://"], {
  stdio: ["pipe", "pipe", "inherit"],
  shell: process.platform === "win32",
});

const send = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);
const lines = readline.createInterface({ input: child.stdout });
const timeout = setTimeout(() => {
  console.error("App-server smoke test timed out.");
  child.kill();
  process.exitCode = 1;
}, 15_000);

lines.on("line", (line) => {
  let message;
  try {
    message = JSON.parse(line);
  } catch {
    return;
  }

  if (message.id === 1) {
    if (message.error) throw new Error(JSON.stringify(message.error));
    send({ method: "initialized", params: {} });
    send({
      method: "thread/list",
      id: 2,
      params: { limit: 5, archived: false, sortKey: "recency_at", sortDirection: "desc", sourceKinds: [] },
    });
  }

  if (message.id === 2) {
    clearTimeout(timeout);
    if (message.error) {
      console.error(JSON.stringify(message.error));
      process.exitCode = 1;
    } else {
      console.log(`Codex app-server OK: ${message.result?.data?.length ?? 0} Sessions gelesen.`);
    }
    child.kill();
  }
});

child.on("error", (error) => {
  clearTimeout(timeout);
  console.error(error.message);
  process.exitCode = 1;
});

send({
  method: "initialize",
  id: 1,
  params: {
    clientInfo: { name: "codex_cli_manager_smoke", title: "Codex Session Manager Smoke Test", version: "0.1.0" },
  },
});
