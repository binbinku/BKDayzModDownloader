using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BKDayzModDownloader;

internal static class RoundedWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rectangle);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || window.WindowState == WindowState.Minimized) return;
        // Windows 11: compositor corners preserve the native resize frame and shadows.
        int preference = window.WindowState == WindowState.Maximized ? 1 : 2;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int)) == 0)
            return;
        // Server 2019: an HWND region clips the actual outer window, without a
        // transparent layered WPF window or an additional UI/runtime dependency.
        if (window.WindowState == WindowState.Maximized) { SetWindowRgn(hwnd, IntPtr.Zero, true); return; }
        if (!GetWindowRect(hwnd, out var rect)) return;
        int diameter = (int)Math.Round(20 * VisualTreeHelper.GetDpi(window).DpiScaleX);
        var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
        // On success Windows owns the region handle.
    }
}
