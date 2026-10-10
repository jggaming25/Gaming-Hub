@echo off
setlocal
cd /d "%~dp0"

echo Publishing GamingHub single-file exe...
dotnet publish src\GamingHub.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o build
if errorlevel 1 goto :err

copy /y build\GamingHub.App.exe GamingHub.exe >nul

echo.
echo Fertig! Die App liegt hier:
echo %cd%\GamingHub.exe
exit /b 0

:err
echo.
echo Publish fehlgeschlagen.
exit /b 1