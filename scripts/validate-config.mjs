import { readFile } from "node:fs/promises";

const files = [
  "package.json",
  "tsconfig.json",
  "tsconfig.app.json",
  "tsconfig.node.json",
  "src-tauri/tauri.conf.json",
];

for (const file of files) {
  JSON.parse(await readFile(new URL(`../${file}`, import.meta.url), "utf8"));
}

console.log(`${files.length} JSON-Konfigurationen sind valide.`);
