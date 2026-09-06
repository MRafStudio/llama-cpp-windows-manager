@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  build-ext-release.bat — ЕДИНАЯ точка релизной сборки (ext)
rem  Вызывает scripts\build-ext-release.ps1 (App + Service +
rem  Updater + llwmctl -> dist, zip РОВНО из 4 exe, Setup).
rem  Запуск:  build-ext-release.bat [Release|Debug]
rem ============================================================

set "CFG=%~1"
if "%CFG%"=="" set "CFG=Release"

echo [build-ext-release] Configuration: %CFG%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-ext-release.ps1" -Configuration %CFG%
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo [build-ext-release] FAILED with exit code %RC%
  exit /b %RC%
)
echo [build-ext-release] OK — собрано в dist\LlamaCppWindowsManager-win-x64
endlocal
