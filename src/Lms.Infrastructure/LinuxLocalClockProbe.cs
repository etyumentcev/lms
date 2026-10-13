using System.Runtime.InteropServices;
using Lms.Application;

namespace Lms.Infrastructure;

// P1 Linux x64 local profile: REALTIME observed between MONOTONIC/BOOTTIME
// brackets, boot_id and current clocksource/time namespace checked before/after.
// These are local measurements, NOT an external UTC qualification.
public sealed class LinuxLocalClockProbe : ILocalClockProbe
{
    public const string Profile = "P1-LINUX-X64-LOCAL-CLOCK-01";
    public LocalClockObservation? Read()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return null;
        try
        {
            var identity = Identity();
            var monoBefore = ReadNanoseconds(1); // CLOCK_MONOTONIC
            var bootBefore = ReadNanoseconds(7); // CLOCK_BOOTTIME
            var wall = ReadNanoseconds(0);       // CLOCK_REALTIME
            var bootAfter = ReadNanoseconds(7);
            var monoAfter = ReadNanoseconds(1);
            if (identity != Identity() || monoAfter < monoBefore || bootAfter < bootBefore) return null;
            return new(identity.Source, identity.Boot,
                new(DateTimeOffset.UnixEpoch.AddTicks(Floor(wall)), DateTimeOffset.UnixEpoch.AddTicks(Ceiling(wall))),
                TimeSpan.FromTicks(Floor(monoBefore)), TimeSpan.FromTicks(Ceiling(monoAfter)),
                TimeSpan.FromTicks(Floor(bootBefore)), TimeSpan.FromTicks(Ceiling(bootAfter)));
        }
        catch { return null; }
    }
    private static (string Source, string Boot) Identity()
    {
        var boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        var clock = File.ReadAllText("/sys/devices/system/clocksource/clocksource0/current_clocksource").Trim();
        var ns = new FileInfo("/proc/self/ns/time").LinkTarget;
        if (!Guid.TryParse(boot, out _) || string.IsNullOrWhiteSpace(clock) ||
            clock.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(ns)) throw new IOException("Missing clock identity");
        return ($"{Profile}/{clock}/{ns}", boot);
    }
    private static Int128 ReadNanoseconds(int id)
    {
        if (clock_gettime(id, out var value) != 0 || value.Seconds < 0 || value.Nanoseconds is < 0 or >= 1_000_000_000)
            throw new IOException("Clock observation unavailable");
        return (Int128)value.Seconds * 1_000_000_000 + value.Nanoseconds;
    }
    private static long Floor(Int128 ns) => checked((long)(ns / 100));
    private static long Ceiling(Int128 ns) => checked((long)((ns + 99) / 100));
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds; public long Nanoseconds; }
    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clockId, out Timespec value);
}
