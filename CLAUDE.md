# CLAUDE.md — Serkon Disk Suite

Bu dosya, Claude Code'un bu projede çalışırken bağlamı hızlıca kavraması içindir.

## Proje nedir
Windows için açık kaynak bir **disk sağlığı + benchmark + izleme** masaüstü uygulaması.
CrystalDiskInfo (SMART), CrystalDiskMark (benchmark), HWiNFO (sistem/sıcaklık) ve
Disk Management (disk yönetimi) araçlarının ihtiyaç duyulan özelliklerini tek bir
native uygulamada toplar. .NET 8 + WPF (MVVM) ile yazılmıştır.

## Mimari (Clean Architecture)
- **SerkonDiskSuite.Core** — Domain: modeller, arayüzler, saf iş mantığı. Hiçbir dış bağımlılığı yok, `net8.0`. Test edilebilir.
- **SerkonDiskSuite.Infrastructure** — Donanım erişimi: smartctl sarmalayıcı, benchmark motoru, WMI. `net8.0-windows`.
- **SerkonDiskSuite.App** — WPF UI, MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.DependencyInjection).
- **SerkonDiskSuite.Tests** — Core mantığının xUnit birim testleri.

Bağımlılık yönü daima içe doğrudur: App -> Infrastructure -> Core. Core hiçbir şeye bağımlı değildir.

## Önemli tasarım kararları
1. **SMART verisi smartctl ile okunur** (kendi DeviceIoControl kodumuz değil). Olgun, binlerce diski destekleyen araç. `tools/smartctl.exe` olarak dağıtılır. `--json=c` çıktısı parse edilir.
2. **Benchmark kendi motorumuz.** Cache-bypass için `FILE_FLAG_NO_BUFFERING (0x20000000)` + `WriteThrough`. Erişimler sektör (4096 bayt) hizalı olmalı.
3. **Yönetici hakkı gerekir** (SMART/disk erişimi). `app.manifest` UAC yükseltmesi ister.
4. **CPU/GPU/RAM izleme LibreHardwareMonitorLib ile okunur.** `IHardwareMonitorProvider` ->
   `LibreHardwareMonitorProvider`. Bulunamayan sensörler için `null` döner, tahmini değer
   üretilmez (bkz. Bilinen kısıtlar).

## Bilinen kısıtlar (kodda düzeltilemez)

**CPU/anakart sıcaklığı VBS/Bellek Bütünlüğü (Memory Integrity, HVCI) etkinken okunamaz.**
Windows 11'in Sanallaştırma Tabanlı Güvenlik (VBS) özelliği etkinken (Görev Yöneticisi'nde
"Sanallaştırma: Etkin"), LibreHardwareMonitor'ün kullandığı imzasız WinRing0 çekirdek
sürücüsü MSR (Model-Specific Register) kayıtlarına erişemez — bu, Windows'un kasıtlı bir
güvenlik sınırıdır, yönetici hakkı bile bunu aşamaz ve uygulama kodunda düzeltilemez.

Doğrulama: [LibreHardwareMonitor GitHub issue #566](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/566)
("Incorrect watts and clock speed readings ... when Core Isolation / Memory Integrity is
enabled") 2021'den beri açık, `bug`+`help wanted` etiketli, 2026-03'te hâlâ güncel yorum
alıyor. Bir katılımcının gözlemi: Windows Güvenliği'nden "Bellek Bütünlüğü"nü kapatmak
sorunu çözüyor; imzasız sürücülerin MSR erişimi VBS/HVCI tarafından bloklanıyor (HWiNFO
gibi araçların bunu VBS açıkken okuyabilmesi özel olarak imzalanmış bir sürücü kullanmaları
sayesinde olabilir — LibreHardwareMonitor'ün WinRing0'ı bu şekilde imzalı değil).

Uygulama bunu şu şekilde ele alıyor: `IVbsStatusProvider`/`WmiVbsStatusProvider` (WMI
`Win32_DeviceGuard.SecurityServicesRunning`, kod 2 = HVCI) VBS/Bellek Bütünlüğü'nün
ÇALIŞIP çalışmadığını tespit ediyor; tespit edilebiliyorsa Sistem sekmesindeki "Bu
sistemde okunamıyor" mesajı bunu açıklayan bir metne dönüşüyor ("...VBS etkin — çekirdek
sürücüsü sıcaklık sensörüne erişemez"). Tespit edilemezse (WMI sınıfı yoksa vb.) genel
mesaj kullanılır.

## Kapsam dışı (kalıcı olarak kapatıldı, bu depoya eklenmeyecek)

- **Disk format/partition işlemleri (Disk Management özelliği).** Yıkıcı işlem riski
  nedeniyle kasıtlı olarak dışarıda tutuluyor.
- **Firmware güncelleme (üretici API'leri).** Kasıtlı olarak dışarıda tutuluyor.

## Kurulum / geliştirme komutları
```powershell
dotnet restore
dotnet build
dotnet test                      # Core birim testleri
dotnet run --project src/SerkonDiskSuite.App
```

## Yayın (tek dosya exe)
```powershell
dotnet publish src/SerkonDiskSuite.App -c Release
# Çıktı: bin/Release/net8.0-windows/win-x64/publish/SerkonDiskSuite.exe
```

## smartctl kurulumu (geliştirme için)
smartmontools'u https://www.smartmontools.org/ adresinden indirin, `smartctl.exe`
(ve gerekli DLL'leri) proje kökündeki `tools/` klasörüne kopyalayın. Bu dosyalar
`.gitignore`'da olduğu için repoya girmez; kullanıcı kendi indirir (GPL lisansı).

## Yapılacaklar / genişleme fikirleri
- NVMe self-test ÇALIŞIRKEN kalan yüzdeyi taşıyan JSON alanı bu makinede/donanımda
  doğrulanamadıysa (bkz. PROGRESS.md ilgili madde), gerçek bir self-testin tam
  ilerleme döngüsü izlenerek alan adı bulunmalı.
- Çoklu dil (şu an Türkçe UI) — kullanıcı kararıyla bu turlarda atlandı, öncelikli değil.

Disk format/partition ve firmware güncelleme **kapsam dışı** — bkz. yukarıdaki
"Kapsam dışı" bölümü, bu turlarda tekrar önerilmemeli.

Tamamlananlar (sıcaklık grafiği + trend loglama, PCIe link speed/width, Teşhis
sayfası/self-test, rapor dışa aktarma, hazır benchmark profilleri, WPF-UI migrasyonu,
CPU/GPU/RAM canlı donanım izleme) için bkz. `PROGRESS.md`.

## Kod stili
- Nullable etkin, warnings-as-errors (Core/Infrastructure).
- Türkçe yorum ve UI metinleri, İngilizce tip/üye isimleri.
- ViewModel'ler `[ObservableProperty]` / `[RelayCommand]` source generator kullanır.
