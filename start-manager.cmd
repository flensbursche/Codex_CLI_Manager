@echo off
setlocal
cd /d "%~dp0"
title Codex Session Manager
python launcher.py
if errorlevel 1 pause
