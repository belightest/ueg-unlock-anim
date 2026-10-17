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
pause
