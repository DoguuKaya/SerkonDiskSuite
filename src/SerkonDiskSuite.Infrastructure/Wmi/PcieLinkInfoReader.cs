using System.Diagnostics;
using System.Text.Json;

namespace SerkonDiskSuite.Infrastructure.Wmi;

/// <summary>
/// NVMe disklerin bağlı olduğu PCIe link hızı/genişliğini okur.
///
/// Bu bilgi (DEVPKEY_PciDevice_CurrentLinkSpeed/Width) klasik WMI (System.Management)
/// üzerinden sorgulanamaz; disk PNP cihazından üst PCI denetleyici cihazına kadar
/// DEVPKEY_Device_Parent zincirini izleyip cihaz özelliklerini okumak gerekir.
/// Windows'un kendi getirdiği PnpDevice PowerShell modülü (Get-PnpDeviceProperty) bu
/// zinciri ve DEVPKEY sorgusunu zaten doğru şekilde yapıyor; smartctl'de olduğu gibi
/// olgun bir aracı sarmalamak, ham SetupAPI P/Invoke koduna göre çok daha az risklidir.
/// </summary>
internal static class PcieLinkInfoReader
{
    private const string Script = """
        param([string]$InstanceId)
        $ErrorActionPreference = 'SilentlyContinue'
        $current = $InstanceId
        for ($i = 0; $i -lt 8; $i++) {
            $speed = Get-PnpDeviceProperty -InstanceId $current -KeyName DEVPKEY_PciDevice_CurrentLinkSpeed
            if ($speed -and $null -ne $speed.Data) {
                $width = Get-PnpDeviceProperty -InstanceId $current -KeyName DEVPKEY_PciDevice_CurrentLinkWidth
                $maxSpeed = Get-PnpDeviceProperty -InstanceId $current -KeyName DEVPKEY_PciDevice_MaxLinkSpeed
                $maxWidth = Get-PnpDeviceProperty -InstanceId $current -KeyName DEVPKEY_PciDevice_MaxLinkWidth
                [PSCustomObject]@{
                    CurrentSpeed = $speed.Data
                    CurrentWidth = $width.Data
                    MaxSpeed = $maxSpeed.Data
                    MaxWidth = $maxWidth.Data
                } | ConvertTo-Json -Compress
                return
            }
            $parent = Get-PnpDeviceProperty -InstanceId $current -KeyName DEVPKEY_Device_Parent
            if (-not $parent -or -not $parent.Data) { return }
            $current = $parent.Data
        }
        """;

    /// <summary>
    /// Verilen disk PNPDeviceID'sinden yukarı doğru PCI denetleyicisini bulup
    /// "PCIe 3.0 x4 (maks. PCIe 4.0 x4)" gibi okunabilir bir metin döner.
    /// Bulunamazsa veya hata olursa null döner (SMART/WMI katmanlarındaki gibi sessizce yutulur).
    /// </summary>
    public static async Task<string?> TryReadAsync(string pnpDeviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            // -Command sonrasindaki tum argumanlar tek bir script metni olarak birlestirilip
            // calistirilir; ayri bir argumana bindirilmez. Bu yuzden pnpDeviceId, script
            // metninin icine tek-tirnakli bir PowerShell string literali olarak gomulmeli
            // (aksi halde PNPDeviceID'deki '&' karakterleri PowerShell operatoru sanilir).
            string escapedId = pnpDeviceId.Replace("'", "''");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add($"& {{{Script}}} '{escapedId}'");

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stdout = (await stdoutTask).Trim();

            if (string.IsNullOrEmpty(stdout))
                return null;

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            int currentSpeed = root.GetProperty("CurrentSpeed").GetInt32();
            int currentWidth = root.GetProperty("CurrentWidth").GetInt32();
            int maxSpeed = root.GetProperty("MaxSpeed").GetInt32();
            int maxWidth = root.GetProperty("MaxWidth").GetInt32();

            string current = $"PCIe {SpeedToGeneration(currentSpeed)} x{currentWidth}";
            if (currentSpeed == maxSpeed && currentWidth == maxWidth)
                return current;

            return $"{current} (maks. PCIe {SpeedToGeneration(maxSpeed)} x{maxWidth})";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>DEVPKEY_PciDevice_*LinkSpeed değeri PCIe kuşağını (1-5) doğrudan temsil eder.</summary>
    private static string SpeedToGeneration(int speed) => speed switch
    {
        1 => "1.0",
        2 => "2.0",
        3 => "3.0",
        4 => "4.0",
        5 => "5.0",
        _ => "?"
    };
}
