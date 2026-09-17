@echo off
setlocal
pushd "%~dp0"

REM Builds WindowSwitcher as a Native AOT single exe: dist\WindowSwitcher.exe. With --setup it also
REM builds the installer (installer\WindowSwitcher.iss, Inno Setup 6). With --install it also installs
REM that exe for the current user and starts it with Windows (tools\install.ps1).

set "DO_INSTALL="
set "DO_SETUP="
set "VERSION="

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--install" goto opt_install
if /i "%~1"=="-i" goto opt_install
if /i "%~1"=="--setup" goto opt_setup
if /i "%~1"=="-s" goto opt_setup
if /i "%~1"=="--version" goto opt_version
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

:opt_setup
set "DO_SETUP=1"
shift
goto parse

:opt_version
set "VERSION=%~2"
if not defined VERSION (
	echo ERROR: --version needs a value, like --version 1.2.3.
	echo.
	goto usage
)
echo %VERSION%| findstr /r /x "[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*" >nul
if errorlevel 1 (
	echo ERROR: --version must be three numbers, like 1.2.3, not "%VERSION%".
	popd
	exit /b 2
)
shift
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

set "VERSION_ARG="
if defined VERSION set "VERSION_ARG=-p:Version=%VERSION%"

echo [1/3] build
dotnet publish src\WindowSwitcher\WindowSwitcher.csproj -c Release -o dist -nologo -tl:off -clp:Summary %VERSION_ARG%
if errorlevel 1 goto fail
echo Built %~dp0dist\WindowSwitcher.exe

if not defined DO_SETUP (
	echo [2/3] installer - skipped, pass --setup to build it
	goto install
)

echo [2/3] installer
REM Without --version the installer gets the version in WindowSwitcher.csproj.
if not defined VERSION for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content -Raw 'src\WindowSwitcher\WindowSwitcher.csproj')).Project.PropertyGroup.Version"`) do set "VERSION=%%v"
if not defined VERSION (
	echo ERROR: could not read the version from src\WindowSwitcher\WindowSwitcher.csproj.
	goto fail
)

REM Inno Setup's compiler: WINDOWSWITCHER_ISCC if set, else ISCC.exe on PATH, else the usual folders.
set "ISCC=%WINDOWSWITCHER_ISCC%"
if not defined ISCC for /f "delims=" %%p in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%p"
set "PF86=%ProgramFiles(x86)%"
if not defined ISCC if exist "%PF86%\Inno Setup 6\ISCC.exe" set "ISCC=%PF86%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
	echo ERROR: Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php, or set
	echo WINDOWSWITCHER_ISCC to its ISCC.exe.
	goto fail
)
if not exist "%ISCC%" (
	echo ERROR: "%ISCC%" does not exist.
	goto fail
)

del /q dist\WindowSwitcher-*-setup.exe 2>nul
"%ISCC%" /Q /DAppVersion=%VERSION% installer\WindowSwitcher.iss
if errorlevel 1 goto fail
echo Built %~dp0dist\WindowSwitcher-%VERSION%-setup.exe

:install
if not defined DO_INSTALL (
	echo [3/3] install - skipped, pass --install to install it for this user and start it with Windows
	goto done
)

echo [3/3] install
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" -Exe "%~dp0dist\WindowSwitcher.exe"
if errorlevel 1 goto fail

:done
popd
exit /b 0

:usage
echo Usage: build.cmd [--setup] [--install] [--version X.Y.Z]
echo.
echo   (no args)    build the Native AOT exe into dist\WindowSwitcher.exe.
echo   --setup      also build the installer, dist\WindowSwitcher-X.Y.Z-setup.exe, with Inno Setup 6
echo                  (found on PATH, in its usual folders, or at WINDOWSWITCHER_ISCC).
echo   --install    also install the exe for the current user:
echo                  - exit a running WindowSwitcher, as its tray menu's Exit does
echo                  - copy the exe to %%LOCALAPPDATA%%\Programs\WindowSwitcher
echo                    or WINDOWSWITCHER_INSTALL_DIR; WindowSwitcher.json there is kept
echo                  - start it with Windows: HKCU Run value "WindowSwitcher"
echo                    WINDOWSWITCHER_INSTALL_NO_STARTUP=1 skips this step
echo                  - start the installed copy
echo   --version    the version to stamp on the exe and the installer; without it, the version in
echo                  src\WindowSwitcher\WindowSwitcher.csproj is used.
echo   --help       this text.
popd
exit /b 2

:fail
set ERR=%ERRORLEVEL%
if "%ERR%"=="0" set ERR=1
echo.
echo BUILD FAILED ^(exit %ERR%^)
popd
exit /b %ERR%
