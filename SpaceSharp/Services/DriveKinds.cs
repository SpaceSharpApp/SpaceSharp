using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>What kind of disk a volume lives on, for the drive icon and label.</summary>
public enum DriveKind
{
    /// <summary>Not determined: no Windows, an unreadable device, or a query the storage stack refused.</summary>
    Unknown,
    /// <summary>A spinning disk (reports a seek penalty).</summary>
    Hdd,
    /// <summary>Flash storage on a bus other than NVMe.</summary>
    Ssd,
    /// <summary>Flash storage on the NVMe bus.</summary>
    Nvme,
    /// <summary>On the USB or SD bus, or a removable drive of any kind.</summary>
    Usb,        // USB bus or removable
    /// <summary>A mapped network drive or a UNC share.</summary>
    Network,
    /// <summary>A CD, DVD or Blu-ray drive.</summary>
    Optical
}

/// <summary>Detects what kind of disk a volume lives on.</summary>
/// <remarks>Uses IOCTL_STORAGE_QUERY_PROPERTY (seek penalty and bus type); works unelevated. Failures return Unknown.</remarks>
public static class DriveKinds
{
    /// <summary>Kinds already queried, by volume root; the storage stack is asked once per root per process.</summary>
    private static readonly Dictionary<string, DriveKind> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kind of a drive, from its DriveType first and the storage stack only for fixed drives.</summary>
    /// <param name="drive">A ready or not ready drive; only DriveType and RootDirectory are read.</param>
    /// <returns>Network, Optical or Usb straight from the type; the queried kind for a fixed drive; Unknown for RAM disks and the like.</returns>
    public static DriveKind Detect(DriveInfo drive)
    {
        switch (drive.DriveType)
        {
            case DriveType.Network:
                return DriveKind.Network;
            case DriveType.CDRom:
                return DriveKind.Optical;
            case DriveType.Removable:
                return DriveKind.Usb;
            case DriveType.Fixed:
                return Detect(drive.RootDirectory.FullName);
            default:
                return DriveKind.Unknown;
        }
    }

    /// <summary>The kind of the volume a path is on.</summary>
    /// <param name="path">Any path, relative or full; a UNC path is a network share.</param>
    /// <returns>The cached or freshly queried kind; Unknown off Windows or when the path has no root.</returns>
    public static DriveKind Detect(string path)
    {
        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // A path the runtime cannot resolve has no drive to name.
            return DriveKind.Unknown;
        }

        // A UNC root is a network share.
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return root is null ? DriveKind.Unknown : DriveKind.Network;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(root, out DriveKind cached))
            {
                return cached;
            }

            DriveKind kind = OperatingSystem.IsWindows() ? Query(root) : DriveKind.Unknown;
            Cache[root] = kind;
            return kind;
        }
    }

    /// <summary>The icon name for a kind.</summary>
    /// <param name="kind">Any kind.</param>
    /// <returns>A key in Icons.axaml; Unknown shares the plain "Drive" icon with Hdd.</returns>
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
    /// <param name="kind">Any kind.</param>
    /// <returns>The "Drive_*" resource for the kind; null for Unknown so the caller shows nothing rather than a guess.</returns>
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

    /// <summary>IOCTL_STORAGE_QUERY_PROPERTY: asks the storage stack for a device descriptor.</summary>
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    /// <summary>STORAGE_PROPERTY_ID values: the device descriptor (bus type) and the seek penalty descriptor.</summary>
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    /// <summary>STORAGE_BUS_TYPE values that decide between Usb, Ssd and Nvme.</summary>
    private const int BusTypeUsb = 7, BusTypeSd = 12, BusTypeNvme = 17;

    /// <summary>Asks the volume's device for its bus type and seek penalty.</summary>
    /// <param name="root">The volume root, such as "C:\"; opened as "\\.\C:" with zero access.</param>
    /// <returns>Usb for the USB and SD buses, Hdd when seeks cost, Nvme or Ssd otherwise; Unknown when the volume or the seek query fails.</returns>
    [SupportedOSPlatform("windows")]
    private static DriveKind Query(string root)
    {
        // zero access is enough for property queries
        using SafeFileHandle handle = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return DriveKind.Unknown;
        }

        int? busType = null;
        byte[] deviceDescriptor = new byte[1024];
        if (Query(handle, StorageDeviceProperty, deviceDescriptor))
        {
            busType = BitConverter.ToInt32(deviceDescriptor, 28); // STORAGE_DEVICE_DESCRIPTOR.BusType
        }
        if (busType is BusTypeUsb or BusTypeSd)
        {
            return DriveKind.Usb;
        }

        // Spinning disks report a seek penalty; flash does not.
        byte[] seekPenaltyDescriptor = new byte[12]; // DEVICE_SEEK_PENALTY_DESCRIPTOR
        if (!Query(handle, StorageDeviceSeekPenaltyProperty, seekPenaltyDescriptor))
        {
            return DriveKind.Unknown;
        }
        if (seekPenaltyDescriptor[8] != 0)
        {
            return DriveKind.Hdd;
        }

        return busType == BusTypeNvme ? DriveKind.Nvme : DriveKind.Ssd;
    }

    /// <summary>One IOCTL_STORAGE_QUERY_PROPERTY call for a standard query of the given property.</summary>
    /// <param name="handle">The open volume handle.</param>
    /// <param name="property">The STORAGE_PROPERTY_ID to read.</param>
    /// <param name="output">Receives the descriptor; must be large enough for the property's structure.</param>
    /// <returns>True when the device answered; the bytes in <paramref name="output"/> are then valid.</returns>
    [SupportedOSPlatform("windows")]
    private static bool Query(SafeFileHandle handle, int property, byte[] output)
    {
        byte[] query = new byte[12]; // STORAGE_PROPERTY_QUERY
        BitConverter.GetBytes(property).CopyTo(query, 0);
        return DeviceIoControl(handle, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out _, IntPtr.Zero);
    }

    /// <summary>Win32 CreateFileW, used here to open a volume device for property queries.</summary>
    /// <param name="fileName">The device path, "\\.\C:".</param>
    /// <param name="access">The access mask; 0 suffices for IOCTL property queries and needs no administrator rights.</param>
    /// <param name="share">FILE_SHARE_READ | FILE_SHARE_WRITE (3), so the mounted volume is not disturbed.</param>
    /// <param name="security">Unused; IntPtr.Zero.</param>
    /// <param name="disposition">OPEN_EXISTING (3).</param>
    /// <param name="flags">0.</param>
    /// <param name="template">Unused; IntPtr.Zero.</param>
    /// <returns>The handle, invalid on failure.</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    /// <summary>Win32 DeviceIoControl: sends a control code with an input buffer and reads the output buffer.</summary>
    /// <param name="device">The open device handle.</param>
    /// <param name="code">The IOCTL code.</param>
    /// <param name="input">The request structure.</param>
    /// <param name="inputSize">The bytes of <paramref name="input"/> to send.</param>
    /// <param name="output">Receives the reply.</param>
    /// <param name="outputSize">The capacity of <paramref name="output"/> in bytes.</param>
    /// <param name="returned">Receives the bytes written to <paramref name="output"/>.</param>
    /// <param name="overlapped">Unused; IntPtr.Zero for a synchronous call.</param>
    /// <returns>True when the device completed the request.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
