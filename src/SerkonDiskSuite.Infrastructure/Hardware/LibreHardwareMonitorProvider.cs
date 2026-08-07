using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Hardware;

/// <summary>CPU/GPU/RAM okumasını LibreHardwareMonitorLib üzerinden sağlar.
/// <see cref="Computer"/> nesnesi uygulama ömrü boyunca bir kez açılır (yeniden
/// oluşturmak pahalı ve bazı donanımlarda sorunlu olabilir); her okuma yalnızca
/// <see cref="Computer.Accept(IVisitor)"/> ile mevcut sensörleri güncelleyip okur.
/// Bulunamayan sensörler null döner, tahmini değer üretilmez.</summary>
[SupportedOSPlatform("windows")]
public sealed class LibreHardwareMonitorProvider : IHardwareMonitorProvider, IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor = new();
    private readonly object _lock = new();
    private bool _disposed;

    public LibreHardwareMonitorProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                _computer.Accept(_updateVisitor);

                double? cpuTemperature = null;
                double? cpuLoad = null;
                string? gpuName = null;
                double? gpuTemperature = null;
                double? gpuLoad = null;
                long? gpuMemoryUsedBytes = null;
                long? ramUsedBytes = null;
                long? ramTotalBytes = null;

                foreach (IHardware hw in _computer.Hardware)
                {
                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            cpuTemperature = FindSensorValue(hw, SensorType.Temperature, "CPU Package")
                                ?? FindSensorValue(hw, SensorType.Temperature, "Core Max");
                            cpuLoad = FindSensorValue(hw, SensorType.Load, "CPU Total");
                            break;

                        case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                            gpuName = hw.Name;
                            gpuTemperature = FindFirstSensorValue(hw, SensorType.Temperature);
                            gpuLoad = FindSensorValue(hw, SensorType.Load, "D3D 3D")
                                ?? FindFirstSensorValue(hw, SensorType.Load);
                            gpuMemoryUsedBytes = FindMemoryUsedBytes(hw);
                            break;

                        case HardwareType.Memory when hw.Name == "Total Memory":
                            double? usedGb = FindSensorValue(hw, SensorType.Data, "Memory Used");
                            double? availableGb = FindSensorValue(hw, SensorType.Data, "Memory Available");
                            if (usedGb.HasValue)
                            {
                                ramUsedBytes = GigabytesToBytes(usedGb.Value);
                            }
                            if (usedGb.HasValue && availableGb.HasValue)
                            {
                                ramTotalBytes = GigabytesToBytes(usedGb.Value + availableGb.Value);
                            }
                            break;
                    }
                }

                return new HardwareSnapshot
                {
                    CpuTemperatureCelsius = cpuTemperature,
                    CpuLoadPercent = cpuLoad,
                    GpuName = gpuName,
                    GpuTemperatureCelsius = gpuTemperature,
                    GpuLoadPercent = gpuLoad,
                    GpuMemoryUsedBytes = gpuMemoryUsedBytes,
                    RamUsedBytes = ramUsedBytes,
                    RamTotalBytes = ramTotalBytes,
                };
            }
        }, ct);

    private static double? FindSensorValue(IHardware hardware, SensorType type, string name) =>
        hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;

    private static double? FindFirstSensorValue(IHardware hardware, SensorType type) =>
        hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Value.HasValue)?.Value;

    /// <summary>GPU'nun kullandığı bellek miktarını bayt olarak bulur. Entegre GPU'larda
    /// "D3D Shared Memory Used" (SmallData, MB); ayrı kartlarda genelde "D3D Dedicated
    /// Memory Used" (SmallData, MB) sensörü bulunur — ADIM 1'in probu bu makinede yalnızca
    /// entegre GPU ile doğrulayabildi, ayrı kart senaryosu isim kalıbına göre en iyi tahmindir.</summary>
    private static long? FindMemoryUsedBytes(IHardware hardware)
    {
        ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
            s.Name.Contains("Dedicated Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
            ?? hardware.Sensors.FirstOrDefault(s =>
            s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);

        if (sensor is null)
        {
            return null;
        }

        double megabytes = sensor.SensorType == SensorType.Data ? sensor.Value!.Value * 1024 : sensor.Value!.Value;
        return (long)(megabytes * 1024 * 1024);
    }

    private static long GigabytesToBytes(double gigabytes) => (long)(gigabytes * 1024 * 1024 * 1024);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _computer.Close();
        }
    }

    /// <summary>Resmi LibreHardwareMonitor deseni: her donanımı ve SubHardware'ini
    /// (ör. Motherboard'un SuperIO çipi) recurse ile günceller.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
