# PROGRESS — Serkon Disk Suite

Bu dosya oturumlar arası devamlılık için tutulur. Her anlamlı adımdan sonra
güncellenir ve commit atılır. Yeni bir oturuma başlarken önce bu dosyayı oku.

## Tamamlanan işler

### 1. BUG: Disk listesi boş, "Hata: Not found" (ÇÖZÜLDÜ — 2026-07-22)

**Kök neden:** `WmiDiskProvider.GetDriveLetters` içinde, disk `DeviceID`
değeri (`\\.\PHYSICALDRIVE0`) WQL `ASSOCIATORS OF {...}` sorgusuna
gömülmeden önce `.Replace("\\", "\\\\")` ile her ters eğik çizgi
kaçışlanıyordu (çift ters eğik çizgiye çevriliyordu). DeviceID zaten ters
eğik çizgi içerdiğinden bu fazladan kaçış, sorguyu bozup WMI'dan
`ManagementException: "Not found"` (WBEM_E_NOT_FOUND, 0x80041002)
fırlatılmasına yol açıyordu. Bu istisna `WmiDiskProvider.GetDisksAsync()`
içinde yakalanmadığı için `MainViewModel.LoadAsync()`'in genel catch
bloğuna düşüyor, `Disks` koleksiyonu hiç doldurulmadan boş kalıyor ve
`StatusMessage` alanına `"Hata: Not found"` yazılıyordu.

**Doğrulama:**
- PowerShell üzerinden `Get-CimInstance` ile hem kaçışlı (hatalı) hem
  kaçışsız (doğru) `ASSOCIATORS OF` sorgusu çalıştırıldı; kaçışlı sorgu
  birebir aynı `"Not found"` hatasını verdi, kaçışsız sorgu partition
  listesini doğru döndürdü.
- Düzeltme sonrası `%TEMP%` altında geçici bir konsol harness'ı ile
  `WmiDiskProvider.GetDisksAsync()` doğrudan çağrıldı: 1 disk bulundu,
  sürücü harfleri (`C:`, `Q:`) doğru şekilde çözümlendi. Harness iş
  bitince silindi (repoya dahil değil).
- `dotnet build`: 0 hata / 0 uyarı.
- `dotnet test`: 16/16 test başarılı (Core testleri).

**Değişiklik:** `src/SerkonDiskSuite.Infrastructure/Wmi/WmiDiskProvider.cs`
— `diskId` artık `DeviceID`'yi olduğu gibi kullanıyor, ekstra escape
kaldırıldı.

**Ayrıca doğrulanan (bug değil, bilgi amaçlı):** `SmartctlSmartProvider`
`disk.DevicePath`'i `ProcessStartInfo.ArgumentList` üzerinden smartctl'e
geçiriyor; bu, shell araya girmediği için backslash kaçışı sorunu
yaşamıyor. Yükseltilmemiş (non-admin) bir kabuktan `smartctl.exe -a
--json=c "\\.\PHYSICALDRIVE0"` çalıştırıldığında argv doğru
(`\\.\PHYSICALDRIVE0`) iletiliyor ama "Unable to detect device type"
hatası dönüyor — bu, yönetici hakkı olmadığı için beklenen bir durum
(app.manifest zaten `requireAdministrator` istiyor). Yönetici olarak
çalıştırıldığında SMART verisi okunmalı; bir sonraki adımda gerçek
uygulama üzerinden (yönetici hakkıyla) doğrulanacak.

### 2. BUG: Benchmark sekmesi hiç çalışmıyordu (ÇÖZÜLDÜ — 2026-07-22)

**Kök neden:** `DiskBenchmarkRunner.RunSinglePass`, `File.OpenHandle(...)`
çağrısına her zaman `fileSize`'ı `preallocationSize` parametresi olarak
geçiriyordu. .NET çalışma zamanı, preallocation'ın yalnızca dosyayı
(yeniden) oluşturan modlarla (`Create`/`CreateNew`/`Truncate`)
kullanılabileceğini şart koşuyor; `OpenOrCreate` (yazma geçişleri) veya
`Open` (okuma geçişleri) ile `preallocationSize > 0` verilirse
`ArgumentException: "Preallocation size can be requested only for new
files."` fırlatıyor. Sonuç: ilk `SequentialWrite` geçişinde anında
istisna, benchmark hiçbir zaman ilerlemiyordu.

**Doğrulama:**
- `%TEMP%` altında geçici bir harness ile gerçek `DiskBenchmarkRunner`
  4 MiB'lık bir test dosyasıyla çalıştırıldı; düzeltmeden önce yukarıdaki
  `ArgumentException` birebir reprodüklendi.
- Düzeltme: yazma geçişlerinde `FileMode.Create` + `preallocationSize =
  fileSize`; okuma geçişlerinde `FileMode.Open` + `preallocationSize = 0`.
- Aynı harness'le tekrar çalıştırıldı: 4 test türü de (SequentialWrite/
  Read, RandomWrite/Read) başarıyla tamamlandı, gerçekçi throughput/IOPS
  değerleri döndü. Harness iş bitince silindi (repoya dahil değil).
