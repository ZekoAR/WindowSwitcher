@echo off
rem Builds WindowSwitcher as a Native AOT single exe: dist\WindowSwitcher.exe
setlocal
cd /d "%~dp0"

rem VsDevCmd.bat runs vswhere.exe from its own folder by bare name. Some shells (agent sandboxes) set
rem this variable, which stops cmd finding it, and the Native AOT linker lookup then fails.
set NoDefaultCurrentDirectoryInExePath=

if exist dist\WindowSwitcher.exe (
  del /q dist\WindowSwitcher.exe 2>nul
  if exist dist\WindowSwitcher.exe (
    echo dist\WindowSwitcher.exe is in use. Exit WindowSwitcher from its tray icon and build again.
    exit /b 1
  )
)

dotnet publish src\WindowSwitcher\WindowSwitcher.csproj -c Release -o dist -nologo -tl:off -clp:Summary
if errorlevel 1 exit /b 1

echo.
echo Built %~dp0dist\WindowSwitcher.exe
