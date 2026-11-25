@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "CONFIG=Release"
set "PROFILE=FolderProfile"
set "PUBLISH_DIR=%~dp0publish\Release"
if defined CRIMSONX_PUBLISH_DIR set "PUBLISH_DIR=%CRIMSONX_PUBLISH_DIR%"

echo ============================================
echo  CrimsonX Release build
echo ============================================

where dotnet >nul 2>nul
if errorlevel 1 goto :no_dotnet

if not exist "Services\AppSecrets.cs" goto :no_secrets

echo [1/5] Restoring packages...
dotnet restore "CrimsonX.csproj" || goto :failed
if exist "CrimsonX.Tests\CrimsonX.Tests.csproj" (
    dotnet restore "CrimsonX.Tests\CrimsonX.Tests.csproj" || goto :failed
)

echo [2/5] Building Release...
dotnet build "CrimsonX.csproj" -c %CONFIG% --no-restore || goto :failed

echo [3/5] Running tests...
if exist "CrimsonX.Tests\CrimsonX.Tests.csproj" (
    dotnet test "CrimsonX.Tests\CrimsonX.Tests.csproj" -c %CONFIG% --no-restore || goto :failed
) else (
    echo     skipped: the test suite is not part of this repository
)

echo [4/5] Publishing single-file Release...
dotnet publish "CrimsonX.csproj" -c %CONFIG% -p:PublishProfile=%PROFILE% || goto :failed

echo [5/5] Verifying publish output...
if not exist "%PUBLISH_DIR%\CrimsonX.exe" goto :missing_exe

del /q "%PUBLISH_DIR%\*.pdb" >nul 2>nul
dir /b "%PUBLISH_DIR%\*.pdb" >nul 2>nul
if not errorlevel 1 goto :pdb_left

for %%F in ("%PUBLISH_DIR%\CrimsonX.exe") do echo Published: %%~fF (%%~zF bytes)
echo.
echo Build complete.
goto :success

:no_dotnet
echo ERROR: dotnet was not found in PATH.
goto :failed

:no_secrets
echo ERROR: Services\AppSecrets.cs is missing.
echo Copy Services\AppSecrets.cs.example and fill it in before building.
goto :failed

:missing_exe
echo ERROR: Publish did not produce "%PUBLISH_DIR%\CrimsonX.exe".
goto :failed

:pdb_left
echo ERROR: PDB files are still present in "%PUBLISH_DIR%".
dir /b "%PUBLISH_DIR%\*.pdb"
goto :failed

:failed
echo.
echo BUILD FAILED.
if /I not "%~1"=="/nopause" pause
exit /b 1

:success
if /I not "%~1"=="/nopause" pause
exit /b 0
