# Code signing

Öffentliche Windows-Releases sollten die `CodexSessionManager.exe` vor dem Verpacken per Authenticode signieren. Das Zertifikat wird dabei als Teil der Signatur in die EXE eingebettet; der private Schlüssel wird niemals ausgeliefert oder im Repository gespeichert.

## Empfohlener Weg

Microsoft empfiehlt für Downloads außerhalb des Stores [Azure Artifact Signing](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options). Dafür werden ein verifiziertes Signing-Konto und ein Zertifikatsprofil benötigt. Organisationen können den Dienst unter anderem in der EU nutzen; für individuelle Entwickler ist die regionale Verfügbarkeit eingeschränkter.

Nach der Identitätsprüfung wird die GitHub-Actions-Pipeline so erweitert:

1. Self-contained EXE veröffentlichen.
2. `publish/CodexSessionManager.exe` mit Artifact Signing und RFC-3161-Zeitstempel signieren.
3. Die Signatur mit SignTool prüfen.
4. Erst danach ZIP und SHA-256-Datei erzeugen.

Für GitHub Actions sollten Azure-Zugangsdaten bevorzugt per OIDC und Repository-Variablen bereitgestellt werden. Alternativ kann ein OV-Code-Signing-Zertifikat einer öffentlichen Zertifizierungsstelle verwendet werden. Dessen Schlüssel gehört in ein HSM, einen Hardware-Token oder einen geeigneten Secret Store, niemals als `.pfx` in das Repository.

Ein selbstsigniertes Zertifikat eignet sich nur für lokale Tests oder verwaltete Unternehmensgeräte, auf denen die eigene Root-CA zentral als vertrauenswürdig verteilt wird. Für öffentliche Downloads verhindert es SmartScreen-Warnungen nicht.

## Noch benötigte Entscheidung

- Veröffentlichung als Organisation oder als Einzelperson
- Azure Artifact Signing, Microsoft Store oder klassisches OV-Zertifikat
- gewünschter öffentlicher Publisher-Name

Der Release-Workflow ist inzwischen für Azure Artifact Signing vorbereitet. Ohne konfigurierte Publisher-Identität bleiben Releases bewusst unsigniert. Für die Aktivierung werden folgende GitHub-Einstellungen benötigt:

Repository-Variablen:

- `ARTIFACT_SIGNING_ENABLED=true`
- `ARTIFACT_SIGNING_ENDPOINT`, zum Beispiel `https://eus.codesigning.azure.net/`
- `ARTIFACT_SIGNING_ACCOUNT`
- `ARTIFACT_SIGNING_PROFILE`
- `REQUIRE_SIGNED_RELEASES=true`, sobald kein unsigniertes Release mehr zulässig sein soll

Repository-Secrets für die OIDC-Anmeldung:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Die Pipeline meldet sich über `azure/login` an, signiert ausschließlich `publish/CodexSessionManager.exe`, verwendet SHA-256 und den RFC-3161-Zeitstempel von Microsoft und prüft die Signatur vor dem Verpacken erneut mit `Get-AuthenticodeSignature`.

Die Richtlinie sollte in zwei Schritten aktiviert werden: zunächst `ARTIFACT_SIGNING_ENABLED`, nach einem erfolgreich geprüften Testrelease zusätzlich `REQUIRE_SIGNED_RELEASES`. Dadurch kann kein späteres Release versehentlich wieder unsigniert veröffentlicht werden.
