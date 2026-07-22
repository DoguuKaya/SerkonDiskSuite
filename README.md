# Serkon Disk Suite

Windows için açık kaynak **disk sağlığı, benchmark ve izleme** uygulaması.
CrystalDiskInfo, CrystalDiskMark ve HWiNFO'nun ihtiyaç duyulan özelliklerini
tek bir native uygulamada toplar. .NET 8 + WPF ile yazılmıştır.

> Bu proje, tek bir M.2 NVMe SSD'yi bir PCIe adaptör kart üzerinden ikinci disk
> olarak kuran, ardından sağlığını ve performansını doğrulamak isteyen gerçek bir
> ihtiyaçtan doğdu. Kullanılan tüm araçların özelliklerini tek çatı altında topluyor.

## Özellikler
- **Disk Sağlığı (SMART):** Sıcaklık, kalan ömür, açılma sayısı, güvensiz kapanma, tüm SMART öznitelikleri. NVMe ve SATA destekli.
- **Benchmark:** Sıralı/rastgele okuma-yazma testi (MB/s ve IOPS), cache-bypass ile gerçekçi sonuçlar.
- **Sistem Bilgisi:** İşletim sistemi, CPU, anakart, BIOS.
- **Modern koyu tema arayüz**, çoklu disk desteği.

## Ekran görüntüsü
_(Buraya derledikten sonra bir ekran görüntüsü ekleyin: `docs/screenshot.png`)_

## Gereksinimler
- Windows 10/11 (x64)
- Uygulama **yönetici olarak** çalışır (SMART/disk erişimi için gereklidir)
- Geliştirme için: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SMART okuma için: [smartmontools](https://www.smartmontools.org/) (`smartctl.exe`)

## SMART aracı kurulumu
1. [smartmontools](https://www.smartmontools.org/) indirin.
2. `smartctl.exe` ve beraberindeki DLL'leri proje kökündeki `tools/` klasörüne kopyalayın.
   (Bu dosyalar GPL lisanslı olduğu için repoya dahil edilmez; `.gitignore`'dadır.)

## Derleme ve çalıştırma
```powershell
git clone https://github.com/<kullanici>/SerkonDiskSuite.git
cd SerkonDiskSuite
dotnet restore
dotnet build
dotnet run --project src/SerkonDiskSuite.App
```

## Testler
```powershell
dotnet test
```

## Tek dosya (indirilebilir) exe üretme
```powershell
dotnet publish src/SerkonDiskSuite.App -c Release
```
Çıktı: `src/SerkonDiskSuite.App/bin/Release/net8.0-windows/win-x64/publish/SerkonDiskSuite.exe`

## Mimari
Katmanlı (Clean Architecture) yapı — ayrıntı için [`CLAUDE.md`](CLAUDE.md):

```
App (WPF/MVVM)  ->  Infrastructure (smartctl, WMI, benchmark)  ->  Core (domain, saf mantık)
```

## Uyarı
Disk yönetimi/benchmark işlemleri diske yazma yapar. Benchmark, hedef sürücüde geçici
bir dosya oluşturup siler. Önemli verilerinizin yedeğini almanız önerilir.

## Lisans
MIT — bkz. [`LICENSE`](LICENSE).
smartmontools ayrı olarak GPL lisanslıdır ve bu depoya dahil edilmez.

## Katkı
Issue ve pull request'ler açıktır. Yapılacaklar listesi `CLAUDE.md` içindedir.
