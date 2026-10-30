# Codex CLI Manager

**Open-source Windows workspace manager for OpenAI Codex CLI and Codex Desktop.** Organize sessions across projects, inspect Git state, and resume CLI and Desktop work from one browser UI and Windows tray app.

Codex CLI Manager ist ein lokales Windows-Cockpit zum Finden, Sortieren, Öffnen und Verwalten gespeicherter Codex-Sessions. Es bündelt CLI- und Desktop-Sessions, setzt Terminal-Sessions mit dem persönlichen PowerShell-Profil fort und benötigt weder Cloud-Dienst noch Administratorrechte.

> Unabhängiges Community-Projekt. Nicht von OpenAI entwickelt oder unterstützt.

## Geeignet für

- Codex-CLI-Nutzer, die alte oder parallele Sessions schnell wiederfinden und fortsetzen möchten
- Windows-Nutzer, die Codex-Sessions projektbezogen organisieren wollen
- Teams, die einen lokalen Codex Session Manager mit PowerShell-, Tray- und Auto-Update-Unterstützung suchen
- Nutzer von Codex Desktop und CLI, die beide Sessionquellen in einer Oberfläche benötigen

## Funktionen

- liest echte Sessions direkt aus der lokalen Codex-State-Datenbank
- gruppiert Sessions nach Aktivität und filtert nach Projekt, Zeitraum, Quelle und Archivstatus
- durchsucht Titel, Vorschau, Projekt, Git-Daten und Pfade und hebt sichtbare Treffer hervor
- blendet interne Guardian- und Memory-Sessions standardmäßig aus
- erkennt offene Threads über Codex-Prozess und Writer-Lock, kennzeichnet unklare Zustände und verhindert Doppelstarts
- verwaltet Favoriten in app-eigenen Metadaten und frei angelegte Projekte mit Drag-and-drop-Zuordnung
- löst Projektzuordnungen direkt wieder an der jeweiligen Session
- erkennt Git-Repositories, Branch, Arbeitsbaumstatus, Konflikte und den letzten Commit ohne Schreibzugriff
- bündelt About, die aus Release-Tags und Git-Commits erzeugte Versionshistorie und eine erweiterbare 1.0-Roadmap
- bündelt von Connectoren erzeugte Schatten-Sessions unter ihrer Hauptsession
- setzt CLI-Sessions in einem neuen Windows-Terminal-Fenster mit PowerShell fort
- erkennt Windows Terminal und installierte PowerShell-Versionen pro Benutzer
- öffnet Desktop-Sessions über den von aktuellen Codex-Desktop-Versionen registrierten `codex://threads/<id>`-Deep-Link
- macht Session- und Projektpfade direkt kopierbar und zeigt vollständige Metadaten
- speichert eigene Session-Titel dauerhaft, ohne den von Codex gelieferten Originaltitel zu überschreiben
- öffnet Projektordner in VS Code
- läuft im Windows-Tray; das Terminal erscheint nur auf Wunsch
- startet ohne Administratorrechte automatisch mit Windows
- erkennt neue GitHub Releases und installiert sie über das Tray-Menü
- aktualisiert die Sessionliste ohne Seitenreload über Server-Sent Events
- bietet eine gemeinsame, sofort wirksame Sprachauswahl für Deutsch, Englisch und Dänisch

## Installation unter Windows

1. `CodexSessionManager-Setup-win-x64.zip` vollständig entpacken.
2. `CodexSessionManager.exe` doppelklicken.

Beim ersten Start installiert sich die Anwendung nach:

```text
%LOCALAPPDATA%\CodexSessionManager
```

Sie trägt sich für den aktuellen Benutzer in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ein, öffnet die Browseroberfläche bevorzugt unter `http://127.0.0.1:4180/` und läuft anschließend im Tray. Ist Port 4180 belegt, wählt sie automatisch den nächsten freien Port bis 4199. Es werden weder Administratorrechte noch ein Windows-Dienst benötigt.

Im Tray-Menü prüft `Nach Updates suchen` das neueste GitHub Release. Verfügbare Updates werden heruntergeladen, per SHA-256 geprüft, in der lokalen Installation ersetzt und anschließend neu gestartet. Ein sichtbares Fortschrittsfenster zeigt Download, Prüfung, Installation und Neustart. Projekte, eigene Session-Titel, Sprache und Terminal-Einstellungen bleiben erhalten.

Voraussetzungen auf dem Zielrechner:

- Windows 10 oder Windows 11 x64
- installierte Codex CLI mit bestehender Anmeldung

Das veröffentlichte Windows-Paket ist selbstenthalten und benötigt kein separat installiertes .NET. Unternehmensrichtlinien wie AppLocker, WDAC oder Defender können unsignierte Anwendungen trotzdem blockieren; für eine breite interne Verteilung ist eine Codesignatur empfehlenswert.

Die vorbereitete Produktionsstrategie für Authenticode, Azure Artifact Signing und klassische OV-Zertifikate steht in [CODE_SIGNING.md](CODE_SIGNING.md). Ein öffentlich vertrauenswürdiges Zertifikat setzt immer eine externe Identitätsprüfung voraus und wird nicht im Repository gespeichert.

## Release bauen

Der GitHub-Workflow `Windows companion` läuft manuell oder bei Tags mit dem Präfix `v`. Ein Tag wie `v0.9.0` erzeugt ein GitHub Release mit `CodexSessionManager-Setup-win-x64.zip` und SHA-256-Prüfsumme. Installierte Clients erkennen dieses Release ohne Abhängigkeit vom anonymen GitHub-API-Limit und bieten es im Tray-Menü an.

