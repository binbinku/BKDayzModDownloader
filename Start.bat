@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 goto missing_runtime
dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 10." >nul
if errorlevel 1 goto missing_runtime
start "" "%~dp0BKDayzModDownloader.exe"
exit /b 0
:missing_runtime
echo This app requires .NET 10 Desktop Runtime x64.
echo The .NET SDK is NOT required on the server.
echo Download: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
echo Choose Windows - .NET Desktop Runtime - x64.
pause
exit /b 1
