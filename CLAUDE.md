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
- Sıcaklık grafiği (gerçek zamanlı izleme, LiveCharts2)
- PCIe link speed/width tespiti (şu an TransferMode boş)
- Disk format/partition işlemleri (Disk Management özelliği) — dikkatli, yıkıcı işlem
- SMART verisini periyodik loglama + trend
- Firmware güncelleme uyarısı (üretici API'leri)
- Çoklu dil (şu an Türkçe UI)

## Kod stili
- Nullable etkin, warnings-as-errors (Core/Infrastructure).
- Türkçe yorum ve UI metinleri, İngilizce tip/üye isimleri.
- ViewModel'ler `[ObservableProperty]` / `[RelayCommand]` source generator kullanır.
