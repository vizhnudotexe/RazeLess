using System.Runtime.InteropServices;
using System.Diagnostics;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Application;

public sealed class MouseHookService : IDisposable
{
    private readonly ILogger<MouseHookService> _logger;
    private readonly IMouseService _mouseService;
    private readonly object _lock = new();
    private IntPtr _hookId = IntPtr.Zero;
    private ButtonMappings _mappings = new();
    private bool _disposed;

    public event EventHandler<MouseButton>? ButtonRemapped;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const int MOUSEEVENTF_XDOWN = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    private readonly LowLevelMouseProc _proc;

    public MouseHookService(ILogger<MouseHookService> logger, IMouseService mouseService)
    {
        _logger = logger;
        _mouseService = mouseService;
        _proc = HookCallback;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_hookId != IntPtr.Zero) return;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule != null)
            {
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                _logger?.LogInformation("Mouse hook installed");
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _logger?.LogInformation("Mouse hook uninstalled");
            }
        }
    }

    public void UpdateMappings(ButtonMappings mappings)
    {
        _mappings = mappings;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _mouseService.ActiveProfile != null)
        {
            _mappings = _mouseService.ActiveProfile.Buttons;
            int msg = wParam.ToInt32();

            MouseButton? button = msg switch
            {
                WM_LBUTTONDOWN => MouseButton.LeftButton,
                WM_RBUTTONDOWN => MouseButton.RightButton,
                WM_MBUTTONDOWN => MouseButton.MiddleButton,
                WM_XBUTTONDOWN => (int)((uint)Marshal.ReadInt32(lParam, 8) >> 16) == 1 ? MouseButton.SideButton1 : MouseButton.SideButton2,
                WM_MOUSEWHEEL => (int)((short)((uint)Marshal.ReadInt32(lParam, 8) >> 16)) > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown,
                _ => null
            };

            if (button.HasValue && button != MouseButton.LeftButton)
            {
                var action = _mappings.GetAction(button.Value);
                if (action.Type != ButtonActionType.Default)
                {
                    ProcessRemappedAction(action, button.Value);
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessRemappedAction(ButtonAction action, MouseButton originalButton)
    {
        switch (action.Type)
        {
            case ButtonActionType.Disabled:
                _logger?.LogInformation("Button {Button} disabled", originalButton);
                break;

            case ButtonActionType.MouseButton:
                if (int.TryParse(action.Parameter, out int targetBtn))
                {
                    var target = (MouseButton)targetBtn;
                    SimulateMouseButton(target);
                    _logger?.LogInformation("Remapped {Original} to {Target}", originalButton, target);
                }
                break;

            case ButtonActionType.KeyboardKey:
                if (int.TryParse(action.Parameter, out int vk))
                {
                    SimulateKeyPress(vk);
                    _logger?.LogInformation("Remapped {Button} to key VK=0x{Vk:X2}", originalButton, vk);
                }
                break;

            case ButtonActionType.MediaKey:
            case ButtonActionType.BrowserKey:
                if (int.TryParse(action.Parameter, out int mediaVk))
                {
                    SimulateKeyPress(mediaVk);
                    _logger?.LogInformation("Remapped {Button} to media/browser key VK=0x{MediaVk:X2}", originalButton, mediaVk);
                }
                break;
        }
    }

    private void SimulateMouseButton(MouseButton button)
    {
        int flags = button switch
        {
            MouseButton.LeftButton => MOUSEEVENTF_LEFTDOWN,
            MouseButton.RightButton => MOUSEEVENTF_RIGHTDOWN,
            MouseButton.MiddleButton => MOUSEEVENTF_MIDDLEDOWN,
            _ => 0
        };
        if (flags != 0)
            mouse_event(flags, 0, 0, 0, 0);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    private const int KEYEVENTF_KEYDOWN = 0x0000;
    private const int KEYEVENTF_KEYUP = 0x0002;

    private void SimulateKeyPress(int vk)
    {
        keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, 0);
        keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
