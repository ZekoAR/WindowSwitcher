@echo off
setlocal
pushd "%~dp0"

REM Builds WindowSwitcher as a Native AOT single exe: dist\WindowSwitcher.exe. With --install it also
REM installs that exe for the current user and starts it with Windows (tools\install.ps1).

set "DO_INSTALL="

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--install" goto opt_install
if /i "%~1"=="-i" goto opt_install
if /i "%~1"=="--help" goto usage
if /i "%~1"=="-h" goto usage
if /i "%~1"=="/?" goto usage
echo ERROR: unknown option "%~1".
echo.
goto usage

:opt_install
set "DO_INSTALL=1"
shift
goto parse

:parsed

REM VsDevCmd.bat runs vswhere.exe from its own folder by bare name. Some shells (agent sandboxes) set
REM this variable, which stops cmd finding it, and the Native AOT linker lookup then fails.
set NoDefaultCurrentDirectoryInExePath=

if exist dist\WindowSwitcher.exe (
	del /q dist\WindowSwitcher.exe 2>nul
	if exist dist\WindowSwitcher.exe (
		echo ERROR: dist\WindowSwitcher.exe is in use. Exit that copy from its tray icon and build again.
		goto fail
	)
)

echo [1/2] build
dotnet publish src\WindowSwitcher\WindowSwitcher.csproj -c Release -o dist -nologo -tl:off -clp:Summary
if errorlevel 1 goto fail
echo Built %~dp0dist\WindowSwitcher.exe

if not defined DO_INSTALL (
	echo [2/2] install - skipped, pass --install to install it for this user and start it with Windows
	goto done
)

echo [2/2] install
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" -Exe "%~dp0dist\WindowSwitcher.exe"
if errorlevel 1 goto fail

:done
popd
exit /b 0

:usage
echo Usage: build.cmd [--install]
echo.
echo   (no args)   build the Native AOT exe into dist\WindowSwitcher.exe.
echo   --install   the above, then install it for the current user:
echo                 - exit a running WindowSwitcher, as its tray menu's Exit does
echo                 - copy the exe to %%LOCALAPPDATA%%\Programs\WindowSwitcher
echo                   or WINDOWSWITCHER_INSTALL_DIR; WindowSwitcher.json there is kept
echo                 - start it with Windows: HKCU Run value "WindowSwitcher"
echo                   WINDOWSWITCHER_INSTALL_NO_STARTUP=1 skips this step
echo                 - start the installed copy
echo   --help      this text.
popd
exit /b 2

:fail
set ERR=%ERRORLEVEL%
if "%ERR%"=="0" set ERR=1
echo.
echo BUILD FAILED ^(exit %ERR%^)
popd
exit /b %ERR%
