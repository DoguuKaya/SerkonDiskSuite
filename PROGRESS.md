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

### 4. ARAYÜZ: Koyu tema stilleri + Türkçe etiketler + yerleşim düzeltmesi (TAMAMLANDI — 2026-08-04)

**Sorun:** `Resources/Theme.xaml` içinde DataGrid, TabControl, TextBox,
ScrollBar, ListBox için hiç stil yoktu; bu yüzden WPF'in varsayılan açık
tema renkleri koyu pencere zemininde kullanılıyor, özellikle DataGrid
hücrelerinde siyah yazı koyu satır zemini üzerinde okunmuyordu ve
TabItem başlıkları neredeyse görünmüyordu. Ayrıca SMART öznitelik
tablosunda ham alan adları ("nsid", "percentage_used", "data_units_read")
ve ham sayısal değerler olduğu gibi gösteriliyordu; sağlık sekmesindeki
DataGrid `MaxHeight="320"` ile sınırlıydı ve altında büyük boş alan
kalıyordu.

**Yapılan değişiklikler:**
- `Resources/Theme.xaml`: DataGridColumnHeader/DataGridRow/DataGridCell,
  TabControl/TabItem (tam özel `ControlTemplate`, seçili/hover/normal
  durumları ayrı), TextBox, ProgressBar, ScrollBar (ince özel `Track`/
  `Thumb` şablonu), ListBox/ListBoxItem için açık stiller eklendi.
  Yeni paleti (`HeaderBackgroundColor`, `RowHoverColor`,
  `RowSelectedColor`, `BorderMutedColor`, `AccentColor` #2563EB,
  `AccentTextColor` #60A5FA, `ErrorTextColor`, `DangerColor`) WCAG AA
  hedefiyle (gövde metni ≥4.5:1, büyük/kalın durum rozetleri ≥3:1)
  hesaplayıp doğruladım — oranlar dosyanın başındaki yorumda listeli
  (ör. TextColor/SurfaceAlt 11.73:1, TextMuted/SurfaceAlt 5.47:1,
  White/AccentColor 5.17:1, ErrorTextColor/Surface 5.83:1).
- `Formatting/DisplayFormatting.cs`, `SmartAttributeLabels.cs`,
  `SmartAttributeValueFormatter.cs` (yeni): SMART öznitelik ham adları
  için Türkçe sözlük (ör. "Kullanım Yüzdesi", "Okunan Veri Birimi";
  ham ad hücre tooltip'inde kalıyor) + `data_units_read` -> bayt ->
  "8,93 TB", `power_on_hours` -> "8.962 saat" gibi tr-TR yerelleştirmeli
  değer biçimlendirme.
- `Converters/SmartAttributeConverters.cs` (yeni) + `Converters.cs`'e
  `BusTypeToStringConverter`/`SolidStateToStringConverter` eklendi;
  `BytesToStringConverter` artık `DisplayFormatting.FormatBytes` kullanıyor
  (tr-TR virgüllü ondalık).
- `Views/MainWindow.xaml`: Sağlık sekmesi `ScrollViewer`+`StackPanel`
  yerine `Grid` (Auto/Auto/Auto/Auto/`*`) ile yeniden kuruldu; DataGrid
  artık kalan tüm dikey alanı dolduruyor (MaxHeight kaldırıldı). Hata
  metni yeni `ErrorText` stiliyle, iptal butonu yeni `DangerButton`
  stiliyle gösteriliyor; inline renk/stil override'ları (Background,
  BorderThickness vb.) kaldırılıp Theme.xaml'deki implicit stillere
  bırakıldı ki yeni stiller gerçekten devreye girsin.

**Doğrulama:** `dotnet build` (tüm çözüm): 0 hata/0 uyarı. `dotnet test`:
16/16 başarılı. **Görsel doğrulama kullanıcı tarafından elle yapılmalı**
— WPF arayüzünü göremiyorum; kontrast oranlarını yukarıdaki gibi
hesaplayarak doğruladım ama gerçek render'ı (font kalınlığı, TabItem
geçişleri, ScrollBar görünümü) uygulamayı açıp gözle kontrol etmeniz
gerekiyor.

