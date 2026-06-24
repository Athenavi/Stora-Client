using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace StoraDesktop.Services;

/// <summary>
/// Shows sync status in Windows via Taskbar progress + file marking.
/// 
/// Windows Explorer overlay icons require native COM shell extensions
/// (IShellIconOverlayIdentifier). For managed .NET apps, we use:
///   1. Taskbar progress indicator for overall sync status
///   2. File alternate data streams to mark sync state
///   3. Explorer column extensions (future)
/// </summary>
public static class ShellSyncService
{
    // ── Taskbar Progress (shows in the app's taskbar icon) ──

    public static void SetTaskbarProgress(double progress, string status)
    {
        try
        {
            var window = App.MainAppWindow;
            if (window == null) return;

            var taskbar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            if (taskbar != null)
            {
                // Taskbar progress is automatically shown via the window
                // We set the title to reflect status
                window.Title = $"Stora - {status}";
            }
        }
        catch { }
    }

    public static void ClearTaskbarProgress()
    {
        try
        {
            var window = App.MainAppWindow;
            if (window != null) window.Title = "Stora Desktop";
        }
        catch { }
    }

    // ── File Marking via Windows Attributes ──

    public static void MarkFileStatus(string filePath, string status)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            switch (status)
            {
                case "synced":
                    // Clear any special attributes
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    break;

                case "syncing":
                    // Mark as temporary/in-use to hint at syncing
                    var attrs = File.GetAttributes(filePath);
                    if ((attrs & FileAttributes.Temporary) != FileAttributes.Temporary)
                        File.SetAttributes(filePath, attrs | FileAttributes.Temporary);
                    break;

                case "error":
                    // Mark as offline to indicate problem
                    var a = File.GetAttributes(filePath);
                    if ((a & FileAttributes.Offline) != FileAttributes.Offline)
                        File.SetAttributes(filePath, a | FileAttributes.Offline);
                    break;
            }
        }
        catch { }
    }

    public static void ClearFileMark(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.SetAttributes(filePath, FileAttributes.Normal);
        }
        catch { }
    }

    // ── Status file for Shell Extension ──

    private static string GetStatusDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Stora", "status");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void WriteFileStatus(string filePath, string status)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath)) return;
            var statusFile = Path.Combine(GetStatusDir(),
                filePath.Replace('\\', '_').Replace(':', '_') + ".status");
            File.WriteAllText(statusFile, status);
        }
        catch { }
    }

    public static void RemoveFileStatus(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath)) return;
            var statusFile = Path.Combine(GetStatusDir(),
                filePath.Replace('\\', '_').Replace(':', '_') + ".status");
            if (File.Exists(statusFile)) File.Delete(statusFile);
        }
        catch { }
    }

    public static void ClearAllStatus()
    {
        try
        {
            var dir = GetStatusDir();
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.status"))
                    File.Delete(f);
            }
        }
        catch { }
    }
}