Pushes und Pull Requests gegen `main` führen den Web-Smoke-Test, einen .NET-Release-Build und ausführbare Windows-Integrationstests aus. Die Integrationstests starten eine echte PowerShell mit kodiertem Initialisierungsbefehl, prüfen Windows-Terminal-Argumente, benannte Session-Mutexe, exklusive Dateilocks, die Health-Zustandsmatrix, mehrere echte SQLite-Schemavarianten sowie Prüfsumme, Dateikopie und Pfadschutz des Updaters. Auf einem interaktiven lokalen Windows-Desktop starten sie zusätzlich ein echtes Windows-Terminal-Fenster und verifizieren die Ausführung per Marker; nur dieser GUI-Test wird auf dem headless GitHub-Runner übersprungen. Das ergänzende Smoke-Testskript bleibt bewusst eine schnelle Strukturprüfung.

### Release-Akzeptanz

`scripts/test-release.ps1` prüft das tatsächlich verpackte ZIP statt nur des Build-Ausgabeverzeichnisses. Der isolierte Modus verifiziert SHA-256, optionale Authenticode-Signatur, ZIP-Pfadschutz, Programmstart, Health- und Session-API sowie den echten Update-Helfer mit Prozessbeendigung, Dateiersetzung, Neustart und persistenten Projekt- und Spracheinstellungen, ohne die normale Installation oder den Autostart zu verändern:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./scripts/test-release.ps1 `
  -PackagePath ./CodexSessionManager-Setup-win-x64.zip `
  -ChecksumPath ./CodexSessionManager-Setup-win-x64.zip.sha256 `
  -ExpectedVersion 0.10.3 `
  -InteractiveTerminal `
  -ReportPath ./release-acceptance.json
```

Mit `-PreviousPackagePath` testet das Skript zusätzlich Erstinstallation, Update, Neustart und den Erhalt eigener Daten. Dieser Modus verändert bewusst die Benutzerinstallation und ist deshalb nur zusammen mit `-DisposableMachine` auf einer zurücksetzbaren Windows-VM erlaubt. Der manuell startbare Workflow `Windows release acceptance` führt genau diesen Ablauf auf einem Self-hosted Runner mit dem Label `codex-manager-ui` aus und legt einen JSON-Prüfbericht als Artefakt ab. Für die Kompatibilitätsmatrix wird derselbe Ablauf auf vorbereiteten Runner-Images mit unterschiedlichen Codex-Versionen und echten anonymisierten Session-Daten ausgeführt.

Die Release-Pipeline kann die EXE vor dem Verpacken optional über Azure Artifact Signing signieren und prüft die Authenticode-Signatur anschließend erneut. Die benötigten Variablen, OIDC-Secrets und die Richtlinie zum Erzwingen signierter Releases stehen in [CODE_SIGNING.md](CODE_SIGNING.md).

Ein normaler Push auf `main` veröffentlicht absichtlich kein Client-Update. Release-Versionen werden so angelegt:

```powershell
git tag v0.9.0
git push origin v0.9.0
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
- `web/locales/`: gemeinsame JSON-Sprachressourcen für Browseroberfläche und Tray
- `scripts/check-web-app.mjs`: Syntax- und Feature-Smoke-Test
- `scripts/generate-version-history.mjs`: erzeugt die Versionshistorie aus Tags und Commits
- `tests/CodexSessionManager.IntegrationTests/`: ausführbare Windows-Integrationstests ohne externes Testframework

Die ausgelieferte Anwendung benötigt weder Node.js noch Python. Node.js wird nur
optional während der Entwicklung für den Web-Smoke-Test verwendet.

Build und Prüfungen:

```powershell
node scripts/generate-version-history.mjs
node scripts/check-web-app.mjs
dotnet build agent\CodexSessionManager.Agent.csproj
dotnet run --project tests\CodexSessionManager.IntegrationTests\CodexSessionManager.IntegrationTests.csproj -c Release
```

## Sicherheit

- Der HTTP-Server bindet ausschließlich an `127.0.0.1` und verwendet einen lokalen Port zwischen 4180 und 4199.
- Schreibende Browseraktionen benötigen einen zufälligen lokalen Aktionstoken.
- Aktionsendpunkte akzeptieren ausschließlich validierte Session-IDs, keine freien Shellbefehle oder Arbeitsverzeichnisse.
- Die interne Codex-Datenbank wird ausschließlich read-only geöffnet. Favoriten, eigene Titel und Projektzuordnungen speichert der Manager separat unter `%LOCALAPPDATA%\CodexSessionManager`.
- Das Codex-Datenbankschema ist keine öffentliche Schnittstelle. Optionale fehlende Spalten werden read-only durch leere Werte ersetzt. Fehlen die Session-Tabelle oder ihre ID-Spalte, wechselt der Manager kontrolliert in einen sichtbaren eingeschränkten Lesemodus, statt mit unklaren Folgefehlern weiterzuarbeiten. Die Codex-Datenbank wird dabei nie verändert.
- Der Companion übernimmt die normalen Rechte des angemeldeten Windows-Benutzers.

Sicherheitsprobleme bitte nicht als öffentliches Issue melden. Der Meldeweg steht in [SECURITY.md](SECURITY.md).

## Kompatibilitätshinweis

Der Desktop-Deep-Link ist in aktuellen Codex-Desktop-Installationen registriert, aber nicht als stabile öffentliche API dokumentiert. Änderungen an Codex Desktop können diese Integration daher beeinflussen.

## Lizenz

Codex CLI Manager steht unter der [MIT-Lizenz](LICENSE).
