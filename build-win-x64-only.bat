@echo off
echo ================================================
echo Orbital Organizer - Windows x64 Quick Build (WPF)
echo ================================================
echo.

set /p VERSION=<version.txt
echo Building version: %VERSION%
echo.

set OUTPUT_DIR=_releases\OrbitalOrganizer.%VERSION%-win-x64

if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"

dotnet publish OrbitalOrganizer.WPF\OrbitalOrganizer.WPF.csproj -c Release -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error

xcopy /E /I /Y tools "%OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%OUTPUT_DIR%\" >nul

echo.
echo ================================================
echo Build completed: %OUTPUT_DIR%
echo ================================================
echo.
echo Requires .NET 8 Desktop Runtime to run.
echo.
goto :end

:error
echo.
echo Build failed! See errors above.
pause
exit /b 1

:end
pause
