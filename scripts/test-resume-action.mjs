const baseUrl = "http://127.0.0.1:4179";
const sessions = await fetch(`${baseUrl}/api/sessions`).then((response) => response.json());
const session = sessions.data?.[0];
if (!session) throw new Error("Keine Session zum Testen gefunden.");

const response = await fetch(`${baseUrl}/api/resume`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ threadId: session.id }),
});
const result = await response.json();
console.log(JSON.stringify({ status: response.status, result }, null, 2));
if (!response.ok) process.exitCode = 1;
