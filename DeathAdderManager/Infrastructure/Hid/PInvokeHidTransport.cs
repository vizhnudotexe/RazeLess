using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;
using DeathAdderManager.Core.Interfaces;
using HidSharp;

namespace DeathAdderManager.Infrastructure.Hid;

public sealed class PInvokeHidTransport : IHidTransport
{
    private readonly SafeFileHandle _handle;
    private readonly ILogger _logger;
    private readonly string _devicePath;
    private readonly CancellationTokenSource _cts = new();

    public bool IsOpen => !_handle.IsInvalid && !_handle.IsClosed;

    public PInvokeHidTransport(SafeFileHandle handle, string devicePath, ILogger logger)
    {
        _handle = handle;
        _devicePath = devicePath;
        _logger = logger;
    }

    public static PInvokeHidTransport? TryOpen(HidDevice device, ILogger logger)
    {
        // Must use Zero access (0) to bypass Windows filtering on generic mice
        var handle = NativeMethods.CreateFile(
            device.DevicePath,
            0, // No generic read/write access
            3, // FILE_SHARE_READ | FILE_SHARE_WRITE
            IntPtr.Zero,
            3, // OPEN_EXISTING
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            logger.LogWarning("PInvoke CreateFile failed for {Path} with error {Error}", device.DevicePath, Marshal.GetLastWin32Error());
            return null;
        }

        logger.LogInformation("Opened PInvoke zero-access handle for {Path}", device.DevicePath);
        return new PInvokeHidTransport(handle, device.DevicePath, logger);
    }

    public Task<bool> SendFeatureReportAsync(byte[] packet, CancellationToken ct = default)
    {
        if (!IsOpen) return Task.FromResult(false);

        bool success = NativeMethods.HidD_SetFeature(_handle, packet, packet.Length);
        if (!success)
        {
            _logger.LogError("PInvoke HidD_SetFeature failed with error {Error}", Marshal.GetLastWin32Error());
        }
        return Task.FromResult(success);
    }

    public Task<byte[]?> ReadFeatureReportAsync(CancellationToken ct = default)
    {
        if (!IsOpen) return Task.FromResult<byte[]?>(null);

        byte[] buffer = new byte[91];
        buffer[0] = 0x00; // Expected Report ID

        bool success = NativeMethods.HidD_GetFeature(_handle, buffer, buffer.Length);
        if (!success)
        {
            _logger.LogError("PInvoke HidD_GetFeature failed with error {Error}", Marshal.GetLastWin32Error());
            return Task.FromResult<byte[]?>(null);
        }

        // Buffer already has Report ID at [0], which matches RazerHidPacket layout perfectly.
        return Task.FromResult<byte[]?>(buffer);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _handle.Close();
        _handle.Dispose();
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);
}
