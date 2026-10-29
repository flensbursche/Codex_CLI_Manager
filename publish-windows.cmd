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
powershell.exe -NoProfile -Command "Compress-Archive -Path 'dist\windows-x64\*' -DestinationPath 'dist\CodexSessionManager-Setup-win-x64.zip'"
if errorlevel 1 pause
exit /b 0
:failed
pause
exit /b 1
