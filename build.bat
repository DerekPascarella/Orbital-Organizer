@echo off
echo ================================================
echo Orbital Organizer Build Script
echo ================================================
echo.

REM Read version from version.txt
set /p VERSION=<version.txt

echo Building version: %VERSION%
echo.

REM Format code
echo Formatting code...
dotnet format OrbitalOrganizer.sln
if %ERRORLEVEL% neq 0 goto :error
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist "_releases" rd /s /q "_releases"
mkdir "_releases"

REM Build for Windows x64 (WPF - framework-dependent)
echo.
echo ================================================
echo Building WPF for Windows x64...
echo ================================================
set OUTPUT_DIR=_releases\OrbitalOrganizer.%VERSION%-win-x64
dotnet publish OrbitalOrganizer.WPF\OrbitalOrganizer.WPF.csproj -c Release -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y tools "%OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%OUTPUT_DIR%\" >nul
cd "%OUTPUT_DIR%" && tar -a -c -f ..\OrbitalOrganizer.%VERSION%-win-x64.zip * && cd ..\..
if %ERRORLEVEL% neq 0 echo Warning: Failed to create zip file for win-x64
echo Build completed for win-x64

REM Build for Windows x86 (WPF - framework-dependent)
echo.
echo ================================================
echo Building WPF for Windows x86...
echo ================================================
set OUTPUT_DIR=_releases\OrbitalOrganizer.%VERSION%-win-x86
dotnet publish OrbitalOrganizer.WPF\OrbitalOrganizer.WPF.csproj -c Release -r win-x86 --self-contained false -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y tools "%OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%OUTPUT_DIR%\" >nul
cd "%OUTPUT_DIR%" && tar -a -c -f ..\OrbitalOrganizer.%VERSION%-win-x86.zip * && cd ..\..
if %ERRORLEVEL% neq 0 echo Warning: Failed to create zip file for win-x86
echo Build completed for win-x86

REM Purge intermediate build output before cross-platform builds to prevent
REM stale Windows-only native libs from leaking into non-Windows packages.
echo.
echo Cleaning intermediate output...
if exist "OrbitalOrganizer.Core\bin" rd /s /q "OrbitalOrganizer.Core\bin"
if exist "OrbitalOrganizer.Core\obj" rd /s /q "OrbitalOrganizer.Core\obj"
if exist "OrbitalOrganizer.AvaloniaUI\bin" rd /s /q "OrbitalOrganizer.AvaloniaUI\bin"
if exist "OrbitalOrganizer.AvaloniaUI\obj" rd /s /q "OrbitalOrganizer.AvaloniaUI\obj"

REM Build for linux-x64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for linux-x64...
echo ================================================
set OUTPUT_DIR=_releases\OrbitalOrganizer.%VERSION%-linux-x64
dotnet publish OrbitalOrganizer.AvaloniaUI\OrbitalOrganizer.AvaloniaUI.csproj -c Release --self-contained true -r linux-x64 -p:PublishSingleFile=false -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y tools "%OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%OUTPUT_DIR%\" >nul
cd _releases && tar -czf OrbitalOrganizer.%VERSION%-linux-x64.tar.gz OrbitalOrganizer.%VERSION%-linux-x64 && cd ..
echo Build completed for linux-x64

REM Build for osx-x64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for osx-x64...
echo ================================================
set TEMP_OUTPUT_DIR=_releases\temp-osx-x64
set OUTPUT_DIR=_releases
dotnet publish OrbitalOrganizer.AvaloniaUI\OrbitalOrganizer.AvaloniaUI.csproj -c Release --self-contained true -r osx-x64 -p:PublishSingleFile=false -o "%TEMP_OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y tools "%TEMP_OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%TEMP_OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%TEMP_OUTPUT_DIR%\" >nul
echo Creating macOS .app bundle...
wsl bash create-macos-bundle.sh "_releases/temp-osx-x64" "%VERSION%" "_releases"
if %ERRORLEVEL% neq 0 goto :error
rd /s /q "%TEMP_OUTPUT_DIR%" 2>nul
echo Build completed for osx-x64

REM Build for osx-arm64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for osx-arm64...
echo ================================================
set TEMP_OUTPUT_DIR=_releases\temp-osx-arm64
set OUTPUT_DIR=_releases
dotnet publish OrbitalOrganizer.AvaloniaUI\OrbitalOrganizer.AvaloniaUI.csproj -c Release --self-contained true -r osx-arm64 -p:PublishSingleFile=false -o "%TEMP_OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y tools "%TEMP_OUTPUT_DIR%\tools\"
if exist LICENSE copy /Y LICENSE "%TEMP_OUTPUT_DIR%\" >nul
if exist README.md copy /Y README.md "%TEMP_OUTPUT_DIR%\" >nul
echo Creating macOS .app bundle (arm64)...
wsl bash create-macos-bundle.sh "_releases/temp-osx-arm64" "%VERSION%" "_releases" "arm64"
if %ERRORLEVEL% neq 0 goto :error
rd /s /q "%TEMP_OUTPUT_DIR%" 2>nul
echo Build completed for osx-arm64

echo.
echo ================================================
echo All builds completed successfully!
echo ================================================
echo.
echo Release files are in the _releases directory:
dir /B _releases\*.zip _releases\*.tar.gz 2>nul
echo.
echo NOTE: Windows builds require .NET 8 Desktop Runtime.
echo       Linux and macOS builds are self-contained.
echo.
goto :end

:error
echo.
echo ================================================
echo Build failed! See errors above.
echo ================================================
pause
exit /b 1

:end
echo Build process finished.
pause
