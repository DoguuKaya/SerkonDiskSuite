; Serkon Disk Suite — Inno Setup betiği.
; Derleme: ISCC installer\setup.iss (veya Inno Setup IDE ile açıp derle).
; Önce yayın çıktısı üretilmiş olmalı:
;   dotnet publish src/SerkonDiskSuite.App -c Release
; smartctl.exe KASITLI OLARAK paketlenmiyor (GPL lisansı — kullanıcı
; smartmontools'u kendi indirir, bkz. README.md). Uygulama eksikse
; açılışta kullanıcıyı bilgilendiren bir uyarı gösterir (App.xaml.cs).
;
; Sürüm AppVersion tanımıyla dışarıdan geçirilebilir:
;   ISCC /DAppVersion=1.0.0 installer\setup.iss

#ifndef AppVersion
  #define AppVersion "1.0.4"
#endif

#define AppName "Serkon Disk Suite"
#define AppPublisher "Serkon"
#define AppExeName "SerkonDiskSuite.exe"
#define PublishDir "..\src\SerkonDiskSuite.App\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{6E9F0D5C-6E52-4C82-9A1B-6E7C2C1A3B7D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=output
OutputBaseFilename=SerkonDiskSuite-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Uygulama zaten app.manifest ile requireAdministrator istiyor; kurulum da
; Program Files'a yazdığından yönetici hakkı gerekir.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\SerkonDiskSuite.App\Assets\app.ico

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Yalnızca tek dosya yayın çıktısı paketlenir — tools\ klasörü (smartctl.exe
; içerebilir) KASITLI OLARAK dahil edilmiyor, lisans nedeniyle kullanıcı
; kendi indirir.
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; SORUN 4 (v1.0.0 gerçek kullanıcı raporu): kurulum sonrası "Programı çalıştır"
; kutusuyla uygulama açılırken bazı kullanıcılarda "CreateProcess tamamlanamadı:
; kod 740 (ERROR_ELEVATION_REQUIRED)" hatası alınıyordu. app.manifest
; requireAdministrator istediğinden, shellexec OLMADAN varsayılan CreateProcess
; çağrısı — kurulum süreci kendisi zaten yönetici olarak çalışsa bile bazı
; ortamlarda (ör. kurulumun beklenmeyen bir şekilde yükseltilmemiş bir token'la
; çalıştığı durumlar) elevation isteğini düzgün tetikleyemiyor. shellexec,
; Explorer'da çift tıklamış gibi ShellExecute üzerinden başlatır — bu, UAC
; onay istemini (gerekiyorsa) her koşulda doğru tetikleyen, resmi Inno Setup
; dokümantasyonunun (jrsoftware.org/ishelp/topic_runsection.htm) bu senaryo
; için önerdiği yöntemdir.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec
