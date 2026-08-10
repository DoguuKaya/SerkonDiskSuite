# Serkon Disk Suite

Windows için açık kaynak **disk sağlığı, benchmark ve izleme** uygulaması.
CrystalDiskInfo, CrystalDiskMark ve HWiNFO'nun ihtiyaç duyulan özelliklerini
tek bir native uygulamada toplar. .NET 8 + WPF ile yazılmıştır.

> Bu proje, tek bir M.2 NVMe SSD'yi bir PCIe adaptör kart üzerinden ikinci disk
> olarak kuran, ardından sağlığını ve performansını doğrulamak isteyen gerçek bir
> ihtiyaçtan doğdu. Kullanılan tüm araçların özelliklerini tek çatı altında topluyor.

## Özellikler
- **Disk Sağlığı (SMART):** Sıcaklık, kalan ömür, açılma sayısı, güvensiz kapanma, tüm SMART öznitelikleri. NVMe ve SATA destekli.
- **Gerçek zamanlı sıcaklık grafiği** ve diskin tüm zamanlar trend geçmişi (sıcaklık + kalan ömür).
- **Teşhis:** SMART self-test başlatma (kısa/uzun) ve sonuçları, firmware sürümü, NVMe kritik uyarı bayrakları.
- **Benchmark:** Sıralı/rastgele okuma-yazma testi (MB/s ve IOPS), cache-bypass ile gerçekçi sonuçlar, hazır CrystalDiskMark profilleri (SEQ1M Q8T1, RND4K Q32T16 vb.) ve sıralı/rastgele için ayrı ayarlanabilir kuyruk derinliği (Q) / iş parçacığı (T).
- **Rapor dışa aktarma:** Disk + SMART + benchmark özetini metin/JSON olarak kaydetme veya panoya kopyalama.
- **Sistem Bilgisi:** İşletim sistemi, CPU, anakart, BIOS, RAM.
- **Gerçek zamanlı CPU/GPU/RAM izleme (HWiNFO'nun temel karşılığı):** CPU yük/sıcaklık grafiği,
  GPU yük/sıcaklık/VRAM (varsa), RAM kullanımı; CPU/GPU trend geçmişi loglanır.
- **Modern koyu tema arayüz**, çoklu disk desteği.

## Ekran görüntüsü
![Serkon Disk Suite](docs/SCREENSHOT.png)

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

## Bilinen kısıtlar
- **CPU/anakart sıcaklığı bazı sistemlerde okunamaz.** Windows 11'in Sanallaştırma Tabanlı
  Güvenlik (VBS) / Bellek Bütünlüğü (Memory Integrity) özelliği etkinken (Görev
  Yöneticisi'nde "Sanallaştırma: Etkin"), donanım izleme kütüphanesinin (LibreHardwareMonitor)
  kullandığı imzasız çekirdek sürücüsü sıcaklık kayıtlarına erişemez. Bu, Windows'un kasıtlı
  bir güvenlik sınırıdır — yönetici hakkı bile bunu aşamaz ve uygulamada düzeltilemez (bkz.
  [LibreHardwareMonitor issue #566](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/566)).
  Uygulama VBS'in etkin olduğunu tespit edebilirse bunu açıklayan bir mesaj gösterir.
- Disk format/partition işlemleri ve firmware güncelleme desteklenmez (kapsam dışı, bkz. `CLAUDE.md`).

## Uyarı
Disk yönetimi/benchmark işlemleri diske yazma yapar. Benchmark, hedef sürücüde geçici
bir dosya oluşturup siler. Önemli verilerinizin yedeğini almanız önerilir.

## Lisans
MIT — bkz. [`LICENSE`](LICENSE).
smartmontools ayrı olarak GPL lisanslıdır ve bu depoya dahil edilmez.

## Katkı
Issue ve pull request'ler açıktır. Yapılacaklar listesi `CLAUDE.md` içindedir.
