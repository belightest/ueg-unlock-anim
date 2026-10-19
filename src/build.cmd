@echo off
REM ============================================================
REM  UEG unlock animation console - one-click build
REM  Uses the csc.exe shipped with .NET Framework 4.x.
REM  No downloads, no NuGet, no third-party tools.
REM  Output: ..\UEGUnlockConfig.exe
REM  NOTE: keep this file ASCII + CRLF (cmd.exe batch rule)
REM ============================================================
setlocal
set SRC=%~dp0UEGUnlockConfig.cs
set OUT=%~dp0..\UEGUnlockConfig.exe
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
  pause
  exit /b 1
)
set HOSTOUT=%~dp0..\UEGHost.exe
set HOSTSRC=%~dp0UEGHost.cs

echo Building UEGUnlockConfig.exe ...
REM Microsoft.Win32.Registry lives in mscorlib on .NET Framework - no extra reference needed
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /out:"%OUT%" "%SRC%"
if errorlevel 1 (
  echo.
  echo [FAILED] See the compiler errors above.
  pause
  exit /b 1
)
echo [OK] %OUT%

echo Building UEGHost.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /out:"%HOSTOUT%" "%HOSTSRC%"
if errorlevel 1 (
  echo.
  echo [FAILED] See the compiler errors above.
  pause
  exit /b 1
)
echo [OK] %HOSTOUT%

REM ============================================================
REM  Package: dist\ueg-unlock-anim-<VER>-win64.zip
REM  A ready-to-run bundle (both exes + ueg assets + voice + config
REM  template + PS fallback host) so end users download ONE file,
REM  unzip and run UEGUnlockConfig.exe. Whitelist copy keeps personal
REM  files (photo.jpg / config.txt / run.log / go.txt / edge-profile)
REM  out of the package by construction. Bump VER for each release.
REM ============================================================
echo Packaging full bundle ...
set VER=1.0.3
set STAGE=%~dp0..\dist\stage
set PKG=%~dp0..\dist\ueg-unlock-anim-%VER%-win64.zip
REM clear the staging dir left by the previous run (build scratch only)
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%\ueg\assets" "%STAGE%\ueg\voice"
copy /Y "%HOSTOUT%" "%STAGE%\" >nul
copy /Y "%OUT%" "%STAGE%\" >nul
copy /Y "%~dp0..\host.ps1" "%STAGE%\" >nul
copy /Y "%~dp0..\ueg\unlock.html" "%STAGE%\ueg\" >nul
copy /Y "%~dp0..\ueg\ueg.html" "%STAGE%\ueg\" >nul
copy /Y "%~dp0..\ueg\assets\*.png" "%STAGE%\ueg\assets\" >nul
copy /Y "%~dp0..\ueg\assets\*.js" "%STAGE%\ueg\assets\" >nul
copy /Y "%~dp0..\ueg\voice\*.wav" "%STAGE%\ueg\voice\" >nul
REM docs / template: present in the repo checkout, absent in a bare install dir
for %%F in (config.example.txt README.md LICENSE NOTICE CREDITS.md) do (
  if exist "%~dp0..\%%F" copy /Y "%~dp0..\%%F" "%STAGE%\" >nul
)
if not exist "%STAGE%\UEGHost.exe" (
  echo [WARN] staging incomplete, package skipped ^(exes above are still fine^)
) else (
  powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%PKG%' -Force -CompressionLevel Fastest"
  if exist "%PKG%" (
    echo [OK] %PKG%
  ) else (
    echo [WARN] Compress-Archive failed ^(exes above are still fine^)
  )
)
pause
