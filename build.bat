@echo off
chcp 65001 >nul
title ZETX Boost - Derleme
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto :nosdk
dotnet --list-sdks | findstr /r "^8\. ^9\. ^10\." >nul
if errorlevel 1 goto :nosdk

echo.
echo  === ZETX Boost derleniyor... (ilk seferde paketler indirilir) ===
echo.
dotnet nuget locals http-cache --clear >nul 2>nul
dotnet restore ZETXBoost.csproj --configfile "%~dp0nuget.config"
if errorlevel 1 goto :fail
dotnet publish ZETXBoost.csproj -c Release -o "%~dp0cikti" --no-restore
if errorlevel 1 goto :fail

echo.
echo  TAMAM!  cikti\ZETXBoost.exe hazir. Tek dosya, .NET kurulumu gerekmez.
echo.
explorer "%~dp0cikti"
pause
exit /b 0

:nosdk
echo.
echo  .NET 8 SDK bulunamadi. winget ile kuruluyor...
echo.
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
echo.
echo  Kurulum bitti. Bu pencereyi kapat ve build.bat dosyasini TEKRAR calistir.
echo  winget yoksa: https://dotnet.microsoft.com/download/dotnet/8.0 adresinden SDK x64 indir.
pause
exit /b 1

:fail
echo.
echo  DERLEME HATASI. Yukaridaki kirmizi satirlari kopyalayip Claude'a gonder.
pause
exit /b 1