### 5. ÖZELLİK: Disk detay şeridi (TAMAMLANDI — 2026-08-04)

Üst başlık ile sekmeler arasına, seçili diskin donanım bilgilerini
gösteren yatay bir şerit eklendi: Tür (SSD/HDD) + Arayüz (NVMe/SATA/...),
Bağlantı (TransferMode, ör. "PCIe 3.0 x4 (maks. PCIe 4.0 x4)"; disk
bilgisi yoksa bu rozet gizleniyor), Firmware sürümü, Seri No. Tüm alanlar
zaten `DiskInfo` modelinde vardı; sadece UI'da eksikti.
`Views/MainWindow.xaml` Grid satırları buna göre kaydırıldı (üst şerit
Auto, içerik `*`, durum çubuğu Auto).

**Doğrulama:** `dotnet build`: 0 hata/uyarı. `dotnet test`: 16/16 başarılı.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı.**

### 6. ÖZELLİK: Sağlık sekmesi ek özet kartları + AvailableSpare (TAMAMLANDI — 2026-08-04)

`SmartHealth`'e `AvailableSparePercent` (NVMe "available_spare") eklendi
ve `SmartctlSmartProvider`'da dolduruldu (`TryGetNvmeInt` yardımcı
metodu). Sağlık sekmesindeki özet kutuları 4'ten 9'a çıkarıldı
(3x3 `UniformGrid`): Durum, Sıcaklık, Kalan Ömür, Kullanılabilir Yedek,
Açılma Sayısı, Güvensiz Kapanma, Çalışma Süresi, Toplam Okunan, Toplam
Yazılan. Yeni `HoursToStringConverter`/`CountToStringConverter` tr-TR
binlik ayraçla biçimlendiriyor; bayt alanları mevcut `BytesToString`
converter'ını kullanıyor.

**Doğrulama:** `dotnet build`: 0 hata/uyarı. `dotnet test`: 16/16 başarılı.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** (gerçek NVMe
donanımında `available_spare` alanının smartctl JSON çıktısında
beklenen konumda geldiği varsayılıyor — bu ajan oturumunda canlı SMART
verisiyle test edilemedi).

### 7. ÖZELLİK: Benchmark IOPS gösterimi + blok boyutu seçimi (TAMAMLANDI — 2026-08-04)

Sonuç kartlarında rastgele testler için IOPS değeri artık gösteriliyor
(`BenchmarkResult.Iops` zaten hesaplanıyordu, UI'da eksikti). Kullanıcı
artık rastgele testler için blok boyutunu (4/8/16/32/64/128/256/512/1024
KiB) bir ComboBox'tan seçebiliyor (`BenchmarkViewModel.RandomBlockSizeKiB`
-> `BenchmarkOptions.RandomBlockSize`). Sıralı testlerin blok boyutu
değiştirilmedi (varsayılan 1 MiB'de sabit kaldı — kullanıcılar bunu
nadiren değiştiriyor, YAGNI).

**Yan iş:** `Theme.xaml`'e orijinal A listesinde olmayan ama bu adımda
gerekli olan bir `ComboBox`/`ComboBoxItem` koyu tema stili eklendi
(aksi halde yeni eklenen ComboBox aynı okunmazlık sorununu yaşardı).

**Doğrulama:** `dotnet build`: 0 hata/uyarı. `dotnet test`: 16/16
başarılı. **Görsel doğrulama kullanıcı tarafından elle yapılmalı** —
özellikle yeni ComboBox açılır listesinin (popup) doğru konumlandığı ve
okunabilir olduğu gözle kontrol edilmeli.

### 8. ÖZELLİK: Gerçek zamanlı sıcaklık grafiği (LiveCharts2) (TAMAMLANDI — 2026-08-04)

