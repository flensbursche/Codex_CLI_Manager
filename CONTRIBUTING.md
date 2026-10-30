# Contributing

Danke für dein Interesse am Codex CLI Manager.

## Entwicklung

Voraussetzungen sind Windows, Node.js und das .NET 10 SDK.

```powershell
node scripts/check-web-app.mjs
dotnet build agent\CodexSessionManager.Agent.csproj -c Release
dotnet run --project tests\CodexSessionManager.IntegrationTests\CodexSessionManager.IntegrationTests.csproj -c Release
```

Bitte führe beide Prüfungen vor einem Pull Request aus. Pull Requests sollten eine klar abgegrenzte Änderung enthalten und sichtbare UI-Änderungen kurz beschreiben oder mit einem Screenshot belegen.

## Leitlinien

- Keine Zugangsdaten, persönlichen Session-Inhalte oder lokale Codex-Datenbanken committen.
- Die interne Codex-Datenbank nur lesend öffnen. Manager-Daten gehören nach `%LOCALAPPDATA%\CodexSessionManager`.
- Neue UI-Texte in `web/locales/de.json`, `en.json` und `da.json` ergänzen.
- Änderungen am Updateprozess müssen Paket und SHA-256-Prüfsumme weiterhin gemeinsam validieren.
- Release-Kandidaten müssen zusätzlich mit `scripts/test-release.ps1` gegen das tatsächlich gepackte ZIP geprüft werden. Der Upgrade-Modus mit `-DisposableMachine` darf ausschließlich auf einer zurücksetzbaren Windows-Testumgebung laufen.

Sicherheitsprobleme bitte entsprechend [SECURITY.md](SECURITY.md) melden.
