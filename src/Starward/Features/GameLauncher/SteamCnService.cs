using Microsoft.Win32;
using Starward.Core;
using Starward.Core.Steam;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Starward.Features.GameLauncher;

internal static class SteamCnService
{
    public static bool Enabled
    {
        get => AppConfig.GetValue<bool>(false, "steam_cn_4162040_enabled");
        set => AppConfig.SetValue(value, "steam_cn_4162040_enabled");
    }

    public static string SteamExecutable => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string
        ?? throw new InvalidOperationException("Steam is not installed for this Windows user.");

    public static string[] Libraries()
    {
        string root = Path.GetDirectoryName(SteamExecutable)!;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(root) };
        string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file))
            foreach (var entry in SteamVdf.Parse(File.ReadAllText(file)).Node("libraryfolders").Values.OfType<SteamVdf>())
                if (entry.TryGetValue("path", out var value) && value is string path && Path.IsPathFullyQualified(path)
                    && Directory.Exists(Path.Combine(path, "steamapps"))) libraries.Add(Path.GetFullPath(path));
        return libraries.ToArray();
    }

    public static bool IsBusy()
    {
        foreach (string name in new[] { "steam", "ZenlessZoneZero", "Starward.Steam" })
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) return true;
        }
        return false;
    }

    public static SteamInstallManager Manager() => new(SteamBridge.StateDirectory, Libraries(), IsBusy);

    public static void SaveLaunch(string nativeExecutable)
    {
        if (AppConfig.GetEnableThirdPartyTool(GameBiz.nap_cn) || AppConfig.StartGameWithCMD || AppConfig.EnableLoginAuthTicket is true)
            throw new InvalidOperationException("Disable the custom executable, CMD launch and login-auth-ticket options before using Steam CN launch.");
        string arguments = AppConfig.GetStartArgument(GameBiz.nap_cn)?.Trim() ?? "";
        if (AppConfig.GetUsePopupWindow(GameBiz.nap_cn)) arguments += " -popupwindow";
        if (AppConfig.GetEnableDX12(GameBiz.nap_cn)) arguments += " -use-d3d12";
        var config = new SteamBridgeConfig(1, Path.GetFullPath(nativeExecutable), arguments);
        SteamBridge.ValidateConfig(config);
        SteamBridge.GuardNoLinks(SteamBridge.StateDirectory);
        Directory.CreateDirectory(SteamBridge.StateDirectory);
        string temporary = SteamBridge.ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        SteamBridge.GuardNoLinks(SteamBridge.ConfigPath);
        SteamBridge.GuardNoLinks(temporary);
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(config)); File.Move(temporary, SteamBridge.ConfigPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void DeployHelper()
    {
        if (IsBusy()) throw new InvalidOperationException("Exit Steam, ZZZ and the Steam helper before applying setup.");
        string source = Path.Combine(AppContext.BaseDirectory, "steam-helper", "Starward.Steam.exe");
        if (!File.Exists(source)) throw new FileNotFoundException("The Steam helper is missing from this Starward build.", source);
        SteamBridge.GuardNoLinks(SteamBridge.HelperPath);
        SteamBridge.GuardNoLinks(SteamBridge.HelperPath + ".bak");
        SteamBridge.GuardNoLinks(SteamBridge.HelperPath + ".new");
        Directory.CreateDirectory(Path.GetDirectoryName(SteamBridge.HelperPath)!);
        if (File.Exists(SteamBridge.HelperPath)) File.Copy(SteamBridge.HelperPath, SteamBridge.HelperPath + ".bak", true);
        File.Copy(source, SteamBridge.HelperPath + ".new", true);
        File.Move(SteamBridge.HelperPath + ".new", SteamBridge.HelperPath, true);
    }

    public static void Launch(string nativeExecutable)
    {
        Manager().ValidateInstalled();
        if (!File.Exists(SteamBridge.HelperPath)) throw new FileNotFoundException("Apply the Steam setup again to restore the helper.");
        SaveLaunch(nativeExecutable);
        // Steam's per-game launch options remain under the user's control; no account VDF is rewritten.
        Process.Start(new ProcessStartInfo(SteamExecutable)
        {
            Arguments = "-applaunch " + SteamMetadata.AppId,
            UseShellExecute = true,
        })?.Dispose();
    }
}
