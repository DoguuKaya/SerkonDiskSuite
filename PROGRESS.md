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

## Devam eden iş

- Yok (bug fix tamamlandı, bir sonraki adıma geçiliyor).

## Sıradaki işler (öncelik sırasına göre)

1. **SMART verilerinin arayüzde doğru görünmesini test et** — uygulamayı
   yönetici olarak çalıştırıp (UAC nedeniyle bu adım kullanıcı
   etkileşimi/manuel çalıştırma gerektirebilir) `HealthViewModel`'in
   gerçek SMART verisiyle dolduğunu doğrula.
2. **Benchmark sekmesini doğrula** — `DiskBenchmarkRunner` ile gerçek bir
   okuma/yazma testi çalıştırıp sonuçların arayüze yansıdığını kontrol et.
3. CLAUDE.md genişletme fikirleri (sırayla):
   - PCIe link speed/width tespiti (şu an `TransferMode` boş)
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
