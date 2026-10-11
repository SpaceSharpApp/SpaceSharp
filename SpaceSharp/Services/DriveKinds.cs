using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

public enum DriveKind
{
    Unknown,
    Hdd,        // spinning disk
    Ssd,        // SATA or other solid state
    Nvme,       // solid state on the NVMe bus
    Usb,        // a fixed disk on the USB bus (external drive) or a removable stick
    Network,
    Optical
}

/// <summary>
/// Tells what kind of disk a volume lives on, for the icon and the line under the drive's name. Two questions to
/// the storage stack through DeviceIoControl, which Windows answers for a volume opened with no access rights, so
/// no elevation is needed: does the disk incur a seek penalty (spinning) and which bus is it on (NVMe, USB, ...).
/// Anything that fails answers Unknown, and the caller shows the file system name alone as before.
/// </summary>
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

    /// <summary>For a path: the kind of the volume it is on. Only meaningful for local fixed disks.</summary>
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

    /// <summary>The icon name in Themes/Icons.xaml for a kind.</summary>
    public static string IconName(DriveKind kind) => kind switch
    {
        DriveKind.Hdd => "Drive",
        DriveKind.Ssd or DriveKind.Nvme => "Ssd",
        DriveKind.Usb => "UsbStick",
        DriveKind.Network => "Network",
        DriveKind.Optical => "Disc",
        _ => "Drive"
    };

    /// <summary>What to call it, or null when nothing is known beyond the file system.</summary>
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

    // ---- the storage stack

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int BusTypeUsb = 7, BusTypeSd = 12, BusTypeNvme = 17;

    private static DriveKind Query(string root)
    {
        // "\\.\C:" opens the volume; zero access is enough for property queries and needs no elevation.
        using var handle = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return DriveKind.Unknown;

        int? bus = null;
        var device = new byte[1024];
        if (Query(handle, StorageDeviceProperty, device)) bus = BitConverter.ToInt32(device, 28); // STORAGE_DEVICE_DESCRIPTOR.BusType
        if (bus is BusTypeUsb or BusTypeSd) return DriveKind.Usb;

        var seek = new byte[12]; // DEVICE_SEEK_PENALTY_DESCRIPTOR: Version, Size, IncursSeekPenalty
        if (!Query(handle, StorageDeviceSeekPenaltyProperty, seek)) return DriveKind.Unknown;
        if (seek[8] != 0) return DriveKind.Hdd;
        return bus == BusTypeNvme ? DriveKind.Nvme : DriveKind.Ssd;
    }

    private static bool Query(SafeFileHandle handle, int property, byte[] output)
    {
        var query = new byte[12]; // STORAGE_PROPERTY_QUERY: PropertyId, QueryType (0 = standard), AdditionalParameters[1]
        BitConverter.GetBytes(property).CopyTo(query, 0);
        return DeviceIoControl(handle, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out _, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