`LiveChartsCore.SkiaSharpView.WPF` (2.0.5) paketi `SerkonDiskSuite.App`'e
eklendi. `HealthViewModel` artık seçili disk için 5 saniyede bir SMART
okuyup sıcaklığı `lvc:CartesianChart` üzerinde çiziyor (son 15 dakika,
180 nokta penceresi). Arka plan döngüsü `CancellationTokenSource` ile
iptal edilebilir; `HealthViewModel` artık `IDisposable` (uygulama
kapanışında `App.xaml.cs`'teki `ServiceProvider.Dispose()` otomatik
çağırıyor). Sekme görünürlüğü `MainWindow.xaml.cs`'te
`HealthTabContent`'in `IsVisibleChanged` olayı + `Loaded` olayıyla
(ilk açılış durumu için) izlenip `HealthViewModel.SetMonitoringActive`
ile döngü başlatılıp durduruluyor — Sağlık sekmesinden çıkılınca
gereksiz SMART okuması durur. Disk değişince grafik temizlenip yeni
disk için yeniden başlıyor. Tek bir okuma hatası döngüyü öldürmüyor
(bir sonraki periyotta tekrar denenir); `ChartSyncObject` kilidiyle
arka plan iş parçacığından güvenli koleksiyon güncellemesi yapılıyor.

**Doğrulama:** `dotnet build`: 0 hata (yalnızca LiveCharts2'nin geçişli
bağımlılıklarından gelen 9 adet NU1701 "eski .NET Framework" uyumluluk
uyarısı — kendi kodumuzdan değil, bilinen/zararsız bir paket meta veri
uyarısı). `dotnet test`: 16/16 başarılı. **Görsel doğrulama kullanıcı
tarafından elle yapılmalı** — grafiğin gerçekten çizildiği, sekme
değişince döngünün durduğu ve SkiaSharp render'ının WPF içinde sorunsuz
çalıştığı (GPU/donanım bağımlı olabilir) gözle kontrol edilmeli.

## Devam eden iş

- (B) son kalan özellik: SMART trend loglama (JSON dosyasına yazma +
  açılışta geçmişi yükleme).

## Sıradaki işler (öncelik sırasına göre)

1. **Kullanıcı elle kontrol etmeli:** Yeni Theme.xaml stillerinin gerçek
   görünümü (DataGrid okunabilirliği, TabItem geçişleri, ScrollBar,
   TextBox odak rengi) — WPF render'ı bu ajan oturumunda görülemiyor.
2. **SMART verilerinin arayüzde doğru görünmesini test et** — uygulamayı
   yönetici olarak çalıştırıp `HealthViewModel`'in gerçek SMART
   verisiyle dolduğunu doğrula (UAC nedeniyle kullanıcı etkileşimi
   gerekebilir).
3. (B) görevleri: disk detay paneli, sağlık özet kartları, benchmark
   IOPS/blok boyutu, gerçek zamanlı sıcaklık grafiği, trend loglama.
4. Firmware güncelleme uyarısı, çoklu dil desteği (ileri aşama fikirleri).

## Bilinen buglar

- Yok (aktif olarak bilinen başka bug yok; SMART/Benchmark sekmeleri henüz
  gerçek donanımda uçtan uca test edilmedi; yeni arayüz stilleri henüz
  gerçek render ile gözle doğrulanmadı — bkz. yukarıdaki not).

## Notlar

- Proje bu oturumda git ile başlatıldı (`git init`) — daha önce `.git`
  yoktu. `tools/smartctl.exe` `.gitignore`'da, repoya girmiyor (GPL
  lisansı nedeniyle kullanıcı kendi indiriyor).
- WPF uygulaması `requireAdministrator` istediğinden, bu ajan oturumunda
  (yükseltilmemiş kabuk) doğrudan UI üzerinden UAC promptu geçilip
  görsel doğrulama yapılamadı; doğrulama `WmiDiskProvider` seviyesinde
  doğrudan API çağrısıyla yapıldı (yukarıya bakın).
- Elevated (yönetici) çalışan `SerkonDiskSuite.exe` süreci, bu ajan
  oturumunun yükseltilmemiş kabuğundan `taskkill` ile sonlandırılamıyor
  ("Erişim engellendi"); build sırasında exe kilitliyse kullanıcının
  pencereyi elle kapatması gerekiyor.
