using System.Diagnostics;
using System.Text.Json;

namespace Starward.Core.Steam;

public sealed record SteamBridgeConfig(int Version, string GameExecutable, string Arguments);

public static class SteamBridge
{
    public static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starward", "SteamCN", SteamMetadata.AppId);
    public static string ConfigPath => Path.Combine(StateDirectory, "launch.json");
    public static string HelperPath => Path.Combine(StateDirectory, "helper", "Starward.Steam.exe");
    public static string LaunchOptions => $"\"{HelperPath}\" --config \"{ConfigPath}\" -- %command%";

    public static SteamBridgeConfig ReadConfig(string path)
    {
        GuardNoLinks(path);
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Launch configuration is too large.");
        var config = JsonSerializer.Deserialize<SteamBridgeConfig>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing launch configuration.");
        ValidateConfig(config);
        return config;
    }

    public static void ValidateConfig(SteamBridgeConfig config)
    {
        if (config.Version != 1 || !Path.IsPathFullyQualified(config.GameExecutable)
            || !Path.GetFileName(config.GameExecutable).Equals("ZenlessZoneZero.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(config.GameExecutable) || config.Arguments is null || config.Arguments.Length > 16000
            || config.Arguments.Any(c => c is '\0' or '\r' or '\n')) throw new InvalidDataException("Invalid native ZZZ launch configuration.");
        GuardNoLinks(config.GameExecutable);
        // Steam tickets / SDK arguments from %command% must never be forwarded to the CN client.
        if (config.Arguments.Contains("login_auth_ticket", StringComparison.OrdinalIgnoreCase)
            || config.Arguments.Contains("%command%", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Authentication tickets cannot be saved in Steam launch configuration.");
    }

    public static int Run(string[] args)
    {
        if (args.Length < 4 || args[0] != "--config" || args[2] != "--") throw new ArgumentException("Expected --config <file> -- %command%.");
        if (Environment.GetEnvironmentVariable("SteamAppId") != SteamMetadata.AppId
            || Environment.GetEnvironmentVariable("SteamGameId") is string gameId && gameId != SteamMetadata.AppId)
            throw new InvalidOperationException("Start this helper from the official ZZZ Steam library entry.");
        var config = ReadConfig(args[1]);
        using var singleLaunch = new Mutex(false, "Local\\Starward.SteamCN.4162040");
        bool locked;
        try { locked = singleLaunch.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new InvalidOperationException("A Steam CN session is already active.");
        // The helper lives for the exact child process lifetime, without opening protected process memory.
        // Do not await while owning a Mutex: its ownership is bound to this thread.
        try
        {
            using var process = StartNative(config);
            process.WaitForExit();
            return process.ExitCode;
        }
        finally { singleLaunch.ReleaseMutex(); }
    }

    public static Process StartNative(SteamBridgeConfig config)
    {
        ValidateConfig(config);
        foreach (var existing in Process.GetProcessesByName("ZenlessZoneZero"))
        {
            using (existing)
            {
                if (existing.SessionId == Process.GetCurrentProcess().SessionId)
                    throw new InvalidOperationException("ZZZ is already running. Exit it before starting from Steam.");
            }
        }
        return Process.Start(new ProcessStartInfo(config.GameExecutable)
        {
            Arguments = config.Arguments,
            WorkingDirectory = Path.GetDirectoryName(config.GameExecutable),
            UseShellExecute = false,
        }) ?? throw new IOException("The native game did not start.");
    }

    public static void GuardNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Directory links are not supported: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void WriteAtomic(string path, string text)
    {
        GuardNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
