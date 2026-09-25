using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AutoBrightness.App.Hardware;

public sealed record DisplayEntry(string Id, string Name, string Connection, IDisplayBrightness? Device,
    int? InitialBrightness, string Detail)
{
    public bool Supported => Device is not null;
}

public sealed class DisplayInventory : IDisposable
{
    public List<DisplayEntry> Displays { get; } = [];
    public void Dispose() { foreach (var item in Displays) item.Device?.Dispose(); }

    public static DisplayInventory Discover()
    {
        var inventory = new DisplayInventory();
        var internalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID WHERE Active = TRUE");
            using var monitors = searcher.Get();
            foreach (ManagementObject monitor in monitors)
                using (monitor)
                {
                    var characters = monitor["UserFriendlyName"] as ushort[];
                    var name = characters is null ? "" : new string(characters.Where(c => c != 0).Select(c => (char)c).ToArray());
                    if (!string.IsNullOrWhiteSpace(name)) names[CanonicalId((string)monitor["InstanceName"])] = name;
                }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var values = searcher.Get();
            foreach (ManagementObject item in values)
            {
                using (item)
                {
                    var instance = (string)item["InstanceName"];
                    var device = new WmiDisplay(instance, item["Level"] as byte[] ?? []);
                    try
                    {
                        var current = device.Read();
                        inventory.Displays.Add(new(device.Id, device.Name, device.Connection, device, current, "Ready"));
                        internalIds.Add(CanonicalId(instance));
                    }
                    catch (Exception ex)
                    {
                        device.Dispose();
                        inventory.Displays.Add(new(device.Id, device.Name, device.Connection, null, null, ex.Message));
                    }
                }
            }
        }
        catch (ManagementException) { /* Desktop PCs commonly have no WMI brightness provider. */ }
        catch (UnauthorizedAccessException) { }

        Native.MonitorEnum callback = (IntPtr monitor, IntPtr _, ref Native.Rect __, IntPtr ___) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            var display = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>() };
            var hasIdentity = Native.EnumDisplayDevices(info.Device, 0, ref display, 1);
            var identity = hasIdentity ? CanonicalId(display.DeviceId ?? info.Device) : info.Device;
            if (internalIds.Contains(identity)) return true;

            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0 || count > 32)
            {
                inventory.Displays.Add(new("ddc:" + identity, display.DeviceString ?? info.Device, "DDC/CI", null, null,
                    "No physical monitor interface. Check the cable, dock and display driver."));
                return true;
            }
            var physical = new Native.PhysicalMonitor[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
            for (var index = 0; index < physical.Length; index++)
            {
                var handle = new PhysicalMonitorHandle(physical[index].Handle);
                var id = $"ddc:{identity}:{index}";
                var name = string.IsNullOrWhiteSpace(physical[index].Description) ? display.DeviceString ?? info.Device : physical[index].Description;
                name = names.GetValueOrDefault(identity) ?? name;
                try
                {
                    var device = new DdcDisplay(id, name, handle);
                    inventory.Displays.Add(new(id, name, device.Connection, device, device.Read(), "Ready"));
                }
                catch (Exception ex)
                {
                    handle.Dispose();
                    inventory.Displays.Add(new(id, name, "DDC/CI", null, null,
                        $"Brightness unavailable. Enable DDC/CI in the monitor menu; the cable/dock must pass it. {ex.Message}"));
                }
            }
            return true;
        };
        if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            inventory.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Display enumeration failed.");
        }
        return inventory;
    }

    // WMI: DISPLAY\MODEL\INSTANCE_0; Win32 interface: \\?\DISPLAY#MODEL#INSTANCE#{GUID}.
    internal static string CanonicalId(string id)
    {
        var result = id.Replace(@"\\?\", "").Replace('#', '\\');
        var parts = result.Split('\\');
        if (parts.Length >= 3) result = string.Join('\\', parts.Take(3));
        if (result.EndsWith("_0", StringComparison.Ordinal)) result = result[..^2];
        return result.ToUpperInvariant();
    }
}

