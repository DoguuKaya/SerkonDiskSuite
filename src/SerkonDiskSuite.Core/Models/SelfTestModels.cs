namespace SerkonDiskSuite.Core.Models;

/// <summary>SMART self-test türü (smartctl "-t short" / "-t long").</summary>
public enum SelfTestType
{
    Short,
    Long
}

/// <summary>Bir SMART self-test'in güncel (çalışıyorsa) veya son biten durumu.</summary>
public sealed record SelfTestStatus(
    bool IsRunning,
    int? PercentRemaining,
    string? StatusDescription,
    bool? Passed);
