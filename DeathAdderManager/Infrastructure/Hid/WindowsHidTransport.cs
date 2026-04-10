using System.Runtime.InteropServices;
using System.Text;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Native;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

public sealed class WindowsHidTransport : IHidTransport
{
    private readonly IntPtr _deviceHandle;
    private readonly ILogger _logger;
    private bool _disposed;
    private const int FeatureReportSize = 91;

    public bool IsOpen => !_disposed && _deviceHandle != IntPtr.Zero;

    public WindowsHidTransport(string devicePath, ILogger logger)
    {
        _logger = logger;
        
        _deviceHandle = CreateFile(devicePath, 
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (_deviceHandle == INVALID_HANDLE_VALUE)
        {
            var err = Marshal.GetLastWin32Error();
            throw new Exception($"Failed to open device: {err}");
        }
        
        _logger.LogInformation("Opened device via Windows API: {Path}", devicePath);
    }

    public Task<bool> SendFeatureReportAsync(byte[] packet, CancellationToken ct = default)
    {
        if (_disposed || _deviceHandle == IntPtr.Zero)
            return Task.FromResult(false);

        if (packet.Length != FeatureReportSize)
        {
            _logger.LogError("Invalid packet size {Size}", packet.Length);
            return Task.FromResult(false);
        }

        try
        {
            _logger.LogInformation(">>> WINAPI SEND: Class=0x{PClass:X2} Cmd=0x{PCmd:X2}", 
                packet[7], packet[8]);

            // Add 1 byte for report ID at position 0
            var buffer = new byte[FeatureReportSize + 1];
            buffer[0] = 0; // Report ID
            Array.Copy(packet, 0, buffer, 1, packet.Length);
            
            var result = HidApi.HidD_SetFeature(_deviceHandle, buffer, buffer.Length);
            
            if (result)
            {
                _logger.LogInformation("<<< WINAPI SEND OK");
                return Task.FromResult(true);
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                _logger.LogWarning("WINAPI SetFeature failed: {Error}", err);
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WINAPI SendFeatureReport failed");
            return Task.FromResult(false);
        }
    }

    public Task<byte[]?> ReadFeatureReportAsync(CancellationToken ct = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_deviceHandle != IntPtr.Zero)
        {
            CloseHandle(_deviceHandle);
        }
    }

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, 
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

public static class WindowsHidDeviceFactory
{
    public const int RazerVendorId = 0x1532;
    public const int DeathAdderEssentialPid = 0x0071;
    public const int DeathAdderEssential2021Pid = 0x0098;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(IntPtr handle, out HidApi.HIDD_ATTRIBUTES attributes);

    public static IEnumerable<string> EnumerateDeathAdderDevicePaths()
    {
        var guid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        
        var hDevSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevSet == INVALID_HANDLE)
            yield break;

        try
        {
            int i = 0;
            while (true)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(hDevSet, IntPtr.Zero, ref guid, i, ref interfaceData))
                    break;

                i++;

                int requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevSet, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                var detailData = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailData, 0, Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA>());
                    var pathPtr = detailData + Marshal.SizeOf<int>();
                    
                    if (SetupDiGetDeviceInterfaceDetail(hDevSet, ref interfaceData, detailData, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(pathPtr);
                        if (path != null && path.Contains("vid_1532") && 
                           (path.Contains("pid_0071") || path.Contains("pid_0098")))
                        {
                            yield return path;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(detailData); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(hDevSet); }
    }

    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public int cbSize;
        public char DevicePath;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, 
        ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, 
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, 
        int DeviceInterfaceDetailDataSize, ref int RequiredSize, IntPtr DeviceInfoData);
}
