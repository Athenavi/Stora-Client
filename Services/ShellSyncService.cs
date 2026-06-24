using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Provider;

namespace StoraDesktop.Services;

/// <summary>
/// Registers Stora sync root with Windows Cloud Files API
/// for OneDrive-style overlay icons in File Explorer.
/// </summary>
public static class ShellSyncService
{
    private static bool _registered;

    public static void RegisterSyncRoot(string localPath, string displayName = "Stora Sync")
    {
        if (_registered || string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath))
            return;

        try
        {
            var folder = StorageFolder.GetFolderFromPathAsync(localPath).GetAwaiter().GetResult();

            var syncRoot = new StorageProviderSyncRootInfo
            {
                Id = "StoraDesktopSync",
                DisplayNameResource = displayName,
                IconResource = GetIconPath(),
                Path = folder,
                Version = "1.0",
                HydrationPolicy = StorageProviderHydrationPolicy.Full,
                HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.None,
                PopulationPolicy = StorageProviderPopulationPolicy.Full,
                InSyncPolicy = StorageProviderInSyncPolicy.FileLastWriteTime
                    | StorageProviderInSyncPolicy.FileCreationTime
                    | StorageProviderInSyncPolicy.DirectoryLastWriteTime
                    | StorageProviderInSyncPolicy.DirectoryCreationTime,
            };

            StorageProviderSyncRootManager.Register(syncRoot);
            _registered = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync root registration: {ex.Message}");
        }
    }

    public static void UnregisterSyncRoot(string localPath)
    {
        try
        {
            StorageProviderSyncRootManager.Unregister("StoraDesktopSync");
        }
        catch { }
        _registered = false;
    }

    private static string GetIconPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var iconPath = Path.Combine(appDir, "Assets", "StoreLogo.png");
        if (File.Exists(iconPath)) return iconPath;
        return "";
    }
}
