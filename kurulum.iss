#ifndef Surum
#define Surum "1.0.0"
#endif
[Setup]
AppId={{7C1D6B3E-4F7A-4B52-9A0E-3D5F8A1B2C40}
AppName=HukukERP
AppVersion={#Surum}
AppPublisher=HukukERP
CreateAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
WizardStyle=modern
SetupIconFile=icon.ico
OutputDir=Releases
OutputBaseFilename=HukukERP-Kurulum
Compression=lzma2
SolidCompression=yes
Uninstallable=no

[Messages]
SetupAppTitle=Kurulum
SetupWindowTitle=HukukERP Kurulumu
WelcomeLabel1=HukukERP kurulum sihirbazına hoş geldiniz
WelcomeLabel2=Bu sihirbaz HukukERP'yi bilgisayarınıza kuracak. Masaüstü ve Başlat menüsüne kısayol eklenir; yeni sürümler açılışta bildirilir. Verileriniz güncellemelerde korunur.%n%nDevam etmek için İleri'ye tıklayın.
ButtonNext=İleri >
ButtonInstall=Kur
ButtonFinish=Bitir
ButtonCancel=İptal
InstallingLabel=HukukERP kuruluyor, lütfen bekleyin...
FinishedHeadingLabel=Kurulum tamamlandı
FinishedLabelNoIcons=HukukERP kuruldu. Masaüstündeki veya Başlat menüsündeki HukukERP kısayolundan açabilirsiniz.
ExitSetupTitle=Kurulumdan çık
ExitSetupMessage=Kurulum tamamlanmadı. Çıkmak istiyor musunuz?

[Files]
Source: "Releases\HukukERP-win-Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
Filename: "{tmp}\HukukERP-win-Setup.exe"; Parameters: "--silent"; StatusMsg: "HukukERP kuruluyor..."; Flags: waituntilterminated
