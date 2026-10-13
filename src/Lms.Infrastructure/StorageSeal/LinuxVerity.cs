using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lms.Infrastructure.StorageSeal;

// Test seam is internal; a test double is not a qualified filesystem.
internal interface IOriginalSealKernel
{
    void Enable(SafeFileHandle file);
    string Measure(SafeFileHandle file);
    void Sync(SafeFileHandle file);
    void SyncDirectory(string directory);
}

internal sealed class LinuxVerity : IOriginalSealKernel
{
    private static void Supported()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Seal profile requires Linux x64");
    }
    public void Enable(SafeFileHandle file)
    {
        Supported();
        var arg = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(arg, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(arg.AsSpan(4), 1); // SHA-256
        BinaryPrimitives.WriteUInt32LittleEndian(arg.AsSpan(8), 4096);
        Check(ioctl(file, 0x40806685, arg), "FS_IOC_ENABLE_VERITY");
    }
    public string Measure(SafeFileHandle file)
    {
        Supported();
        var arg = new byte[36];
        BinaryPrimitives.WriteUInt16LittleEndian(arg.AsSpan(2), 32);
        Check(ioctl(file, 0xC0046686, arg), "FS_IOC_MEASURE_VERITY");
        if (BinaryPrimitives.ReadUInt16LittleEndian(arg) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(arg.AsSpan(2)) != 32)
            throw new IOException("Unexpected verity digest");
        return Convert.ToHexString(arg.AsSpan(4));
    }
    public void Sync(SafeFileHandle file) { Supported(); Check(fsync(file), "fsync"); }
    public void SyncDirectory(string directory)
    {
        Supported();
        var fd = open(directory, 0x10000 | 0x20000 | 0x80000); // O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC
        if (fd < 0) Check(-1, "open directory");
        using var handle = new SafeFileHandle((IntPtr)fd, true);
        Sync(handle);
    }
    private static void Check(int result, string operation)
    {
        if (result < 0) throw new IOException($"{operation} failed: errno={Marshal.GetLastPInvokeError()}");
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(SafeFileHandle fd, nuint request, [In, Out] byte[] argument);
    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(SafeFileHandle fd);
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);
}
