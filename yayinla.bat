@echo off
setlocal
REM TEK KOMUTLA YAYIN:  yayinla.bat 1.0.1
REM Bu komut: derler -> Setup+guncelleme paketini uretir -> GitHub'a yukler. Diger bilgisayarlar otomatik gorur.
REM Gerekenler: .NET 8 SDK, bos bir GitHub deposu (public onerilir; icinde yalniz program olur, veriniz YOK),
REM             ortam degiskeni GITHUB_TOKEN (GitHub > Settings > Developer settings > Personal access tokens, "repo" izni).
if "%1"=="" (echo Kullanim: yayinla.bat SURUM   ornek: yayinla.bat 1.0.1 & exit /b 1)
if not exist guncelleme.txt (
  set /p REPO="GitHub depo adresi (ornek https://github.com/kullanici/hukukerp): "
  >guncelleme.txt echo %REPO%
)
set /p REPO=<guncelleme.txt
if "%GITHUB_TOKEN%"=="" (echo HATA: GITHUB_TOKEN tanimli degil. setx GITHUB_TOKEN "ghp_..." yazip yeni pencere acin. & pause & exit /b 1)
dotnet tool install -g vpk --version 0.0.942 >nul 2>&1
dotnet publish HukukErp.csproj -c Release -r win-x64 --self-contained true -p:Version=%1 -o publish || goto :hata
vpk download github --repoUrl %REPO% --token %GITHUB_TOKEN% --outputDir Releases >nul 2>&1
vpk pack --packId HukukERP --packVersion %1 --packDir publish --mainExe HukukERP.exe --packTitle "HukukERP" --outputDir Releases || goto :hata
vpk upload github --repoUrl %REPO% --token %GITHUB_TOKEN% --outputDir Releases --publish --releaseName "HukukERP %1" --tag v%1 || goto :hata
echo.
echo TAMAM. Diger bilgisayarlar acilista/saat basi yeni surumu gorup kurulumu soracak.
echo Ilk kurulum icin Releases\HukukERP-win-Setup.exe dosyasini kullanin (GitHub Release sayfasindan da indirilebilir).
pause & exit /b 0
:hata
echo HATA olustu. Yukaridaki mesaja bakin.
pause & exit /b 1
