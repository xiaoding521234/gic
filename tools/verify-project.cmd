@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "EDITOR_EXE=%~1"
if not defined EDITOR_EXE set "EDITOR_EXE=%TUANJIE_EDITOR%"
if not defined EDITOR_EXE (
    echo Usage: %~nx0 "C:\path\to\Tuanjie.exe" 1>&2
    exit /b 2
)
if not exist "%EDITOR_EXE%" (
    echo Tuanjie executable not found: "%EDITOR_EXE%" 1>&2
    exit /b 2
)
for %%I in ("%~dp0..") do set "PROJECT_DIR=%%~fI"
set "LOG_DIR=%PROJECT_DIR%\Logs"
if not exist "%LOG_DIR%\" mkdir "%LOG_DIR%"
if not exist "%LOG_DIR%\" exit /b 2
set "LOG_FILE=%LOG_DIR%\gic-verify-%RANDOM%-%RANDOM%.log"
if exist "%LOG_FILE%" exit /b 2
echo Running project verification. Log: "%LOG_FILE%"
start "" /wait "%EDITOR_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_DIR%" -logFile "%LOG_FILE%" -executeMethod GIC.Editor.ProjectVerification.RunBatch
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
    echo FAIL: Tuanjie exited with code %RESULT%. See "%LOG_FILE%". 1>&2
    exit /b %RESULT%
)
findstr /l /c:"[GIC.Verify] PASS" "%LOG_FILE%" >nul
if errorlevel 1 (
    echo FAIL: verification did not report success. See "%LOG_FILE%". 1>&2
    exit /b 1
)
echo PASS: project verification completed.
exit /b 0