internal sealed class WmiDisplay(string instance, byte[] levels) : IDisplayBrightness
{
    public string Id => "wmi:" + instance;
    public string Name => "Laptop / built-in display";
    public string Connection => "WMI";
    public int Read()
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
        using var matches = searcher.Get();
        foreach (ManagementObject item in matches)
        {
            using (item) if ((string)item["InstanceName"] == instance) return Convert.ToInt32(item["CurrentBrightness"]);
        }
        throw new InvalidOperationException("Display disconnected.");
    }
    public int Write(int percent)
    {
        var requested = Math.Clamp(percent, 0, 100);
        if (levels.Length > 0) requested = levels.OrderBy(x => Math.Abs(x - requested)).First();
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
        using var methods = searcher.Get();
        foreach (ManagementObject item in methods)
        {
            using (item)
            {
                if ((string)item["InstanceName"] != instance) continue;
                using var input = item.GetMethodParameters("WmiSetBrightness");
                input["Timeout"] = (uint)0;
                input["Brightness"] = (byte)requested;
                using var output = item.InvokeMethod("WmiSetBrightness", input, null);
                if (output?["ReturnValue"] is object status && Convert.ToUInt32(status) != 0)
                    throw new InvalidOperationException($"WMI returned {status}.");
                return requested;
            }
        }
        throw new InvalidOperationException("Display disconnected.");
    }
    public void Dispose() { }
}

internal sealed class DdcDisplay : IDisplayBrightness
{
    private readonly PhysicalMonitorHandle _handle;
    private readonly bool _useVcp;
    private readonly uint _minimum;
    private readonly uint _maximum;
    private uint? _lastWrite;
    public string Id { get; }
    public string Name { get; }
    public string Connection => _useVcp ? "DDC/CI · VCP 0x10" : "DDC/CI";

    public DdcDisplay(string id, string name, PhysicalMonitorHandle handle)
    {
        Id = id; Name = name; _handle = handle;
        if (Native.GetMonitorBrightness(handle, out var min, out _, out var max) && max > min)
        { _minimum = min; _maximum = max; }
        else if (Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x10, out _, out _, out max) && max > 0)
        { _minimum = 0; _maximum = max; _useVcp = true; }
        else throw new Win32Exception(Marshal.GetLastWin32Error(), "Monitor did not expose a brightness range.");
    }

    public int Read()
    {
        uint current;
        var ok = _useVcp
            ? Native.GetVCPFeatureAndVCPFeatureReply(_handle, 0x10, out _, out current, out _)
            : Native.GetMonitorBrightness(_handle, out _, out current, out _);
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read monitor brightness.");
        _lastWrite = current;
        return ToPercent(current);
    }

    public int Write(int percent)
    {
        var raw = _minimum + (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * (_maximum - _minimum));
        if (_lastWrite != raw)
        {
            var ok = _useVcp ? Native.SetVCPFeature(_handle, 0x10, raw) : Native.SetMonitorBrightness(_handle, raw);
            if (!ok)
            {
                _lastWrite = null; // A failed driver call may still have reached the hardware.
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Monitor rejected the brightness change.");
            }
            _lastWrite = raw;
        }
        return ToPercent(raw);
    }
    private int ToPercent(uint raw) => (int)Math.Round(Math.Clamp(((double)raw - _minimum) / (_maximum - _minimum), 0, 1) * 100);
    public void Dispose() => _handle.Dispose();
}

internal sealed class PhysicalMonitorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public PhysicalMonitorHandle(IntPtr handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => Native.DestroyPhysicalMonitor(handle);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    internal delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorBrightness(PhysicalMonitorHandle handle, out uint minimum, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorBrightness(PhysicalMonitorHandle handle, uint brightness);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVCPFeatureAndVCPFeatureReply(PhysicalMonitorHandle handle, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetVCPFeature(PhysicalMonitorHandle handle, byte code, uint value);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
