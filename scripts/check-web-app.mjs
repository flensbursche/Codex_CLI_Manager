import { readFile } from "node:fs/promises";
import vm from "node:vm";

const script = await readFile(new URL("../web/app.js", import.meta.url), "utf8");
new vm.Script(script);
for (const feature of ['session.internal === true', 'open-desktop', 'session-play', 'Gerade eben']) {
  if (!script.includes(feature)) throw new Error(`Web-Feature fehlt: ${feature}`);
}
const agent = await readFile(new URL("../agent/Program.cs", import.meta.url), "utf8");
if (!agent.includes("thread-writer-locks")) throw new Error("Aktive Codex-Threads werden nicht über Writer-Locks erkannt.");
console.log("Web-App und Companion-Features sind syntaktisch valide.");
