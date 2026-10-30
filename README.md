# Codex CLI Manager

Ein lokales Windows-Cockpit zum Finden, Sortieren, Öffnen und Verwalten gespeicherter Codex-Sessions.

## Funktionen

- liest echte Sessions direkt aus der lokalen Codex-State-Datenbank
- gruppiert Sessions nach Aktivität und filtert nach Projekt, Zeitraum, Quelle und Archivstatus
- blendet interne Guardian- und Memory-Sessions standardmäßig aus
- erkennt laufende Threads über Codex' Writer-Locks und verhindert Doppelstarts
- verwaltet Favoriten und frei angelegte Projekte mit Drag-and-drop-Zuordnung
- bündelt von Connectoren erzeugte Schatten-Sessions unter ihrer Hauptsession
- setzt CLI-Sessions in PowerShell fort
- erkennt Windows Terminal und installierte PowerShell-Versionen pro Benutzer
- öffnet Desktop-Sessions per offiziellem `codex://threads/<id>`-Deep-Link
- zeigt Session- und Projektpfade sowie vollständige Metadaten
- öffnet Projektordner in VS Code
- läuft im Windows-Tray; das Terminal erscheint nur auf Wunsch
- startet ohne Administratorrechte automatisch mit Windows
- erkennt neue GitHub Releases und installiert sie über das Tray-Menü
- aktualisiert die Sessionliste ohne Seitenreload über Server-Sent Events

## Installation unter Windows

1. `CodexSessionManager-Setup-win-x64.zip` vollständig entpacken.
2. `CodexSessionManager.exe` doppelklicken.

Beim ersten Start installiert sich die Anwendung nach:

```text
%LOCALAPPDATA%\CodexSessionManager
```

Sie trägt sich für den aktuellen Benutzer in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ein, öffnet die Browseroberfläche unter `http://127.0.0.1:4180/` und läuft anschließend im Tray. Es werden weder Administratorrechte noch ein Windows-Dienst benötigt.

Im Tray-Menü prüft `Nach Updates suchen` das neueste GitHub Release. Verfügbare Updates werden heruntergeladen, per SHA-256 geprüft, in der lokalen Installation ersetzt und anschließend neu gestartet. Projekte und Terminal-Einstellungen bleiben erhalten.

Voraussetzungen auf dem Zielrechner:

- Windows 10 oder Windows 11 x64
- installierte Codex CLI mit bestehender Anmeldung

Das veröffentlichte Windows-Paket ist selbstenthalten und benötigt kein separat installiertes .NET. Unternehmensrichtlinien wie AppLocker, WDAC oder Defender können unsignierte Anwendungen trotzdem blockieren; für eine breite interne Verteilung ist eine Codesignatur empfehlenswert.

## Release bauen

Der GitHub-Workflow `Windows companion` läuft manuell oder bei Tags mit dem Präfix `v`. Ein Tag wie `v0.7.0` erzeugt ein GitHub Release mit `CodexSessionManager-Setup-win-x64.zip` und SHA-256-Prüfsumme. Installierte Clients erkennen dieses Release automatisch und bieten es im Tray-Menü an.

Ein normaler Push auf `main` veröffentlicht absichtlich kein Client-Update. Release-Versionen werden so angelegt:

```powershell
git tag v0.7.0
git push origin v0.7.0
```

Lokal mit installiertem .NET 10 SDK:

```powershell
publish-windows.cmd
```

Der Build ist `win-x64`, self-contained und als einzelne ausführbare Datei konfiguriert.

## Entwicklung

Der produktive Companion besteht aus:

- `agent/`: ASP.NET-Core-Companion, lokale API, Tray und Windows-Integration
- `web/`: statische Browseroberfläche
- `scripts/check-web-app.mjs`: Syntax- und Feature-Smoke-Test

Die ausgelieferte Anwendung benötigt weder Node.js noch Python. Node.js wird nur
optional während der Entwicklung für den Web-Smoke-Test verwendet.

Build und Prüfungen:

```powershell
node scripts/check-web-app.mjs
dotnet build agent\CodexSessionManager.Agent.csproj
```

## Sicherheit

- Der HTTP-Server bindet ausschließlich an `127.0.0.1:4180`.
- Schreibende Browseraktionen benötigen einen zufälligen lokalen Aktionstoken.
- Aktionsendpunkte akzeptieren ausschließlich validierte Session-IDs, keine freien Shellbefehle oder Arbeitsverzeichnisse.
- Die Codex-Datenbank wird read-only geöffnet.
- Der Companion übernimmt die normalen Rechte des angemeldeten Windows-Benutzers.
