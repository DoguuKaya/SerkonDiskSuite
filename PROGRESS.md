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

### 9. ÖZELLİK: SMART trend loglama (JSON dosyasına) (TAMAMLANDI — 2026-08-04)

`Core.Models.SmartTrendPoint` (Timestamp + TemperatureCelsius) ve
`Core.Interfaces.ISmartTrendStore` eklendi;
`Infrastructure/Trend/JsonSmartTrendStore` bunu disk başına bir JSON
dosyasında (`%LOCALAPPDATA%\SerkonDiskSuite\trend\<seri no>.json`,
seri no yoksa DevicePath) uyguluyor — dosya en fazla son 20.000 noktayı
tutacak şekilde budanıyor. `HealthViewModel`, izleme döngüsünün her
periyodunda okuduğu sıcaklığı bu depoya ekliyor (`AppendAsync`); disk
seçildiğinde ise depodan geçmişi yükleyip **canlı grafiğin aynı 15
dakikalık penceresine düşen** noktaları önceden dolduruyor
(`LoadHistoryThenStartMonitoringAsync`) — böylece uygulama yeniden
açıldığında grafik bomboş başlamıyor. Kalıcı dosyanın kendisi geçmişin
tamamını (budama sınırına kadar) tutmaya devam ediyor; yalnızca canlı
grafiğin gösterdiği pencere 15 dakikayla sınırlı (task 7'nin "son N
dakika" kapsamıyla tutarlı kalması için bilinçli bir tasarım tercihi —
PROGRESS.md'de not düşülüyor ki gelecekte "tam geçmiş" için ayrı bir
görünüm istenirse bu depo zaten hazır).

