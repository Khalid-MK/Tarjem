@echo off
REM Builds publish\Tarjem.exe  (needs the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0)
REM   build.bat            -> small exe, needs the .NET 8 Desktop Runtime on the machine
REM   build.bat standalone -> bigger exe that runs anywhere, no runtime needed

cd /d "%~dp0src"

if /i "%1"=="standalone" (
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%~dp0publish"
) else (
  dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%~dp0publish"
)

if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Done: %~dp0publish\Tarjem.exe
start "" "%~dp0publish"
