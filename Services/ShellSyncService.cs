using System;
using System.IO;
using Windows.Storage;
using Windows.Storage.Provider;

namespace StoraDesktop.Services;

/// <summary>
/// Registers Stora sync root with Windows Cloud Files API,
/// enabling OneDrive-style overlay icons in File Explorer.
/// 
/// Requirements:
///   1. Package.appxmanifest must declare storageProviderSync capability
///   2. App must be running with full trust (runFullTrust)
///   3. Icon resource must be a .ico file (not .png)
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
            var iconPath = GetIconPath();
            var version = Windows.ApplicationModel.Package.Current.Id.Version;

            var syncRoot = new StorageProviderSyncRootInfo
            {
                Id = "StoraDesktopSync_v1",
                DisplayNameResource = displayName,
                IconResource = iconPath,
                Path = folder,
                Version = $"{version.Major}.{version.Minor}",
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
            System.Diagnostics.Debug.WriteLine($"[ShellSyncService] Register failed: {ex.Message}");
        }
    }

    public static void UnregisterSyncRoot()
    {
        try
        {
            StorageProviderSyncRootManager.Unregister("StoraDesktopSync_v1");
        }
        catch { }
        _registered = false;
    }

    private static string GetIconPath()
    {
        // Cloud Files API requires .ico format with resource index
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // Check for .ico in app directory
        var icoPath = Path.Combine(appDir, "stora.ico");
        if (File.Exists(icoPath))
            return icoPath + ",-101";

        // Fallback: use StoreLogo.png (may not show as overlay but won't crash)
        var pngPath = Path.Combine(appDir, "Assets", "StoreLogo.png");
        if (File.Exists(pngPath))
            return pngPath;

        // Try the app local state
        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Stora", "stora.ico");
        if (File.Exists(statePath))
            return statePath + ",-101";

        return "";
    }
}
