@echo off
setlocal
cd /d "%~dp0"
set DOTNET_CLI_HOME=%CD%\.dotnet-cli
set NUGET_PACKAGES=%CD%\.nuget-packages
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet publish agent\CodexSessionManager.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\windows-x64
if errorlevel 1 goto failed
if exist dist\CodexSessionManager-Setup-win-x64.zip del dist\CodexSessionManager-Setup-win-x64.zip
pushd dist\windows-x64
tar.exe -a -c -f ..\CodexSessionManager-Setup-win-x64.zip *
popd
if errorlevel 1 pause
powershell.exe -NoProfile -Command "$stream = [IO.File]::OpenRead('dist\CodexSessionManager-Setup-win-x64.zip'); $sha = [Security.Cryptography.SHA256]::Create(); try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose(); $stream.Dispose() }; Set-Content -Path 'dist\CodexSessionManager-Setup-win-x64.zip.sha256' -Value ($hash + '  CodexSessionManager-Setup-win-x64.zip') -Encoding ascii -NoNewline"
if errorlevel 1 pause
exit /b 0
:failed
pause
exit /b 1
