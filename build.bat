@echo off
REM Gereken: .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
dotnet publish HukukErp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if errorlevel 1 (echo HATA: derleme basarisiz & pause & exit /b 1)
echo Bitti: dist\HukukERP.exe
echo Mevcut config.json ve .db dosyalarinizi exe ile ayni klasore koyun (uzerine YAZILMAZ).
pause
