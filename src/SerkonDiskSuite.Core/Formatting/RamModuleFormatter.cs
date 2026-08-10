using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Formatting;

/// <summary>RAM modül listesinden Görev Yöneticisi tarzı kısa bir özet üretir
/// (ör. "2x16 GB, DDR4-3200").</summary>
public static class RamModuleFormatter
{
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    /// <summary>Modüller listesinden özet dizge üretir. Hepsi aynı kapasite/hız/tipteyse
    /// tek satırda özetlenir; farklıysa her modül kendi satırında (slot etiketiyle)
    /// listelenir. Liste boşsa null döner.</summary>
    public static string? FormatSummary(IReadOnlyList<RamModuleInfo> modules)
    {
        if (modules.Count == 0)
        {
            return null;
        }

        var first = modules[0];
        bool allSame = modules.All(m =>
            m.CapacityBytes == first.CapacityBytes &&
            m.SpeedMHz == first.SpeedMHz &&
            m.Type == first.Type);

        return allSame
            ? $"{modules.Count}x{FormatModule(first)}"
            : string.Join(Environment.NewLine, modules.Select(FormatModuleWithSlot));
    }

    private static string FormatModuleWithSlot(RamModuleInfo module)
    {
        string body = FormatModule(module);
        return module.Slot is { Length: > 0 } slot ? $"{slot}: {body}" : body;
    }

    private static string FormatModule(RamModuleInfo module)
    {
        string capacity = FormatCapacity(module.CapacityBytes);
        string typeSpeed = FormatTypeSpeed(module);
        return typeSpeed.Length > 0 ? $"{capacity}, {typeSpeed}" : capacity;
    }

    private static string FormatCapacity(long bytes) =>
        $"{DisplayFormatting.FormatNumber(bytes / BytesPerGb, 0)} GB";

    private static string FormatTypeSpeed(RamModuleInfo module)
    {
        string typeName = module.Type switch
        {
            RamType.Ddr3 => "DDR3",
            RamType.Ddr4 => "DDR4",
            RamType.Ddr5 => "DDR5",
            _ => "",
        };

        if (typeName.Length == 0)
        {
            return module.SpeedMHz is { } speed ? $"{speed} MHz" : "";
        }

        return module.SpeedMHz is { } s ? $"{typeName}-{s}" : typeName;
    }
}
