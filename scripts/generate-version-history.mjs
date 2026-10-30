import { execFileSync } from "node:child_process";
import { writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const runGit = (...args) => execFileSync("git", args, { cwd: root, encoding: "utf8" }).trim();

const readCommits = (range) => {
  const output = runGit("log", range, "--date=iso-strict", "--format=%H%x1f%h%x1f%cI%x1f%s%x1e");
  if (!output) return [];
  return output.split("\x1e").map((record) => record.trim()).filter(Boolean).map((record) => {
    const [hash, shortHash, date, subject] = record.split("\x1f");
    return { hash, shortHash, date, subject };
  });
};

const tags = runGit("tag", "--list", "v*", "--sort=-version:refname").split(/\r?\n/).filter(Boolean);
const head = runGit("rev-parse", "HEAD");
const releases = [];

if (!tags.length || runGit("rev-list", "-n", "1", tags[0]) !== head) {
  const commits = readCommits(tags.length ? `${tags[0]}..HEAD` : "HEAD");
  if (commits.length) releases.push({ version: null, tag: null, date: commits[0].date, commit: head, released: false, commits });
}

for (let index = 0; index < tags.length; index += 1) {
  const tag = tags[index];
  const previousTag = tags[index + 1];
  const commits = readCommits(previousTag ? `${previousTag}..${tag}` : tag);
  const commit = runGit("rev-list", "-n", "1", tag);
  releases.push({
    version: tag.replace(/^v/i, ""),
    tag,
    date: commits[0]?.date ?? runGit("show", "-s", "--format=%cI", commit),
    commit,
    released: true,
    commits
  });
}

const history = { generatedAt: runGit("show", "-s", "--format=%cI", "HEAD"), releases };
await writeFile(resolve(root, "web", "version-history.json"), `${JSON.stringify(history, null, 2)}\n`, "utf8");
console.log(`Versionshistorie mit ${releases.length} Abschnitt(en) erzeugt.`);
