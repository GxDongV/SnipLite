@echo off
setlocal
pushd "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Install the .NET 10 SDK, then run this script again.
    popd
    pause
    exit /b 1
)
dotnet build "src\SnipLite\SnipLite.csproj" -c Release --nologo
if errorlevel 1 (
    popd
    pause
    exit /b 1
)
start "" "%~dp0src\SnipLite\bin\Release\net10.0-windows10.0.19041.0\SnipLite.exe"
popd