- `dotnet build` (tüm çözüm): 0 hata / 0 uyarı.
- `dotnet test`: 16/16 test başarılı.

**Değişiklik:**
`src/SerkonDiskSuite.Infrastructure/Benchmark/DiskBenchmarkRunner.cs`

### 3. ÖZELLİK: PCIe link speed/width tespiti (TAMAMLANDI — 2026-07-22)

`DiskInfo.TransferMode` NVMe diskler için artık dolduruluyor (ör.
`"PCIe 3.0 x4 (maks. PCIe 4.0 x4)"`).

**Yaklaşım:** PCIe link hızı/genişliği (`DEVPKEY_PciDevice_CurrentLinkSpeed`
/ `CurrentLinkWidth` / `MaxLinkSpeed` / `MaxLinkWidth`) klasik WMI
(System.Management) üzerinden sorgulanamıyor; bu özellik disk PNP
cihazının üstündeki gerçek PCI denetleyici (NVMe controller) cihazında
bulunuyor ve `DEVPKEY_Device_Parent` zinciri izlenerek bulunmalı.
`smartctl` deseninde olduğu gibi, Windows'un kendi getirdiği `PnpDevice`
PowerShell modülü (`Get-PnpDeviceProperty`) alt süreç olarak
çalıştırılıp JSON çıktısı parse ediliyor — ham SetupAPI P/Invoke'a göre
çok daha az riskli.

Yeni dosya: `src/SerkonDiskSuite.Infrastructure/Wmi/PcieLinkInfoReader.cs`

**Yol boyunca bulunup düzeltilen 2 yan bug:**
1. `WmiDiskProvider.MapBusType`, NVMe tespitini yalnızca model adında
   "NVMe" geçip geçmediğine bakarak yapıyordu. Gerçek donanımda
   (`KINGSTON SNV2S1000G`) model adı "NVMe" içermiyor ve WMI
   `InterfaceType` "SCSI" dönüyor, bu yüzden disk yanlışlıkla
   `DiskBusType.Scsi` olarak işaretleniyordu. Düzeltme: `PNPDeviceID`
   içindeki `VEN_NVME` işareti de artık kontrol ediliyor (WMI'nin NVMe
   diskleri SCSI miniport soyutlamasıyla sunmasının standart izi).
2. `PcieLinkInfoReader` ilk halinde `powershell.exe -Command "<script>"
   <instanceId>` şeklinde instanceId'yi ayrı bir process argümanı olarak
   geçiriyordu. PowerShell'de `-Command`'den sonraki TÜM argümanlar tek
   bir script metni olarak birleştirilip çalıştırılır; ayrı bir
   parametreye bağlanmaz. Bu yüzden `PNPDeviceID` içindeki `&`
   karakterleri PowerShell operatörü sanılıp `ParserError` fırlatıyordu.
   Düzeltme: instanceId artık tek-tırnaklı bir PowerShell string
   literali olarak script metninin içine gömülüyor.

**Doğrulama:** Gerçek makinede (`KINGSTON SNV2S1000G` NVMe SSD) hem
`Get-PnpDeviceProperty` zincirinin bağımsız PowerShell testiyle hem de
`WmiDiskProvider.GetDisksAsync()` üzerinden uçtan uca harness ile
doğrulandı: `BusType=Nvme`, `TransferMode="PCIe 3.0 x4 (maks. PCIe 4.0
x4)"`. `dotnet build`: 0 hata/uyarı. `dotnet test`: 16/16 başarılı.

## Devam eden iş

- Yok.

## Sıradaki işler (öncelik sırasına göre)

1. **SMART verilerinin arayüzde doğru görünmesini test et** — uygulamayı
   yönetici olarak çalıştırıp (UAC nedeniyle bu adım kullanıcı
   etkileşimi/manuel çalıştırma gerektirebilir) `HealthViewModel`'in
   gerçek SMART verisiyle dolduğunu doğrula. **Bu ajan oturumunda
   yükseltilmemiş (non-admin) kabuk yüzünden yapılamadı — kullanıcının
   uygulamayı yönetici olarak çalıştırıp kontrol etmesi gerekiyor.**
2. Kalan CLAUDE.md genişletme fikirleri (sırayla):
   - Gerçek zamanlı sıcaklık grafiği (LiveCharts2)
   - SMART verisini periyodik loglama + trend
   - Firmware güncelleme uyarısı
   - Çoklu dil desteği

## Bilinen buglar

- Yok (aktif olarak bilinen başka bug yok; SMART/Benchmark sekmeleri henüz
  gerçek donanımda uçtan uca test edilmedi).

## Notlar

- Proje bu oturumda git ile başlatıldı (`git init`) — daha önce `.git`
  yoktu. `tools/smartctl.exe` `.gitignore`'da, repoya girmiyor (GPL
  lisansı nedeniyle kullanıcı kendi indiriyor).
- WPF uygulaması `requireAdministrator` istediğinden, bu ajan oturumunda
  (yükseltilmemiş kabuk) doğrudan UI üzerinden UAC promptu geçilip
  görsel doğrulama yapılamadı; doğrulama `WmiDiskProvider` seviyesinde
  doğrudan API çağrısıyla yapıldı (yukarıya bakın).
