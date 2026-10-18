using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

public enum DriveKind
{
    Unknown,
    Hdd,
    Ssd,
    Nvme,
    Usb,        // USB bus or removable
    Network,
    Optical
}

/// <summary>Detects what kind of disk a volume lives on.</summary>
/// <remarks>Uses IOCTL_STORAGE_QUERY_PROPERTY (seek penalty and bus type); works unelevated. Failures return Unknown.</remarks>
public static class DriveKinds
{
    private static readonly Dictionary<string, DriveKind> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static DriveKind Detect(DriveInfo drive)
    {
        switch (drive.DriveType)
        {
            case DriveType.Network: return DriveKind.Network;
            case DriveType.CDRom: return DriveKind.Optical;
            case DriveType.Removable: return DriveKind.Usb;
            case DriveType.Fixed: return Detect(drive.RootDirectory.FullName);
            default: return DriveKind.Unknown;
        }
    }

    /// <summary>The kind of the volume a path is on.</summary>
    public static DriveKind Detect(string path)
    {
        string? root;
        try { root = Path.GetPathRoot(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return DriveKind.Unknown; }
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return root is null ? DriveKind.Unknown : DriveKind.Network;
        lock (Cache)
        {
            if (Cache.TryGetValue(root, out var cached)) return cached;
            var kind = OperatingSystem.IsWindows() ? Query(root) : DriveKind.Unknown;
            Cache[root] = kind;
            return kind;
        }
    }

    /// <summary>The icon name for a kind.</summary>
    public static string IconName(DriveKind kind) => kind switch
    {
        DriveKind.Hdd => "Drive",
        DriveKind.Ssd or DriveKind.Nvme => "Ssd",
        DriveKind.Usb => "UsbStick",
        DriveKind.Network => "Network",
        DriveKind.Optical => "Disc",
        _ => "Drive"
    };

    /// <summary>The localized label for a kind, or null for Unknown.</summary>
    public static string? Label(DriveKind kind) => kind switch
    {
        DriveKind.Hdd => Strings.Get("Drive_Hdd"),
        DriveKind.Ssd => Strings.Get("Drive_Ssd"),
        DriveKind.Nvme => Strings.Get("Drive_Nvme"),
        DriveKind.Usb => Strings.Get("Drive_Usb"),
        DriveKind.Network => Strings.Get("Drive_Network"),
        DriveKind.Optical => Strings.Get("Drive_Optical"),
        _ => null
    };

    // ---- storage stack

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int BusTypeUsb = 7, BusTypeSd = 12, BusTypeNvme = 17;

    [SupportedOSPlatform("windows")]
    private static DriveKind Query(string root)
    {
        // zero access is enough for property queries
        using var handle = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return DriveKind.Unknown;

        int? bus = null;
        var device = new byte[1024];
        if (Query(handle, StorageDeviceProperty, device)) bus = BitConverter.ToInt32(device, 28); // STORAGE_DEVICE_DESCRIPTOR.BusType
        if (bus is BusTypeUsb or BusTypeSd) return DriveKind.Usb;

        var seek = new byte[12]; // DEVICE_SEEK_PENALTY_DESCRIPTOR
        if (!Query(handle, StorageDeviceSeekPenaltyProperty, seek)) return DriveKind.Unknown;
        if (seek[8] != 0) return DriveKind.Hdd;
        return bus == BusTypeNvme ? DriveKind.Nvme : DriveKind.Ssd;
    }

    [SupportedOSPlatform("windows")]
    private static bool Query(SafeFileHandle handle, int property, byte[] output)
    {
        var query = new byte[12]; // STORAGE_PROPERTY_QUERY
        BitConverter.GetBytes(property).CopyTo(query, 0);
        return DeviceIoControl(handle, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out _, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