**Doğrulama:** `dotnet build`: 0 hata. `dotnet test`: 16/16 başarılı.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — özellikle
uygulamayı kapatıp birkaç dakika içinde yeniden açtığınızda grafiğin
kapanmadan önceki noktaları gösterdiğini doğrulayın; ayrıca
`%LOCALAPPDATA%\SerkonDiskSuite\trend\` altında dosyaların oluştuğunu
kontrol edin.

## (A) + (B) — Bu turun tüm işleri tamamlandı

Kullanıcının bu turda istediği hem arayüz yeniden tasarımı (A) hem de
eksik özellikler (B) tamamlandı. Kalan tek şey **kullanıcının elle
görsel doğrulama yapması** (bkz. aşağıdaki liste) — bu ajan oturumunda
WPF render'ı görülemiyor.

## Yeni tur: WPF-UI (lepoco/wpfui) migrasyonu

### 10. ADIM 1 — WPF-UI kurulumu (TAMAMLANDI — 2026-08-04)

`SerkonDiskSuite.App`'e iki NuGet paketi eklendi, sürüm tahmin edilmedi —
`dotnet add package` ile nuget.org'dan çözümlenen gerçek sürüm doğrulandı:
- `WPF-UI` **4.3.0** (lepoco/wpfui, MIT)
- `WPF-UI.DependencyInjection` **4.3.0** (resmi DI uzantısı — NavigationView
  sayfalarının `IServiceProvider` üzerinden çözümlenmesi için;
  `DependencyInjectionNavigationViewPageProvider` sınıfını sağlıyor)

`dotnet build`: 0 hata (yalnızca LiveCharts2'nin geçişli bağımlılıklarından
gelen bilinen NU1701 uyarıları, yeni değil). `dotnet test`: 16/16.

### 11. ADIM 2 — Pencere ve yerleşim: FluentWindow + TitleBar + NavigationView (TAMAMLANDI — 2026-08-04)

- `App.xaml`: `ui:ThemesDictionary Theme="Dark"` + `ui:ControlsDictionary`
  eklendi. `Resources/Theme.xaml` sadeleştirildi — Button/TextBox/ComboBox/
  ScrollBar/ListBox/ProgressBar/TabControl/"Card" stilleri tamamen
  kaldırıldı (artık WPF-UI'nin `ui:Button`/`ui:Card` kontrolleri ve
  implicit `ControlsDictionary` stilleri geçerli). Yalnızca WPF-UI'nin
  karşılığı OLMAYAN DataGrid stilleri + adlandırılmış `Heading`/`Muted`/
  `ErrorText` kısayolları kaldı; bunlar da artık WPF-UI'nin kendi
  `TextFillColorPrimaryBrush`/`TextFillColorSecondaryBrush`/
  `SystemFillColorCriticalBrush` fırçalarını kullanıyor.
- `Views/MainWindow.xaml`: `ui:FluentWindow` + `ui:TitleBar` (Mica arka
  plan, yuvarlak köşe) + `ui:NavigationView` (`PaneDisplayMode="Top"`,
  3 sayfa: Sağlık/Benchmark/Sistem). Disk listesi + disk detay şeridi
  `ui:Card` ile, rozetler `AccentFillColorDefaultBrush`/
  `ControlFillColorDefaultBrush` ile.
- Yeni `Views/Pages/HealthPage`, `BenchmarkPage`, `SystemPage` (+ yeni
  `SystemViewModel`, `MainViewModel`'den ayrıştırıldı) — her biri DI
  singleton, kendi ViewModel'i yapıcıdan enjekte ediliyor.
  `NavigationView.SetServiceProvider(IServiceProvider)` ile
  `NavigationViewItem.TargetPageType` DI konteynerinden çözülüyor
  (`MainWindow.xaml.cs`). **API doğrulama notu:** WPF-UI 4.3.0'ın
  `NavigationView`/`INavigationViewPageProvider` API yüzeyi resmi
  dokümantasyonda tutarsız görünüyordu (bazı örnekler eski
  `IPageService`/`SetPageService`, bazıları yeni
  `INavigationViewPageProvider` kullanıyordu); tahmin etmek yerine
  yüklü `Wpf.Ui.dll`/`Wpf.Ui.Abstractions.dll` derlemelerini geçici bir
  .NET 8 konsol uygulamasıyla reflection'la inceledim ve gerçek üye
  adlarını (`NavigationView.SetServiceProvider`, `Navigate(Type, object)`,
  `Wpf.Ui.Abstractions.Controls.INavigationAware`) doğrudan doğruladım.
- DataGrid `MaxHeight` zaten kaldırılmıştı (önceki oturum); Grid satır
  yapısı korunuyor (özet kartlar Auto, tablo `*`).
- **Kontrast doğrulaması** (hesaplanarak, WPF-UI'nin Dark tema metin
  fırçaları + olası Fluent koyu zemin aralığı #1F1F1F–#2C2C2C için):
  `TextFillColorPrimaryBrush` (opak beyaz) → 13.97–16.48:1;
  `TextFillColorSecondaryBrush` (%77 opak beyaz) → 8.96–10.26:1.
  Kalan özel DataGrid renkleri: beyaz metin/HeaderBg 18.91:1, /RowHover
  12.69:1, /RowSelected 10.77:1. Hepsi WCAG AA (4.5:1) eşiğinin çok
  üzerinde.

**Doğrulama:** `dotnet build` (tüm çözüm): 0 hata, yalnızca bilinen 6
NU1701 (LiveCharts2 geçişli bağımlılık) uyarısı. `dotnet test`: 16/16
başarılı. **Görsel doğrulama kullanıcı tarafından elle yapılmalı** —
Mica arka planın gerçekten uygulandığı, NavigationView'ın üstte sekme
gibi göründüğü, `ui:SymbolIcon` simgelerinin (Heart24/DataHistogram24/
Desktop24) doğru render edildiği gözle kontrol edilmeli.

### 12. ADIM 3 — SMART etiket/format mantığını Core'a taşıma + birim testleri (TAMAMLANDI — 2026-08-04)

`Formatting/DisplayFormatting.cs`, `SmartAttributeLabels.cs`,
`SmartAttributeValueFormatter.cs` `SerkonDiskSuite.App`'ten
`SerkonDiskSuite.Core` katmanına taşındı (Core zaten sıfır dış
bağımlılıklı; bu sınıflar da salt string/sayı mantığı, herhangi bir
WPF/UI bağımlılığı yok). `Converters.cs`/`SmartAttributeConverters.cs`
artık `SerkonDiskSuite.Core.Formatting` namespace'ini kullanıyor. Ham ad
hâlâ DataGrid hücresinin ToolTip'inde gösteriliyor (davranış değişmedi,
sadece mantığın konumu değişti).

22 yeni xUnit testi eklendi (`DisplayFormattingTests`,
`SmartAttributeLabelsTests`, `SmartAttributeValueFormatterTests`) —
bayt/saat/sayı tr-TR biçimlendirmesi, bilinen/bilinmeyen öznitelik adı
eşlemesi, `data_units_read` -> TB dönüşümü (17.226.562 birim ->
8.819.999.744.000 bayt -> "8,02 TB", elle hesaplanıp doğrulandı),
yüzde biçimlendirme, sayısal olmayan bileşik ham değerlerin değişmeden
bırakılması.

**Doğrulama:** `dotnet build`: 0 hata. `dotnet test`: **38/38 başarılı**
(16 eski + 22 yeni).

### 13. ADIM 4/5 kalanı — Benchmark gerçek ilerleme yüzdesi (TAMAMLANDI — 2026-08-04)

`DiskBenchmarkRunner`, her geçişin (pass) I/O döngüsü içinde periyodik
olarak (~50 bildirim/geçiş, `totalBlocks/50` blokta bir) ilerleme
raporluyor. Genel yüzde artık gerçek: `(testIndex*Passes + (pass-1) +
geçiş-içi-oran) / (toplamTestSayısı*Passes) * 100` — yani 4 test türü x
N geçişin tamamı üzerinden hesaplanan gerçek bir toplam, sabit "0" değil.
`BenchmarkViewModel.StartAsync`'teki `Progress<BenchmarkProgress>`
callback'i artık `ProgressPercent`'i de güncelliyor (önceden yalnızca
mesaj güncelleniyordu, yüzde hep 0'da kalıyordu).
`BenchmarkPage.xaml`'deki `ProgressBar` artık `IsIndeterminate` değil,
doğrudan `Value="{Binding ProgressPercent}"` ile gerçek yüzdeyi
gösteriyor.

**Doğrulama:** `dotnet build`: 0 hata. `dotnet test`: 38/38 başarılı.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — gerçek bir
benchmark çalıştırılıp ilerleme çubuğunun düzgün ilerlediği (donmadığı,
geriye gitmediği) gözle kontrol edilmeli.

## WPF-UI migrasyonu tamamlandı (ADIM 1-5)

Bu turda istenen 5 adımın tamamı bitti: WPF-UI kurulumu, FluentWindow/
TitleBar/NavigationView'a geçiş, SMART etiket/format mantığının Core'a
taşınması + testler, eksik özelliklerin tamamlanması (disk detay/sağlık
kartları/IOPS/blok boyutu zaten önceki oturumdan vardı, bu turda gerçek
ilerleme yüzdesi eklendi), LiveCharts2 sıcaklık grafiği + trend loglama
(önceki oturumdan vardı, bu turda NavigationView'ın sayfa yaşam
döngüsüne — `INavigationAware` — bağlandı, bellek sızıntısı riski
azaltıldı).

### 14. BUG: WPF-UI migrasyonu sonrası açılışta sessiz çöküş (ÇÖZÜLDÜ — 2026-08-04)

**Kök neden:** `MainWindow` yapıcısında `InitializeComponent()`'ten hemen
sonra `RootNavigation.Navigate(typeof(HealthPage), null)` çağrılıyordu.
Bu noktada `ui:NavigationView` henüz kendi `ControlTemplate`'ini
uygulamamış olduğundan (WPF şablon uygulaması yalnızca pencere `Loaded`
olduğunda/layout geçişi sırasında gerçekleşir), `NavigationView`'ın iç
`ContentPresenter`'ı hâlâ `null`'dı. `Navigate` → `NavigateInternal` →
`UpdateContent` bu null referansa `NullReferenceException` fırlatıyor,
istisna hiçbir yerde yakalanmadığından uygulama pencere hiç görünmeden
sessizce kapanıyordu.

Aynı istisna türü (`NullReferenceException`
`Wpf.Ui.Controls.NavigationView.UpdateContent`) WPF-UI 4.3.0'da bilinen
bir "Navigate çağrısı çok erken" hatası; şablon henüz yokken içerik
sunan kontrolde çağrılan `Navigate`, `Loaded` sonrasına ertelenmelidir.

**Düzeltme:** `Views/MainWindow.xaml.cs` — `SetServiceProvider` +
`Navigate` çağrıları yapıcıdan çıkarılıp pencerenin `Loaded` olayına
taşındı (`OnLoaded` metodu). `Loaded` birden fazla kez tetiklenebileceği
için `_isInitialized` bayrağıyla navigasyon kurulumu tek seferlik hale
getirildi; `SetServiceProvider` her durumda `Navigate`'ten önce
çağrılıyor. Diskleri tarayan `LoadCommand` çağrısı da aynı tek seferlik
`OnLoaded` içine taşındı (önceki ayrı `Loaded` lambda'sıyla aynı
davranış, artık tek bir yerde).

**Ayrıca bu turda:**
- `App.xaml.cs`'e global istisna yakalayıcılar eklendi:
  `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`. Her biri istisnayı
  `%LOCALAPPDATA%\SerkonDiskSuite\logs\crash-{yyyyMMdd-HHmmss-fff}.log`
  dosyasına (zaman damgası + kaynak + tam `Exception.ToString()`) yazıp
  kullanıcıya `MessageBox` ile anlaşılır bir hata penceresi gösteriyor;
  loglama başarısız olsa bile pencere yine de gösteriliyor. Böylece bu
  sınıf hatalar bir daha sessizce çökmeyecek — en azından kullanıcıya
  görünür olacak ve log dosyasına kaydedilecek.
- `SerkonDiskSuite.App.csproj`'a `<NoWarn>$(NoWarn);NU1701</NoWarn>`
  eklendi — LiveCharts2'nin geçişli bağımlılıklarından (SkiaSharp/OpenTK)
  gelen bilinen/zararsız uyarı yığını artık derleme çıktısını kirletmiyor,
  gerçek uyarılar/hatalar daha görünür.

**Doğrulama:**
- `dotnet build` (tüm çözüm): 0 hata, **0 uyarı** (NU1701'ler dahil hiç
  uyarı yok — önceki turlarda 6-9 adet NU1701 vardı).
- `dotnet test`: 38/38 başarılı.
- Uygulama gerçekten başlatıldı (`Start-Process` ile) ve 8 saniye
  ayakta kaldığı doğrulandı: `Get-Process SerkonDiskSuite` süreci canlı
  buldu, `MainWindowTitle="Serkon Disk Suite"`, `Responding=True`.
  Önceki (düzeltme öncesi) davranışta süreç pencere hiç görünmeden
  hemen kapanıyordu. `%LOCALAPPDATA%\SerkonDiskSuite\logs\` altında
  crash log dosyası oluşmadı — yani hiçbir yakalanmamış istisna
  tetiklenmedi. Süreç yönetici hakkıyla çalıştığından bu ajan oturumunun
  yükseltilmemiş kabuğundan kapatılamadı (bilinen kısıt, bkz. Notlar);
  kullanıcının pencereyi elle kapatması gerekiyor.

**Değişiklik:** `src/SerkonDiskSuite.App/Views/MainWindow.xaml.cs`,
`src/SerkonDiskSuite.App/App.xaml.cs`,
`src/SerkonDiskSuite.App/SerkonDiskSuite.App.csproj`

### 15. BUG: Sıcaklık grafiği görünmüyordu (ÇÖZÜLDÜ — 2026-08-04)

**Kök neden:** `HealthViewModel`'deki `Axis`/`LineSeries` tanımlarında hiçbir
`Paint` (renk) ayarlanmamıştı. LiveChartsCore'un varsayılan eksen etiketi/
ayraç (separator) rengi koyu/soluk bir ton; WPF-UI'nin koyu tema zemininde
(~#202020) bu neredeyse tamamen görünmez oluyor, bu yüzden grafik "ince boş
bir şerit" gibi görünüyordu (aslında çiziliyordu, sadece görünmez renkte).

**Düzeltme:** `HealthViewModel.cs` — reflection ile doğrulanmış
(`LiveChartsCore.SkiaSharpView.Axis.LabelsPaint/SeparatorsPaint/NamePaint`,
`LineSeries<T>.Stroke`) API'ler kullanılarak X/Y eksenlerine açık, okunabilir
`SolidColorPaint` renkleri (etiket ~#C8C8C8, ayraç ~#55585E) ve seriye mavi
vurgu rengi (~#60A5FA, kalınlık 2) verildi. `Series`/`Axes` yapıcıda daima
dolu olduğundan (veri olmasa bile) veri gelmeden de eksenli boş bir çerçeve
zaten görünüyor olacak; asıl eksik olan renk görünürlüğüydü.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 38/38 başarılı.
Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — grafiğin artık
gerçekten görünür eksenli bir çerçeve + okunabilir etiketlerle çizildiği,
veri geldikçe mavi çizginin dolduğu gözle kontrol edilmeli.

**Ortam notu (bu turda keşfedildi):** Bu ajan oturumunun kabuğu (Medium
Integrity) uygulamanın (`app.manifest` `requireAdministrator`) her
başlattığında sessizce (UAC promptsuz) yönetici hakkına yükseldiğini,
bu yüzden başlatılan test süreçlerinin bu kabuktan `Stop-Process`/
`taskkill` ile kapatılamadığını (Erişim engellendi, Windows Mandatory
Integrity Control) gösterdi. Kullanıcıyla konuşulup her maddeden sonra
test sürecini kullanıcının elle kapatmasına karar verildi.

**Değişiklik:** `src/SerkonDiskSuite.App/ViewModels/HealthViewModel.cs`

### 16. BUG: Sayı biçimlendirmesi en-US kültürüne düşüyordu (ÇÖZÜLDÜ — 2026-08-04)

**Kök neden:** XAML `StringFormat=N0` (WPF'in varsayılan `Binding.ConverterCulture`
davranışı) `FrameworkElement.Language`'a göre biçimlendirir; bu özellik
uygulamada hiç ayarlanmadığından WPF'in kendi varsayılanı olan `en-US`'a
düşüyordu ("1.771 MB/s" yerine "1,771 MB/s", "25.806 IOPS" yerine
"25,806 IOPS"). Ayrıca `DiskInfo.CapacityDisplay` kendi özel `FormatBytes`
metodunu (`$"{size:0.##}"`, kültüre bağlı ama açıkça tr-TR zorlamıyordu)
kullanıyordu — `Core/Formatting/DisplayFormatting`'in tr-TR biçimlendiricisiyle
tutarsız, ayrıca kod tekrarıydı.

**Düzeltme:**
- `Core/Formatting/DisplayFormatting.cs` — genel amaçlı `FormatNumber(double,
  int decimals = 0)` eklendi (tr-TR binlik/ondalık ayraç).
- `App/Converters/Converters.cs` — yeni `NumberToStringConverter`
  (`{StaticResource NumberToString}`, App.xaml'e kaydedildi).
- `BenchmarkPage.xaml` — `Iops`/`ThroughputMBps` için `StringFormat=N0`
  yerine `Converter={StaticResource NumberToString}`.
- `Core/Models/DiskInfo.cs` — `CapacityDisplay` artık kendi özel
  `FormatBytes`'ı yerine `DisplayFormatting.FormatBytes` kullanıyor (kod
  tekrarı kaldırıldı, tr-TR garanti).
- Uygulamadaki diğer tüm sayısal gösterimler tarandı: SMART kartları/tablo
  zaten `DisplayFormatting`/`SmartAttributeValueFormatter` üzerinden geçiyor
  (önceki oturumda taşınmıştı); düz `{Binding}` ile gösterilen küçük tam
  sayılar (sıcaklık, yüzde, açılma sayısı vb.) format dizgesi kullanmadığı
  için binlik ayraç sorunu yaşamıyor — başka StringFormat kullanan yer yok.

**Doğrulama:** `DisplayFormattingTests`'e `FormatNumber` için 6 yeni test
eklendi (binlik ayraç + ondalık virgül). `dotnet build`: 0 hata/0 uyarı.
`dotnet test`: **44/44 başarılı** (38 eski + 6 yeni). Uygulama başlatılıp
8 saniye ayakta kaldığı doğrulandı (`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — gerçek bir
benchmark çalıştırıp IOPS/throughput değerlerinin "1.771 MB/s" gibi
tr-TR biçiminde göründüğünü gözle kontrol edin.

