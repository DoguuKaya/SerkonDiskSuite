namespace SerkonDiskSuite.Core.Models;

/// <summary>RAM modülü tipi. WMI'nin ham SMBIOSMemoryType kodundan türetilir
/// (24=DDR3, 26=DDR4, 34=DDR5 — SMBIOS spesifikasyonu Tablo 78; WmiSystemInfoProvider'da
/// bu eşleme uygulanır). Diğer tüm kodlar (veya alan boşsa) Unknown.</summary>
public enum RamType
{
    Unknown,
    Ddr3,
    Ddr4,
    Ddr5,
}

/// <summary>Fiziksel bir RAM modülünün (DIMM/SODIMM) bilgisi. Alan WMI'dan okunamazsa
/// null bırakılır — tahmini değer üretilmez.</summary>
public sealed record RamModuleInfo(
    long CapacityBytes,
    int? SpeedMHz,
    RamType Type,
    string? Slot);
