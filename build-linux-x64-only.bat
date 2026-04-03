@echo off
echo ================================================
echo Orbital Organizer - Linux x64 Quick Build (Avalonia)
echo ================================================
echo.

set /p VERSION=<version.txt
echo Building version: %VERSION%
echo.

set OUTPUT_DIR=_releases\OrbitalOrganizer.%VERSION%-linux-x64

if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"

dotnet publish OrbitalOrganizer.AvaloniaUI\OrbitalOrganizer.AvaloniaUI.csproj -c Release --self-contained true -r linux-x64 -p:PublishSingleFile=false -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error

xcopy /E /I /Y tools "%OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%OUTPUT_DIR%\" >nul

echo.
echo ================================================
echo Build completed: %OUTPUT_DIR%
echo ================================================
echo.
echo This is a self-contained build (no runtime install needed).
echo.
goto :end

:error
echo.
echo Build failed! See errors above.
pause
exit /b 1

:end
pause