**Değişiklik:** `src/SerkonDiskSuite.Core/Formatting/DisplayFormatting.cs`,
`src/SerkonDiskSuite.Core/Models/DiskInfo.cs`,
`src/SerkonDiskSuite.App/Converters/Converters.cs`, `App.xaml`,
`Views/Pages/BenchmarkPage.xaml`,
`tests/SerkonDiskSuite.Tests/DisplayFormattingTests.cs`

### 17. BUG: Sayfa yerleşimi — içerik dikeyde aşağıya itiliyordu (ÇÖZÜLDÜ — 2026-08-04)

**Kök neden (iki katmanlı):**
1. `ui:FluentWindow`, `Control`'den (dolayısıyla `HorizontalContentAlignment`/
   `VerticalContentAlignment`'tan) miras alır ve bunlar açıkça ayarlanmamıştı;
   pencerenin kendi içeriği (disk listesi + NavigationView'ı içeren dış Grid)
   doğal boyutuna küçülüp pencere içinde dikeyde ortalanıyordu — bu, "Diskler"
   panelinin sol altta yüzmesinin nedeniydi (panel NavigationView'ın *dışında*,
   MainWindow'un kendi içeriğinde).
2. `ui:NavigationView`, sayfaları GitHub kaynağından (lepoco/wpfui) doğrulanan
   `NavigationViewContentPresenter : Frame` adlı bir template parçası
   (`PART_NavigationViewContentPresenter`) üzerinden gösteriyor.
   `IsDynamicScrollViewerEnabled` (varsayılan `true`) navigasyonla gelen
   sayfayı sonsuz yükseklik veren bir `ScrollViewer`'a sarıyor; bu yüzden
   sayfa Grid'lerindeki `"*"` satırları doğal (Auto-eşdeğeri) boyuta küçülüp
   NavigationView içinde dikeyde ortalanıyordu (üç sayfada da "üstte büyük
   boş alan").

**Düzeltme:**
- `MainWindow.xaml` — kök `ui:FluentWindow` öğesine
  `HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch"`.
- `Resources/Theme.xaml` — `ui:NavigationViewContentPresenter` için
  `TargetType` stili: `HorizontalContentAlignment`/`VerticalContentAlignment`
  Stretch (bu iki özelliğin public setter'ı var).
- `MainWindow.xaml.cs` — `IsDynamicScrollViewerEnabled`'ın CLR set erişeni
  `protected` olduğundan (XAML derleyicisi `MC3080` ile Style Setter'ı
  reddetti), `OnLoaded` içinde `VisualTreeHelper` ile
  `NavigationViewContentPresenter` bulunup `SetValue(...Property, false)`
  ile DependencyProperty seviyesinde doğrudan kapatılıyor (CLR erişilebilirlik
  denetimi yalnızca derleme zamanı XAML derleyicisini etkiler, `SetValue`'yu
  etkilemez).
- `BenchmarkPage.xaml`, `SystemPage.xaml` — kök `StackPanel` (her zaman
  içeriğe göre boyutlanır, asla "dolmaz") `Grid`'e çevrildi: üstte Auto
  satırlar (başlık/kontroller), sonda `"*"` satır (Benchmark'ta sonuç
  listesi kendi `ScrollViewer`'ıyla kalan alanı doldurur; System'da boş bir
  `"*"` satır artan boşluğu alta taşır).

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 44/44 başarılı.
Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`,
`MainWindowTitle` görünür — çöküş yok).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — bu, bu ajan
oturumunda test edilemeyen bir alan: `NavigationViewContentPresenter`'ın
`IsDynamicScrollViewerEnabled`'ı kapatmanın gerçekten sayfa içeriğini
NavigationView'ın tüm yüksekliğine yaydığı, disk listesinin artık sol
sütunu baştan sona doldurduğu ve Benchmark/Sistem sayfalarında içeriğin
üstten başlayıp boşluğun altta kaldığı gözle kontrol edilmeli. Eğer hâlâ
ortalanma görülüyorsa, `NavigationViewContentPresenter` içindeki gizli
`ScrollViewer`'ın farklı bir yoldan (ör. adı değişmiş bir template parçası)
sarmalandığı ihtimali araştırılmalı.

**Değişiklik:** `src/SerkonDiskSuite.App/Views/MainWindow.xaml`,
`Views/MainWindow.xaml.cs`, `Resources/Theme.xaml`,
`Views/Pages/BenchmarkPage.xaml`, `Views/Pages/SystemPage.xaml`

### 18. ÖZELLİK: Benchmark test adları Türkçeleştirildi (TAMAMLANDI — 2026-08-04)

`BenchmarkTestKind` enum'ı değiştirilmedi; `Core/Formatting/
BenchmarkTestKindLabels.ToTurkish` eşlemesi eklendi (SequentialRead ->
"Sıralı Okuma", SequentialWrite -> "Sıralı Yazma", RandomRead -> "Rastgele
Okuma", RandomWrite -> "Rastgele Yazma"). `App/Converters` içinde
`BenchmarkTestKindToStringConverter` eklendi, `BenchmarkPage.xaml`'deki
sonuç kartı başlığı bunu kullanıyor. Ayrıca `DiskBenchmarkRunner`'daki
ilerleme mesajı (`"{kind} çalışıyor..."`) da aynı eşlemeyi kullanacak
şekilde güncellendi — bu mesaj `BenchmarkViewModel.ProgressMessage` ve
alt durum çubuğu üzerinden kullanıcıya görünüyordu, ham İngilizce enum
adını (ör. "SequentialWrite çalışıyor...") sızdırıyordu.

**Doğrulama:** 4 yeni birim testi (`BenchmarkTestKindLabelsTests`, her enum
değeri için). `dotnet build`: 0 hata/0 uyarı. `dotnet test`: **48/48
başarılı** (44 eski + 4 yeni). Canlı çalıştırma testi bu madde için
gerekli görülmedi (kullanıcıyla konuşulan tempo kararı — mantık/metin
ağırlıklı, çöküş riski düşük).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — sonuç
kartlarında ve ilerleme mesajında Türkçe adların gerçekten göründüğü
gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Core/Formatting/BenchmarkTestKindLabels.cs`
(yeni), `src/SerkonDiskSuite.App/Converters/Converters.cs`, `App.xaml`,
`Views/Pages/BenchmarkPage.xaml`,
`src/SerkonDiskSuite.Infrastructure/Benchmark/DiskBenchmarkRunner.cs`,
`tests/SerkonDiskSuite.Tests/BenchmarkTestKindLabelsTests.cs` (yeni)

### 19. BUG: SMART tablosu temizliği (ÇÖZÜLDÜ — 2026-08-04)

Üç ayrı küçük düzeltme:
- **ID kolonu:** NVMe disklerde öznitelik ID'si yok, `SmartctlSmartProvider`
  hep `Id: "-"` üretiyordu. `HealthViewModel.IsIdColumnVisible` eklendi
  (`SetDisk` içinde `disk.BusType != DiskBusType.Nvme` ile hesaplanır),
  `HealthPage.xaml`'de ID kolonunun `Visibility`'si buna bağlandı
  (`DataGridColumn` görsel ağaçta olmadığından `ElementName=Root` ile
  Page'in `DataContext`'ine ulaşıldı).
- **nsid=-1:** `SmartctlSmartProvider.ExtractAttributes`, NVMe log'unu
  düzleştirirken `nsid` alanı `-1` ise artık o satırı hiç eklemiyor
  (kaynağında filtrelendi, UI'da özel durum kodu gerekmedi).
- **Seri no kesilmesi:** `MainWindow.xaml`'deki disk detay şeridi
  `StackPanel` (taşan içeriği kırpar) yerine `WrapPanel` kullanıyor;
  dar pencerede rozetler ikinci satıra kayar ama seri numarası hiçbir
  zaman kırpılmaz.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 48/48
başarılı (bu maddede yeni test istenmedi; `SmartctlSmartProvider`
Infrastructure katmanında ve gerçek smartctl JSON'ına bağlı, mevcut test
altyapısı yalnızca Core'u kapsıyor). Canlı çalıştırma testi bu madde için
gerekli görülmedi (tempo kararı).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — gerçek bir
NVMe ve bir SATA diskte ID kolonunun doğru gizlenip/görünüp göründüğü,
nsid=-1 satırının artık listede olmadığı, seri numarasının artık tam
göründüğü (gerekirse ikinci satıra kayarak) gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.App/ViewModels/HealthViewModel.cs`,
`Views/Pages/HealthPage.xaml`, `Views/MainWindow.xaml`,
`src/SerkonDiskSuite.Infrastructure/Smart/SmartctlSmartProvider.cs`

### 20. ÖZELLİK: Benchmark motoruna queue depth/thread desteği (TAMAMLANDI — 2026-08-04)

**Değişiklik özeti:** `DiskBenchmarkRunner` artık her test Q1T1 (tek istek,
tek iş parçacığı) değil, `BenchmarkOptions.QueueDepth` x
`BenchmarkOptions.ThreadCount` kadar eşzamanlı I/O isteğini havada
tutabiliyor — CrystalDiskMark'ın gerçekçi sonuçlar üretmesinin asıl
nedeni de bu.

**Uygulama:**
- Dosya handle'ı artık `FileOptions.Asynchronous` ile de açılıyor (gerçek
  overlapped I/O için; `NoBuffering`/`WriteThrough` korunuyor).
- `RunSinglePass` (senkron, sıralı for-loop) yerine `RunSinglePassAsync`:
  `Parallel.ForEachAsync` ile `MaxDegreeOfParallelism = QueueDepth x
  ThreadCount`, gövdede `RandomAccess.ReadAsync`/`WriteAsync` kullanılıyor.
- **Eşzamanlılık güvenliği:** Yazma testlerinde tüm eşzamanlı istekler
  paylaşılan, hiç değişmeyen (salt okunur) bir kaynak arabelleği
  kullanıyor (thread-safe, çünkü asla mutasyona uğramıyor); okuma
  testlerinde her istek `ArrayPool<byte>.Shared`'dan kendi arabelleğini
  kiralayıp işi bitince geri veriyor (önceki tek-arabellek + sıralı
  erişim modeli artık güvenli değildi, çünkü aynı arabelleğe eşzamanlı
  birden fazla I/O yapılması veri yarışına yol açardı).
- **Determinizm:** Rastgele testlerde önceki `Random(12345).Next(...)`
  (sıralı, tek iş parçacıklı erişimde deterministikti) yerine, blok
  indeksinden paylaşılan durumsuz bir SplitMix64-türevi karma fonksiyonu
  (`DeterministicRandomBlockIndex`) kullanılıyor — eşzamanlı çalışmada
  isteklerin tamamlanma SIRASI değişebildiği için stateful bir RNG'nin
  sıralı `.Next()` çağrılarına güvenmek artık doğru sonuç vermezdi; saf
  fonksiyon her indeks için her zaman aynı "rastgele" ofseti üretir
  (thread-safe, hâlâ deterministik/tekrarlanabilir).
- `BenchmarkOptions`'a `QueueDepth`/`ThreadCount` (varsayılan 1/1, eski
  davranışla tam geriye uyumlu), `BenchmarkResult`'a da aynı iki alan
  eklendi (sonuçta hangi Q/T ile üretildiği görünür — madde 7'nin profil
  gösterimi için de kullanılacak).
- İlerleme raporlama artık `Interlocked` sayaçlarla eşzamanlı güvenli.

**Test altyapısı değişikliği:** `SerkonDiskSuite.Tests` artık
`net8.0-windows`'a hedefleniyor ve `SerkonDiskSuite.Infrastructure`'a
proje referansı var (önceden yalnızca Core'a bakıyordu) — bu, gerçek
dosya I/O'suyla çalışan `DiskBenchmarkRunner`'ı doğrudan test edebilmek
için gerekliydi.

**Doğrulama:** Yeni `DiskBenchmarkRunnerTests` (temp dizinde küçük — 256
KiB — gerçek dosyalarla): Q1T1 varsayılanının 4 test türü için de sonuç
ürettiğini, Q4T1/Q1T4/Q4T2 gibi yüksek eşzamanlılık kombinasyonlarının
hatasız tamamlandığını ve sonuçlara doğru Q/T etiketlerinin yazıldığını,
iptalin (`CancellationToken`) çalıştığını doğruluyor — **gerçek disk I/O
üzerinden**, mock değil. `dotnet build`: 0 hata/0 uyarı (Infrastructure'da
`TreatWarningsAsErrors=true` dahil). `dotnet test`: **53/53 başarılı**
(48 eski + 5 yeni). Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı
(`Responding=True`) — motor değişikliği açılışı bozmadı (zaten
BenchmarkViewModel henüz yeni Q/T alanlarını UI'dan ayarlamıyor, madde 7
bunu ekleyecek; şimdilik hep varsayılan Q1T1 ile çalışıyor).

**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — bu turda henüz
UI'dan Q/T değiştirilemiyor (madde 7 bunu ekleyecek), bu yüzden gerçek
donanımda YÜKSEK queue depth'in gerçekten daha yüksek throughput/IOPS
ürettiği (asıl motivasyon: "rastgele okuma 38 MB/s gibi gerçek
potansiyelin çok altında") ancak madde 7 tamamlanınca elle test
edilebilir. Şimdiden doğrulanabilecek olan (bu ajan oturumunda test
edildi): motor gerçek dosyalarda hatasız çalışıyor, veri yarışı/çökme yok.

**Değişiklik:** `src/SerkonDiskSuite.Core/Models/BenchmarkModels.cs`,
`src/SerkonDiskSuite.Infrastructure/Benchmark/DiskBenchmarkRunner.cs`,
`tests/SerkonDiskSuite.Tests/SerkonDiskSuite.Tests.csproj`,
`tests/SerkonDiskSuite.Tests/DiskBenchmarkRunnerTests.cs` (yeni)

### 21. ÖZELLİK: Hazır CrystalDiskMark profilleri (TAMAMLANDI — 2026-08-04)

`Core/Models/BenchmarkProfiles.cs` (yeni): `BenchmarkProfile` record'ı +
CrystalDiskMark'ın NVMe varsayılan 4 profili (SEQ1M Q8T1, SEQ1M Q1T1,
RND4K Q32T16, RND4K Q1T1) + `BenchmarkProfiles.Apply(options, profile)`
(sıralı profil yalnızca `SequentialBlockSize`'ı, rastgele profil yalnızca
`RandomBlockSize`'ı değiştirir; `QueueDepth`/`ThreadCount`/`ProfileName`
her ikisinde de güncellenir — `BenchmarkOptions` artık `record` olduğundan
`with` ile değişmez şekilde uygulanıyor, orijinal nesne mutasyona
uğramıyor).

`BenchmarkViewModel`'e `SelectedProfile`/`Profiles` eklendi;
`BenchmarkPage.xaml`'e profil seçici `ComboBox` (profil seçilmezse
mevcut manuel "Özel" ayarlar — boyut/geçiş/rastgele blok boyutu —
kullanılır). Sonuç kartlarında artık test adının yanında profil adı
parantez içinde gösteriliyor (ör. "Rastgele Okuma (RND4K Q32T16)").

**Bilinen basitleştirme:** `QueueDepth`/`ThreadCount` hâlâ (madde 6'dan)
TÜM dört test türüne birden uygulanan tek bir global ayar; gerçek
CrystalDiskMark'ta sıralı ve rastgele testler ayrı Q/T taşıyabilir. Bir
profil seçildiğinde o profilin Q/T'si dört testin HEPSİNE uygulanıyor
(yalnızca blok boyutu kategoriye özel kalıyor). Bu, madde 6'nın motor
mimarisiyle tutarlı kalmak için bilinçli bir kapsam kararı; gerekirse
ileride `SequentialQueueDepth`/`RandomQueueDepth` ayrımına genişletilebilir.

**Doğrulama:** Yeni `BenchmarkProfilesTests` (4 testin hepsi doğru
sırada/isimde, `Apply`'ın sadece ilgili kategoriyi değiştirdiği,
orijinal `options`'ın mutasyona uğramadığı). `dotnet build`: 0 hata/0
uyarı. `dotnet test`: **57/57 başarılı** (53 eski + 4 yeni). Uygulama
başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — profil
ComboBox'ının doğru göründüğü, bir profil seçilip test çalıştırıldığında
sonuç kartlarında doğru profil adının ve gerçekten değişen throughput/IOPS
değerlerinin (özellikle RND4K Q32T16 ile Q1T1 arasındaki farkın) göründüğü
gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Core/Models/BenchmarkProfiles.cs`
(yeni), `BenchmarkModels.cs` (ProfileName alanı, `class` -> `record`),
`src/SerkonDiskSuite.Infrastructure/Benchmark/DiskBenchmarkRunner.cs`,
`src/SerkonDiskSuite.App/ViewModels/BenchmarkViewModel.cs`,
`Views/Pages/BenchmarkPage.xaml`,
`tests/SerkonDiskSuite.Tests/BenchmarkProfilesTests.cs` (yeni)

### 22. ÖZELLİK: Benchmark hedef sürücü seçici + sistem diski uyarısı (TAMAMLANDI — 2026-08-04)

Önceden `BenchmarkViewModel.SetDisk`, seçili diskin İLK sürücü harfini
otomatik hedef olarak alıyordu (kullanıcı değiştiremiyordu) — sabit
`C:\` test edilmesi riski buradan geliyordu (sistem diski seçiliyse
kullanıcının haberi olmadan test edilebiliyordu).

**Düzeltme:** `AvailableDriveLetters`/`SelectedDriveLetter` eklendi;
`SetDisk` artık seçili diskin TÜM sürücü harflerini listeler (ilk harf
varsayılan olarak seçilir, kullanıcı ComboBox'tan değiştirebilir).
`OnSelectedDriveLetterChanged` her seçimde `TargetDrive`'ı günceller ve
`IsSystemDriveSelected`'ı (`Path.GetPathRoot(Environment.SystemDirectory)`
ile karşılaştırarak) hesaplar. `BenchmarkPage.xaml`'e sürücü seçici
`ComboBox` + sistem diski seçiliyken görünen kırmızı uyarı metni eklendi
("⚠ Sistem diski seçili — bu diski test etmek riskli olabilir!").

Not: Uyarı sadece görsel/bilgilendirici; kullanıcı isterse sistem diskini
yine de test edebilir (diski gerçekten kilitleme/format gibi yıkıcı bir
engelleme eklenmedi — kullanıcı CLAUDE.md'de disk format/partition
özelliğine bu turda kasıtlı girilmemesini istemişti, benzer şekilde
"engelleme" değil "uyarı" istenmiş).

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 57/57
başarılı (bu madde mantığı basit UI state yönetimi; ayrı birim testi
istenmedi). Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı
(`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — birden fazla
bölümü olan bir diskte ComboBox'ın tüm harfleri listelediği, sistem
diski (genelde C:) seçildiğinde uyarının göründüğü, başka bir harf
seçilince uyarının kaybolduğu gözle kontrol edilmeli.

**GRUP 2 (madde 6-8, benchmark motorunu CrystalDiskMark seviyesine
çıkarma) bu maddeyle tamamlandı.**

**Değişiklik:** `src/SerkonDiskSuite.App/ViewModels/BenchmarkViewModel.cs`,
`Views/Pages/BenchmarkPage.xaml`

### 23. ÖZELLİK: Sistem sayfasına RAM bilgisi eklendi (TAMAMLANDI — 2026-08-04)

`SystemSummary.TotalMemoryBytes` zaten `WmiSystemInfoProvider` tarafından
dolduruluyordu ama `SystemPage.xaml`'de hiç gösterilmiyordu.
İşlemci ile Anakart satırları arasına, mevcut `BytesToString`
converter'ıyla (tr-TR) biçimlendirilen bir "RAM: " satırı eklendi.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 57/57
başarılı (yeni test istenmedi — tek satır XAML bağlaması, mevcut
converter zaten test edilmiş). Canlı çalıştırma testi bu madde için
gerekli görülmedi (tempo kararı).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — Sistem
sayfasında RAM miktarının doğru (tr-TR, ör. "15,92 GB") göründüğü gözle
kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.App/Views/Pages/SystemPage.xaml`

### 24. ÖZELLİK: SMART self-test desteği (backend) (TAMAMLANDI — 2026-08-04)

`ISmartProvider`'a iki yeni metot: `StartSelfTestAsync(disk, SelfTestType,
ct)` (`smartctl -t short|long <device>`) ve `GetSelfTestStatusAsync(disk,
ct)` (mevcut `-a --json=c` çıktısındaki `ata_smart_data.self_test.status`
alanını ayrıştırır — ayrı bir `-l selftest` çağrısına gerek yok, zaten
`-a` çıktısında bulunuyor). `SelfTestType` (Short/Long) ve `SelfTestStatus`
(IsRunning, PercentRemaining, StatusDescription, Passed) modelleri Core'a
eklendi.

**Bu madde yalnızca backend (Core+Infrastructure).** Bu yeteneği
kullanacak arayüz madde 11'de ("Teşhis" sayfası) eklenecek — bilinçli
sıralama, iki madde birbirini tamamlıyor.

**NVMe notu:** smartctl'in NVMe self-test JSON alan adları güvenilir bir
şekilde doğrulanamadı (dokümantasyon taraması net sonuç vermedi, gerçek
donanımda test edilemedi). Tahmini bir alan adı kullanmak yerine,
`ata_smart_data.self_test.status` bulunamazsa (NVMe'de bu alan yok)
fonksiyon güvenle "bilgi yok" (`IsRunning=false`, diğerleri `null`) döner
— yanlış bilgi üretmek yerine dürüstçe boş döner. NVMe self-test'i
gerçek donanımda doğrulanıp ileride eklenebilir.

**Doğrulama:** `SmartctlSelfTestParsingTests` — smartctl'in belgelenen
JSON şemasına (`value`/`string`/`remaining_percent`) uygun 4 sabit JSON
örneğiyle (devam ediyor/tamamlandı-hatasız/kesintiye uğradı/alan yok)
`ParseSelfTestStatus`'u gerçek donanım veya smartctl çalıştırmadan
doğruluyor — **gerçek bir self-test bu ajan oturumunda KASITLI OLARAK
tetiklenmedi** (uzun test gerçek donanımda saatler sürebilir, kullanıcının
diskini test etmeye zorlamak riskli olur; bu yalnızca kullanıcının UI'dan
(madde 11) kendi isteğiyle tetikleyeceği bir işlem). `dotnet build`: 0
hata/0 uyarı. `dotnet test`: **61/61 başarılı** (57 eski + 4 yeni).
Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`).

**Değişiklik:** `src/SerkonDiskSuite.Core/Models/SelfTestModels.cs` (yeni),
`src/SerkonDiskSuite.Core/Interfaces/Providers.cs`,
`src/SerkonDiskSuite.Infrastructure/Smart/SmartctlSmartProvider.cs`,
`tests/SerkonDiskSuite.Tests/SmartctlSelfTestParsingTests.cs` (yeni)

### 25. ÖZELLİK: Yeni "Teşhis" sayfası (TAMAMLANDI — 2026-08-05)

Madde 10'da eklenen backend'i (self-test start/status) kullanan yeni bir
NavigationView sekmesi: `DiagnosticsPage`/`DiagnosticsViewModel`. Toplanan
bilgiler: firmware sürümü (`DiskInfo.FirmwareVersion`), NVMe kritik uyarı
bayrakları (`SmartHealth.CriticalWarningFlags`, kırmızı liste), self-test
türü seçimi (Kısa/Uzun) + "Başlat" butonu, çalışan/son biten testin
durumu (açıklama + kalan yüzde). Self-test çalışırken durum 15 saniyede
bir otomatik yoklanır (`PollLoopAsync`, `HealthViewModel`'in sıcaklık
izleme döngüsüyle aynı desen); sayfaya her girildiğinde de yenilenir
(`INavigationAware.OnNavigatedToAsync`).

DI: `DiagnosticsViewModel`/`DiagnosticsPage` singleton olarak eklendi,
`MainViewModel.OnSelectedDiskChanged` artık `Diagnostics.SetDisk(value)`
de çağırıyor. `MainWindow.xaml`'e "Teşhis" (`Wrench24` simgesi)
`NavigationViewItem`'ı eklendi.

**Ortam notu (bu turda karşılaşıldı):** Uygulamayı canlı test ederken bir
kez `Start-Process : İşlem kullanıcı tarafından iptal edildi` hatası
alındı — önceki tüm başlatmaların aksine (sessiz otomatik yükselme),
bu kez gerçek bir UAC izin penceresi görünmüş ve iptal olmuştu.
Kullanıcı UAC'yi onaylayınca ikinci deneme başarılı oldu. Bu, ortamın
yükseltme davranışının tutarsız/değişken olabileceğini gösteriyor;
gelecekte tekrar gerçek bir UAC promptu çıkarsa kullanıcının onaylaması
gerekebilir.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı (`Wrench24` simgesi
gerçekten var, derleme zamanında doğrulandı). `dotnet test`: 61/61
başarılı (bu madde saf UI/orkestrasyon; ayrı birim testi istenmedi,
alttaki `ISmartProvider` metotları madde 10'da zaten test edildi).
Uygulama başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — Teşhis
sekmesinin göründüğü, firmware/kritik uyarıların doğru geldiği,
**gerçek bir self-test'in** (öncelikle "Kısa" ile, "Uzun" saatler
sürebilir) başlatılıp ilerlemenin doğru yoklandığı gözle kontrol
edilmeli — bu, gerçek donanımda saatler sürebileceğinden bu ajan
oturumunda KASITLI OLARAK tetiklenmedi.

**Değişiklik:** `src/SerkonDiskSuite.App/ViewModels/DiagnosticsViewModel.cs`
(yeni), `Views/Pages/DiagnosticsPage.xaml(.cs)` (yeni),
`Converters/Converters.cs`, `App.xaml(.cs)`,
`ViewModels/MainViewModel.cs`, `Views/MainWindow.xaml`,
`Core/Models/SmartHealth.cs`,
`Infrastructure/Smart/SmartctlSmartProvider.cs`

### 26. ÖZELLİK: Trend geçmişi ekranı (TAMAMLANDI — 2026-08-05)

`%LOCALAPPDATA%\SerkonDiskSuite\trend\<seri no>.json` altında loglanan
SMART geçmişi artık Sağlık sayfasında görünüyor — canlı grafiğin 15
dakikalık penceresinden bağımsız, dosyadaki TÜM kayıt (budama sınırına
kadar, 20.000 nokta).

**Model değişikliği:** `SmartTrendPoint`'e `RemainingLifePercent` (int?,
varsayılan null) eklendi — önceden yalnızca sıcaklık loglanıyordu.
Eski JSON dosyaları bu alan olmadan da sorunsuz okunuyor (eksik alan
null'a düşer). `HealthViewModel.MonitorLoopAsync` artık her periyotta
hem sıcaklığı hem kalan ömrü (varsa) trend deposuna yazıyor.

**UI:** `HealthPage.xaml`'e "Trend Geçmişi (Tüm Kayıt)" başlığı altında
iki yan yana grafik eklendi: sıcaklık (°C) ve kalan ömür (%, 0-100
sabit ölçek). `HealthViewModel`'e `HistoryTemperatureSeries`/
`HistoryRemainingLifeSeries` + paylaşılan `HistoryXAxes` ("dd MMM HH:mm"
biçimli, günleri de gösterir) + ayrı Y eksenleri eklendi. Bu koleksiyonlar
hem disk seçildiğinde (dosyadan tüm geçmiş yüklenerek) hem canlı izleme
sırasında (yeni nokta geldikçe) güncelleniyor — sayfayı yeniden açmaya
gerek kalmadan en güncel veriyi gösteriyor.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı (`Axis.MinLimit`/`MaxLimit`
derleme zamanında doğrulandı). `dotnet test`: 61/61 başarılı (bu madde
saf grafik/veri bağlama; SmartTrendPoint'in yeni alanı geriye dönük
uyumlu olduğundan ayrı test istenmedi). Uygulama başlatılıp 8 saniye
ayakta kaldığı doğrulandı (`Responding=True`).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — mevcut bir
trend JSON dosyasıyla (varsa) geçmiş grafiklerin gerçekten dolduğu,
uygulama birkaç periyot açık kaldığında yeni noktaların canlı olarak
eklendiği, kalan ömür grafiğinin 0-100 aralığında sabit kaldığı gözle
kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Core/Models/SmartTrendPoint.cs`,
`src/SerkonDiskSuite.App/ViewModels/HealthViewModel.cs`,
`Views/Pages/HealthPage.xaml`

### 27. ÖZELLİK: Rapor dışa aktarma (TAMAMLANDI — 2026-08-05)

`Core/Reporting/DiskReportBuilder.cs` (yeni): seçili diskin bilgisi +
SMART sağlığı (null olabilir) + son benchmark sonuçlarından iki çıktı
üretir:
- `BuildPlainText`: CrystalDiskInfo tarzı düz metin (disk bilgisi, SMART
  özeti + kritik uyarılar + tüm öznitelikler Türkçe etiketleriyle, son
  benchmark sonuçları Türkçe test adı + profil adıyla).
- `BuildJson`: aynı verinin girintili JSON'u (`GeneratedAt`/`Disk`/
  `Health`/`BenchmarkResults`).

`MainViewModel`'e iki komut eklendi:
- `ExportReportCommand`: `SaveFileDialog` ile kullanıcıdan bir .txt yolu
  ister, hem `<ad>.txt` hem `<ad>.json`'ı aynı anda yazar.
- `CopyReportToClipboardCommand`: düz metin özetini panoya kopyalar
  (`Clipboard.SetText`).

İkisi de seçili disk yokken devre dışı (`CanExportOrCopyReport`).
`MainWindow.xaml`'in alt durum çubuğuna "Panoya Kopyala"/"Rapor Dışa
Aktar" butonları eklendi.

**Doğrulama:** `DiskReportBuilderTests` — 5 test (model/seri no içeriyor,
SMART + kritik uyarı + Türkçe öznitelik etiketi içeriyor, Türkçe test
adı + profil adı içeriyor, health/sonuç yokken hata vermiyor, JSON
geçerli ve üç ana alanı içeriyor). `dotnet build`: 0 hata/0 uyarı.
`dotnet test`: **66/66 başarılı** (61 eski + 5 yeni). Uygulama
başlatılıp 8 saniye ayakta kaldığı doğrulandı (`Responding=True`).

**Ortam notu:** Bu maddede de canlı test sırasında bir kez gerçek UAC
izin penceresi çıktı ve ilk denemede iptal oldu (madde 11'deki gibi);
kullanıcı onaylayınca ikinci deneme başarılı oldu. Bu ortamda yükseltme
davranışı tutarsız — bazen sessiz otomatik yükseliyor, bazen gerçek
UAC promptu çıkarıp iptal olabiliyor.

**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — "Rapor Dışa
Aktar" ile gerçekten hem .txt hem .json dosyasının doğru içerikle
oluştuğu, "Panoya Kopyala" ile panoya yapıştırılan metnin doğru olduğu
gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Core/Reporting/DiskReportBuilder.cs`
(yeni), `src/SerkonDiskSuite.App/ViewModels/MainViewModel.cs`,
`Views/MainWindow.xaml`,
`tests/SerkonDiskSuite.Tests/DiskReportBuilderTests.cs` (yeni)

### 28. Madde 14 (İngilizce dil desteği) — KULLANICI KARARIYLA ATLANDI (2026-08-05)

Kullanıcıyla konuşuldu: tr/en lokalizasyonun kapsamı (uygulamadaki
~150-250 farklı metnin kaynak dosyalarına taşınması, dil seçici
ayarlar arayüzü, sayı/tarih biçiminin dile göre dinamikleşmesi) diğer
maddelere göre çok daha büyük olduğu için önce nasıl ilerlenmek
istendiği soruldu (tam kapsam / küçük MVP / ayrı oturum). Kullanıcı
"İngilizce dil desteğine gerek yok" diyerek bu maddeyi tamamen
kaldırdı — hiçbir kod değişikliği yapılmadı, commit atılmadı.

**Bu turun (madde 1-13) tüm işleri tamamlandı ve commit edildi.**

### 29. BUG: Madde 3'ün ScrollViewer hack'i navigasyon journal'ını bozmuştu (ÇÖZÜLDÜ — 2026-08-05)

Kullanıcı, uygulamanın açılışta artık navigasyon yapamadığını bildirdi.
Global hata yakalayıcının yazdığı
`%LOCALAPPDATA%\SerkonDiskSuite\logs\crash-20260805-083547-989.log`
dosyası okunup tam yığın izi doğrulandı:

```
System.InvalidOperationException: Bu işlem yalnızca, Frame kendi
günlüğüne sahip olduğunda kullanılabilir.
   at System.Windows.Controls.Frame.RemoveBackEntry()
   at Wpf.Ui.Controls.NavigationView.OnNavigationViewContentPresenterNavigated(...)
   at System.Windows.Navigation.NavigationService.FireNavigated(...)
   ...
```

**Kök neden:** Madde 3'te (bkz. yukarıda #17) eklenen
`MainWindow.xaml.cs`'teki `DisableDynamicScrollViewer` — `VisualTreeHelper`
ile `NavigationViewContentPresenter`'ı bulup
`IsDynamicScrollViewerEnabledProperty`'yi `SetValue` ile zorla `false`
yapıyordu (CLR set erişeni `protected` olduğundan Style Setter
kullanılamamıştı). Bu presenter aslında bir `Frame` türevi ve
`JournalOwnership=UsesParentJournal` ile çalışıyor (kendi journal'ına
sahip değil, ebeveynin journal'ını kullanıyor) — `IsDynamicScrollViewerEnabled`'ı
dıştan zorlamak, `NavigationView`'ın kendi iç navigasyon/journal
yönetimiyle çakıştı ve bir sonraki `Navigate` çağrısında
`RemoveBackEntry()`'nin "Frame kendi journal'ına sahip değilken"
çalışmasına, dolayısıyla istisnaya yol açtı. Yani madde 3'ün "düzeltmesi"
o an çökmüyordu ama gizli bir navigasyon durumu bozukluğu bırakmıştı;
bir sonraki gerçek navigasyon denemesinde ortaya çıktı.

**Düzeltme:**
1. `DisableDynamicScrollViewer` metodu ve çağrısı `MainWindow.xaml.cs`'ten
   tamamen kaldırıldı — `IsDynamicScrollViewerEnabled` artık hiç
   değiştirilmiyor, WPF-UI'nin varsayılanında (`true`) kalıyor. Bu yol
   bir daha denenmeyecek.
2. Yerleşim sorunu (sayfa içeriğinin ScrollViewer içinde dikeyde
   ortalanması) için üç seçenek değerlendirildi:
   - (a) Sayfa kök elemanının `MinHeight`'ini ata `Frame`'in
     `ActualHeight`'ine bağlamak — **seçildi**. Standart, iyi bilinen bir
     WPF tekniği (Frame/ScrollViewer içinde barındırılan içeriğin
     viewport'u doldurmasını sağlar); NavigationView'ın hiçbir iç
     durumuna (journal, ScrollViewer sarmalama) dokunmuyor, salt
     bağlama — sıfır risk.
   - (b) Grid'lerde "*" yerine sabit/oransal yükseklik — reddedildi:
     tüm sayfaların yeniden tasarlanmasını gerektirir, pencere yeniden
     boyutlandığında hâlâ doğru esnemeyebilir, çok daha büyük bir değişiklik.
   - (c) Yalnızca `VerticalContentAlignment=Stretch` — zaten madde 3'te
     `NavigationViewContentPresenter` stiline eklenmişti (journal'a
     dokunmadığı için o kısım korundu) ama tek başına yeterli değildi
     (ScrollViewer içeriği sonsuz yükseklikle ölçtüğü için Stretch hizalama
     tek başına Grid'in "*" satırlarını genişletmeye yetmiyor).
   Sonuç: (a) + korunan (c) birlikte uygulandı. `HealthPage.xaml`,
   `BenchmarkPage.xaml`, `SystemPage.xaml`, `DiagnosticsPage.xaml`'in
   kök `Page` elemanına
   `MinHeight="{Binding ActualHeight, RelativeSource={RelativeSource AncestorType=Frame}}"`
   eklendi.

**Doğrulama:** `dotnet build`: 0 hata/0 uyarı. `dotnet test`: 66/66
başarılı. Uygulama başlatılıp 8 saniye ayakta kaldığı VE
`%LOCALAPPDATA%\SerkonDiskSuite\logs\` altında **yeni hiçbir crash
dosyası oluşmadığı** doğrulandı (yalnızca "ayakta kalmak" yeterli
sayılmadı — kullanıcının talimatı gereği hata penceresi çıkıp
çıkmadığı da kontrol edildi).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — sayfalar
arası gerçek navigasyonun (Sağlık ↔ Benchmark ↔ Sistem ↔ Teşhis) artık
sorunsuz çalıştığı VE madde 3'ün asıl hedefinin (içeriğin dikeyde
ortalanmaması, disk listesinin/sayfa içeriğinin baştan sona dolması)
bu MinHeight yaklaşımıyla da gerçekten sağlandığı gözle kontrol
edilmeli — bu ikincisi bu ajan oturumunda görsel olarak doğrulanamıyor.

**Değişiklik:** `src/SerkonDiskSuite.App/Views/MainWindow.xaml.cs`,
`Resources/Theme.xaml`, `Views/Pages/HealthPage.xaml`,
`Views/Pages/BenchmarkPage.xaml`, `Views/Pages/SystemPage.xaml`,
`Views/Pages/DiagnosticsPage.xaml`

### 30. BUG: Madde 29'un düzeltmesi işe yaramamıştı, gerçek kök neden bulundu (ÇÖZÜLDÜ — 2026-08-05)

Kullanıcı, madde 29'daki düzeltmenin işe yaramadığını ve önceki
doğrulamamın hatalı olduğunu bildirdi (çöküş penceresi çıkmasına
rağmen süreç `DispatcherUnhandledException`'ın `e.Handled=true`
yapması sayesinde ayakta kalmaya devam ediyordu; "süreç ayakta" tek
başına yeterli bir kanıt değildi). Yeni crash log
(`crash-20260805-093856-288.log`) okunup doğrulandı: **birebir aynı**
yığın izi (`Frame.RemoveBackEntry()` → "Bu işlem yalnızca Frame kendi
günlüğüne sahip olduğunda kullanılabilir").

**Gerçek kök neden (bu kez kaynak koddan doğrulandı, tahmin edilmedi):**
Madde 29'un düzeltmesi yanlış bileşeni suçlamıştı (kod tarafındaki
`SetValue` hack'i) — o kaldırıldı ama asıl suçlu, madde 3'te
`Resources/Theme.xaml`'e eklenen ve **`BasedOn` içermeyen**
`<Style TargetType="{x:Type ui:NavigationViewContentPresenter}">`
idi. lepoco/wpfui kaynak kodundan (GitHub, doğrudan fetch edilip
doğrulandı) `NavigationViewContentPresenter`'ın static constructor'ı:

```csharp
JournalOwnershipProperty.OverrideMetadata(
    typeof(NavigationViewContentPresenter),
    new FrameworkPropertyMetadata(JournalOwnership.UsesParentJournal));
```

— yani bu tipin HAM (metadata) varsayılanı `UsesParentJournal`.
`NavigationView.OnNavigationViewContentPresenterNavigated` ise koşulsuz
`frame.RemoveBackEntry()` çağırıyor; bu yalnızca `JournalOwnership=
OwnsJournal` iken çalışır. Böyle bir uygulamanın normalde çalışması,
WPF-UI'nin kendi `ui:ControlsDictionary`'sindeki (App.xaml'de bizim
Theme.xaml'den ÖNCE birleştirilen) implicit style'ın bunu
`OwnsJournal`'a çevirdiğini gösteriyor. `BasedOn` OLMADAN aynı tipte
(aynı örtük anahtarla) tanımlanan bir Style, WPF'te o anahtarla daha
önce bulunan kütüphane stilini TAMAMEN EZER — Template'i ve
`JournalOwnership` Setter'ı dahil. Madde 3'te bu Style `BasedOn`'suz
eklenmişti; bu yüzden ekleme anından itibaren `JournalOwnership` sessizce
ham varsayılana (`UsesParentJournal`) düşmüştü — hiçbir gerçek navigasyon
denemesi olana kadar (yalnızca ilk sayfa yükleniyordu, sonraki bir
Navigate hiç tetiklenmiyordu) bu fark edilmedi.

**Düzeltme:** Aynı `Style`'a:
- `BasedOn="{StaticResource {x:Type ui:NavigationViewContentPresenter}}"`
  eklendi — WPF, bu öz-referanslı `BasedOn` deyiminde kendi tanımını
  değil, `MergedDictionaries` zincirinde KENDİSİNDEN ÖNCE gelen aynı
  anahtarlı tanımı (WPF-UI'nin `ui:ControlsDictionary`'si) bulur; böylece
  Template + tüm orijinal Setter'lar (JournalOwnership dahil) korunur.
- Çifte güvence olarak `<Setter Property="JournalOwnership" Value="OwnsJournal" />`
  açıkça eklendi (`JournalOwnership`, `IsDynamicScrollViewerEnabled`'ın
  aksine public bir setter'a sahip standart bir `Frame` özelliği; Style
  Setter ile sorunsuz ayarlanabiliyor).

**Doğrulama (kullanıcının istediği kesin yöntemle):** Uygulama
başlatılmadan ÖNCE `%LOCALAPPDATA%\SerkonDiskSuite\logs\` içeriği
alındı (boş), uygulama başlatılıp **10 saniye** beklendi, dizin
TEKRAR listelendi (yine boş) ve `Compare-Object` ile önce/sonra
karşılaştırıldı — **fark yok, yeni dosya oluşmadı**. Süreç ayrıca
ayakta ve `Responding=True`. `dotnet build`: 0 hata/0 uyarı.
`dotnet test`: 66/66 başarılı.

**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — dört sayfa
arasında (Sağlık/Benchmark/Sistem/Teşhis) gerçek navigasyonun artık
istisna fırlatmadan çalıştığı, ve madde 3'ün asıl hedefinin (Stretch
hizalama + `BasedOn` ile geri kazanılan orijinal Template) içerik
yerleşimini bozmadığı gözle kontrol edilmeli — bu ajan oturumunda görsel
render doğrulanamıyor, yalnızca çökme/log kontrolü yapılabiliyor.

**Ders:** WPF'te bir kütüphanenin kontrolüne ait `TargetType` Style'ı
ASLA `BasedOn` olmadan yazılmamalı — bu, o tipin implicit stilini (ve
o stilin sağladığı, görünmeyen ama davranışsal olarak kritik olabilecek
Setter'ları) sessizce siler.

**Değişiklik:** `src/SerkonDiskSuite.App/Resources/Theme.xaml`

### 32. BUG: Yerleşim ortalama + grafiklerin sıfır yükseklikte çökmesi — GERÇEK kök neden (ÇÖZÜLDÜ — 2026-08-05)

Kullanıcı madde 30'un düzeltmesinden SONRA da her sayfada içeriğin dikeyde
ortalandığını, MainWindow'un disk listesi panelinin de aynı sorunu
yaşadığını (yani sorunun NavigationView'a özel olmadığını) ve
grafiklerin `Height="160"` verilse de ~15px'lik ince şerit halinde
kaldığını bildirdi. Bu turda TAHMİN EDİLMEDİ: `MainWindow.xaml.cs`'e
geçici bir teşhis kodu eklendi (Loaded'da görsel ağacı kökten aşağı
dolaşıp her elemanın tipi + gerçek W/H + MinHeight + Horizontal/
VerticalAlignment'ını `%LOCALAPPDATA%\SerkonDiskSuite\logs\visualtree.txt`'e
yazan `DumpVisualTreeDiagnostics`/`DumpElement`).

**Ortam kısıtı nedeniyle bulunan çözüm:** Önceki oturumdan kalan
yükseltilmiş süreç (PID 35928) hâlâ kapatılamadığından gerçek
`bin\...\win-x64\` çıktısı kilitliydi. `dotnet build ... -o <geçici
klasör>` ile projeyi tekrarlanan (run_diag1/2/3) geçici, kilitsiz
çıktı klasörlerine derleyip oradan çalıştırarak devam edildi — bu,
orijinal kilitli süreci kapatmaya gerek kalmadan build+launch+doğrulama
döngüsünü sürdürmeyi sağladı.

**Gerçek kök neden (döküm dosyasından doğrudan okundu):** `ui:Card`'ın
kendi varsayılan stili `VerticalAlignment="Center"` kullanıyor — dökümde
HER `Card` örneğinde tutarlı şekilde görüldü (ör. disk listesi Card'ı
`H=227,0` iken ayrıldığı alan `H=601,0`; grafik Card'ları `H=160,0`
olsa da içteki `ContentBorder` yalnızca `H=18,0`, `CartesianChart` ise
tam `H=0,0`). Bu TEK kök neden iki farklı semptomu açıklıyor:
1. **Yerleşim:** Card kendi doğal içerik boyutuna küçülüp ayrılan alan
   içinde dikeyde ortalanıyor (Stretch olmadığı için "*" satırlar/kalan
   alan asla dolmuyor) — hem MainWindow'un disk listesi Card'ında hem
   her sayfanın kök Card'ında.
2. **Grafikler:** Card'a `Height="160"` verilse de Card'ın kendi iç
   şablonundaki `ContentBorder`/`ContentPresenter` da `VerticalAlignment`
   miras aldığından içeriğe göre boyutlanıyor. `CartesianChart`'ın
   (metin gibi doğal bir "içerik boyutu" olmadığından) stretch
   edilmediğinde ölçülecek referansı kalmıyor ve `H=0`'a çöküyor.

**Düzeltme:** Madde 30'un dersiyle (`BasedOn` olmadan tanımlanan bir
`TargetType` Style, kütüphanenin implicit stilini TAMAMEN ezer)
`Resources/Theme.xaml`'e `BasedOn` ile `ui:Card` için
`VerticalAlignment="Stretch"` + `VerticalContentAlignment="Stretch"`
eklendi — Template ve diğer tüm Setter'lar korunuyor. Küçük özet
kartları (UniformGrid içindeki sağlık kutuları, benchmark sonuç
kartları) `StackPanel`/`UniformGrid` içinde olduğundan bu değişiklikten
etkilenmiyor (o panel türleri çocuğun `VerticalAlignment`'ından
bağımsız olarak doğal/sabit boyut veriyor — bu da döküm dosyasından
doğrulandı, boyutları değişmedi).

**Sayısal doğrulama (aynı teşhis dökümüyle, düzeltmeden önce/sonra):**
| Eleman | Önce | Sonra |
|---|---|---|
| Disk listesi Card (H, VA) | 227,0 / Center | 593,0 / Stretch |
| HealthPage kök Card (VA) | Center | Stretch |
| Sıcaklık grafiği CartesianChart (H) | 0,0 | 142,0 |
| Trend geçmişi grafik 1 CartesianChart (H) | 0,0 | 142,0 |
| Trend geçmişi grafik 2 CartesianChart (H) | 0,0 | 142,0 |

Kök neden bulunup düzeltildikten sonra `MainWindow.xaml.cs`'teki geçici
teşhis kodu (`DumpVisualTreeDiagnostics`, `DumpElement`, çağrısı ve
ilgili `using`'ler) tamamen kaldırıldı.

**Doğrulama:** `dotnet build` (geçici çıktı klasörüne): 0 hata/0 uyarı.
Launch öncesi/sonrası log dizini karşılaştırması: fark yok, yeni crash
dosyası oluşmadı; süreç ayakta ve `Responding=True`.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — bu ajan
oturumunda piksel render'ı görülemiyor, yalnızca `ActualHeight`/
`VerticalAlignment` sayısal değerleri doğrulanabildi (ki bu semptomu
doğrudan açıklayan ölçülebilir kanıt).

**Değişiklik:** `src/SerkonDiskSuite.App/Resources/Theme.xaml`,
`Views/MainWindow.xaml.cs` (teşhis kodu eklendi + kaldırıldı)

### 33. BUG: SMART tablosu ID kolonu NVMe'de hâlâ görünüyordu (ÇÖZÜLDÜ — 2026-08-05)

Madde 19'daki `ElementName=Root` bağlaması çalışmıyordu çünkü
`DataGridColumn` görsel ağaçta değildir; `ElementName` bağlamaları
görsel/mantıksal ağaç üzerinden çözülür ve bir kolon tanımı bu ağacın
parçası olmadığından `Root` adlı elemanı (Page'i) hiçbir zaman
bulamıyordu — bağlama sessizce başarısız oluyor, kolon hep görünür
kalıyordu.

**Düzeltme:** Standart WPF çözümü — bir `FrameworkElement` proxy.
`HealthPage.xaml`'deki `DataGrid.Resources`'a
`<FrameworkElement x:Key="ProxyElement" DataContext="{Binding}" />`
eklendi (DataGrid görsel ağaçta olduğundan `{Binding}` DataGrid'in
miras aldığı DataContext'e, yani `HealthViewModel`'e çözülüyor). ID
kolonunun `Visibility` bağlaması artık
`Source={StaticResource ProxyElement}, Path=DataContext.IsIdColumnVisible`
kullanıyor.

**Doğrulama:** Madde 32'nin aynı çıktı klasörü + launch döngüsüyle
(kilitli orijinal süreç nedeniyle) doğrulandı: uygulama çöküşsüz
başlıyor (binding hatası bir `XamlParseException`/çöküşe dönüşmezdi
zaten — WPF binding hataları varsayılan olarak sessiz kalır — ama
kodun kendisi artık doğru bağlama yolunu kullanıyor).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — bir NVMe
diskte ID kolonunun gerçekten gizlendiği, bir SATA diskte (varsa)
görünür kaldığı gözle kontrol edilmeli; bu ajan oturumunda gerçek bir
SATA disk yoktu (bkz. madde 34 — bu makinedeki tek disk NVMe).

**Değişiklik:** `src/SerkonDiskSuite.App/Views/Pages/HealthPage.xaml`

### 34. BUG: NVMe self-test durumu boş dönüyordu (ÇÖZÜLDÜ — 2026-08-05)

Madde 10/24'teki `ParseSelfTestStatus` yalnızca ATA'ya özgü
`ata_smart_data.self_test.status` alanını okuyordu; bu makinedeki (ve
büyük olasılıkla kullanıcının makinesindeki) tüm diskler NVMe olduğundan
özellik hiç veri üretmiyordu.

**Gerçek NVMe JSON şeması** (tahmin edilmedi — bu makinedeki gerçek
KINGSTON SNV2S1000G üzerinde `tools\smartctl.exe -a --json=c /dev/sda -d
nvme` çalıştırılıp çıktı okunarak doğrulandı; `--scan` ile cihaz
otomatik keşfedildi, yönetici hakkı olmadan da SMART verisi tam
okunabildi):

```json
"nvme_self_test_log": {
  "nsid": -1,
  "current_self_test_operation": { "value": 0, "string": "No self-test in progress" },
  "table": [
    { "self_test_code": { "value": 1, "string": "Short" },
      "self_test_result": { "value": 0, "string": "Completed without error" },
      "power_on_hours": 1069 }
  ]
}
```

**Düzeltme:** `ParseSelfTestStatus`'a NVMe dalı eklendi: `current_self_test_
operation.value != 0` çalışıyor demek; çalışmıyorsa `table[0]`'daki
(en yeni kayıt) `self_test_code`/`self_test_result` ile bir özet
oluşturuluyor (ör. "Short: Completed without error"), `self_test_result.
value==0` "geçti" (Passed=true) sayılıyor. NVMe'de test ÇALIŞIRKEN
kalan yüzdeyi taşıyan alan adı bu makinede DOĞRULANAMADI (gerçek bir
self-test tetiklenmedi — kısa test dahi olsa gerçek donanımda dakikalar
sürer ve kullanıcının diskini test etmeye zorlamak riskli olurdu); bu
yüzden NVMe için `PercentRemaining` her zaman `null` döner, tahmini bir
alan adı KULLANILMADI. Ne ATA ne NVMe self-test verisi bulunamazsa
(disk gerçekten desteklemiyor olabilir), arayüz artık boş bırakmıyor —
"Bu disk self-test durumu raporlamıyor." mesajı dönüyor (benzer şekilde
NVMe'de log var ama hiç kayıt yoksa "Bu disk için self-test kaydı yok.").

**Doğrulama:** `SmartctlSelfTestParsingTests`'e gerçek yakalanan JSON
şemasıyla 3 yeni test eklendi (geçmişli/çalışmıyor -> son sonucu
gösteriyor, çalışıyor -> yüzdesiz "çalışıyor" durumu, geçmiş yok ->
net mesaj) + mevcut "alan yok" testi yeni mesaja güncellendi.
`dotnet test`: **69/69 başarılı**.
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — Teşhis
sayfasında artık "Durum:" alanının NVMe'de de dolu geldiği, gerçek bir
self-test (önce "Kısa") başlatılıp tamamlandığında doğru sonucun
göründüğü gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Infrastructure/Smart/SmartctlSmartProvider.cs`,
`tests/SerkonDiskSuite.Tests/SmartctlSelfTestParsingTests.cs`

### 35. Küçük düzeltmeler: Kritik Uyarı=0 satırı + Profil ComboBox placeholder (TAMAMLANDI — 2026-08-05)

- SMART tablosunda NVMe `critical_warning` özniteliği değeri 0 (uyarı
  yok) ise artık listeye hiç eklenmiyor (`ExtractAttributes`) — bu
  bilgi zaten Teşhis sayfasında (`CriticalWarningFlags`, madde 25)
  ayrıntılı gösteriliyor, tabloda gürültü yapmasın.
- Benchmark sayfasındaki Profil ComboBox'ı artık boş başlamıyor:
  `BenchmarkProfiles.Custom` adlı bir "Özel" sentinel eklendi,
  `BenchmarkViewModel.Profiles` listesinin başında görünüyor ve
  `SelectedProfile` varsayılan olarak buna ayarlı geliyor.
  `StartAsync`, `Custom`'a başvuru eşitliğiyle (`ReferenceEquals`)
  bakıp bu sentinel seçiliyken hiçbir profil uygulamıyor (manuel
  ayarlar aynen kullanılıyor).

**Doğrulama:** `dotnet test`: 69/69 başarılı (mevcut
`BenchmarkProfilesTests` `All`'ın hâlâ sadece 4 gerçek profili
içerdiğini doğruluyor, `Custom` ayrı tutulduğu için etkilenmedi).
**Görsel doğrulama kullanıcı tarafından elle yapılmalı** — ComboBox'ın
artık "Özel" ile başladığı, bir NVMe diskte kritik uyarı 0 iken
tabloda o satırın hiç görünmediği gözle kontrol edilmeli.

**Değişiklik:** `src/SerkonDiskSuite.Infrastructure/Smart/SmartctlSmartProvider.cs`,
`src/SerkonDiskSuite.Core/Models/BenchmarkProfiles.cs`,
`src/SerkonDiskSuite.App/ViewModels/BenchmarkViewModel.cs`

**Ortam notu (bu 4 madde boyunca geçerli):** Kullanıcı oturum sırasında
uzaklaştığı için önceki kilitli süreç (PID 35928) hiç kapatılamadı.
Tüm build+launch doğrulamaları `dotnet build ... -o <geçici klasör>`
ile geçici, kilitsiz çıktı klasörlerine (scratchpad altında
run_diag1/2/3) derleyip oradan çalıştırılarak yapıldı. **Gerçek proje
çıktısı (`src/SerkonDiskSuite.App/bin/...`) hâlâ eski süreç tarafından
kilitli** — kullanıcı bir sonraki `dotnet build`'den önce açık
SerkonDiskSuite pencerelerini (bu oturumda biriken birden fazla örnek
olabilir) elle kapatmalı.

## Devam eden iş

- Yok. Disk format/partition özelliğine bu turda da kasıtlı olarak
  girilmedi — kullanıcı ayrıca konuşulacağını belirtti.

## Sıradaki işler (öncelik sırasına göre)

1. **Kullanıcı elle kontrol etmeli (bu ajan oturumunda WPF render'ı
   görülemiyor — yönetici olarak çalıştırıp UAC geçilmesi gerekiyor):**
   - `ui:FluentWindow`'un Mica arka planı + yuvarlak köşelerinin
     gerçekten uygulandığı (bazı Windows sürümlerinde/uzak masaüstünde
     Mica devre dışı kalabilir, bu durumda WPF-UI'nin düz renk yedeğine
     düşmesi beklenir — bu bir hata değildir).
   - `ui:NavigationView`'ın üstte (Top) sekme gibi göründüğü, üç sayfa
     (Sağlık/Benchmark/Sistem) arasında geçişin çalıştığı, simgelerin
     (`Heart24`/`DataHistogram24`/`Desktop24`) doğru render edildiği.
   - DataGrid okunabilirliği (satır/hücre metni, seçim ve hover renkleri,
     alternating row) — bu stiller WPF-UI'de yok, kendi özel stilimiz
     hâlâ geçerli.
   - Disk detay şeridi, sağlık özet kartları (3x3 `ui:Card` grid) ve
     benchmark IOPS/blok boyutu alanlarının gerçek SMART/benchmark
     verisiyle doğru dolduğu.
   - Sıcaklık grafiğinin gerçekten çizildiği; Sağlık sayfasından başka
     bir sayfaya geçilince izleme döngüsünün durduğu (`INavigationAware`
     üzerinden — ör. Görev Yöneticisi'nde smartctl alt sürecinin sayfadan
     çıkınca tetiklenmediğini gözlemleyerek).
   - Uygulamayı kapatıp yeniden açtığınızda sıcaklık grafiğinin önceki
     oturumdan kalan (son 15 dakikaya düşen) noktalarla başladığı ve
     `%LOCALAPPDATA%\SerkonDiskSuite\trend\` altında JSON dosyalarının
     oluştuğu.
   - Benchmark ilerleme çubuğunun artık gerçek yüzdeyle (donmadan,
     geriye gitmeden) ilerlediği.
2. Firmware güncelleme uyarısı, çoklu dil desteği (ileri aşama fikirleri
   — henüz başlanmadı).
3. Disk format/partition özelliği — kullanıcıyla ayrıca konuşulacak,
   bu turda kasıtlı olarak dokunulmadı.

## Bilinen buglar

- Yok (aktif olarak bilinen başka bug yok; WPF-UI migrasyonu ve ilgili
  tüm özellikler henüz gerçek donanımda gözle uçtan uca doğrulanmadı —
  bkz. yukarıdaki manuel kontrol listesi).

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
