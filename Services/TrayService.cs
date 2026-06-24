using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace StoraDesktop.Services;

/// <summary>
/// Windows system tray icon with context menu.
/// Uses Win32 P/Invoke for Shell_NotifyIconW.
/// </summary>
public class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _hWnd;
    private bool _disposed;
    private const uint WM_TRAY_CALLBACK = 0x8000;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_DESTROY = 0x0002;
    private const int ID_TRAY = 100;
    private const int CMD_SHOW = 1001;
    private const int CMD_LOGOUT = 1002;
    private const int CMD_EXIT = 1003;

    // Callbacks
    public Action? ShowWindowRequested;
    public Action? LogoutRequested;
    public Action? ExitRequested;

    public TrayService(Window window)
    {
        _window = window;

        // Create a message-only window to receive tray notifications
        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(new WndProcDelegate(WndProc)),
            hInstance = hInstance,
            lpszClassName = "StoraTrayWindow"
        };

        RegisterClassEx(ref wc);
        _hWnd = CreateWindowEx(0, "StoraTrayWindow", "", 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Initialize()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = ID_TRAY,
            uFlags = 0x00000002 | 0x00000004, // NIF_ICON | NIF_TIP
            hIcon = GetAppIcon(),
            uCallbackMessage = WM_TRAY_CALLBACK,
        };
        nid.szTip = "Stora Desktop";

        Shell_NotifyIcon(0x00000000, ref nid); // NIM_ADD
    }

    private IntPtr GetAppIcon()
    {
        // Try to load an icon from app assets
        // Load icon from .ico file or use system default
        var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stora.ico");
        if (File.Exists(icoPath))
        {
            return LoadImage(IntPtr.Zero, icoPath, 1, 16, 16, 0x00000010);
        }
        return LoadIcon(IntPtr.Zero, 32512); // IDI_APPLICATION
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            var l = lParam.ToInt32();
            if (l == 0x0203) // WM_LBUTTONDBLCLK
            {
                ShowWindowRequested?.Invoke();
            }
            else if (l == 0x0205) // WM_RBUTTONUP
            {
                ShowContextMenu();
            }
        }
        else if (msg == WM_COMMAND)
        {
            var cmd = wParam.ToInt32();
            switch (cmd)
            {
                case CMD_SHOW: ShowWindowRequested?.Invoke(); break;
                case CMD_LOGOUT: LogoutRequested?.Invoke(); break;
                case CMD_EXIT: ExitRequested?.Invoke(); break;
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        AppendMenu(hMenu, 0, CMD_SHOW, "Show Stora");
        AppendMenu(hMenu, 0x0800, 0, null); // MF_SEPARATOR
        AppendMenu(hMenu, 0, CMD_LOGOUT, "Logout");
        AppendMenu(hMenu, 0, CMD_EXIT, "Exit");

        // Show the menu at the cursor position
        GetCursorPos(out var pt);
        SetForegroundWindow(_hWnd);
        TrackPopupMenu(hMenu, 0x00000100, pt.x, pt.y, 0, _hWnd, IntPtr.Zero);
        DestroyMenu(hMenu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hWnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = ID_TRAY
            };
            Shell_NotifyIcon(0x00000002, ref nid); // NIM_DELETE
            PostMessage(_hWnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        }
    }

    #region P/Invoke

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion
}
