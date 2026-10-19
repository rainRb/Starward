using System.Text.Json;

namespace Starward.Core.Steam;

/// <summary>Explicit, offline-only management of a separate Steam placeholder install.</summary>
public sealed class SteamInstallManager(string stateDirectory, IReadOnlyCollection<string> registeredLibraries, Func<bool> isBusy)
{
    private const string Product = "Starward.SteamCN.4162040.v1";
    private string ReceiptPath => Path.Combine(stateDirectory, "install.json");
    private sealed record Receipt(string Product, string Token, string Library, SteamMetadata Metadata);

    public bool IsManaged => File.Exists(ReceiptPath);

    private void CheckIdle()
    {
        SteamBridge.GuardNoLinks(stateDirectory);
        if (isBusy()) throw new InvalidOperationException("Exit Steam (including the tray client), ZZZ and the Steam helper before changing this setup.");
    }

    private FileStream LockOperation()
    {
        string path = Path.Combine(stateDirectory, "operation.lock");
        SteamBridge.GuardNoLinks(path);
        Directory.CreateDirectory(stateDirectory);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static bool SamePath(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a))
        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPath(string parent, string path) => SamePath(parent, path)
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private (string Game, string Executable, string Manifest) Paths(string library, SteamMetadata metadata)
    {
        metadata.Validate();
        if (!Path.IsPathFullyQualified(library) || !registeredLibraries.Any(x => SamePath(x, library))
            || !Directory.Exists(Path.Combine(library, "steamapps"))) throw new InvalidDataException("Choose a registered Steam library.");
        string game = Path.Combine(library, "steamapps", "common", metadata.InstallDirectory);
        string exe = Path.Combine(game, metadata.Executable.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        string manifest = Path.Combine(library, "steamapps", "appmanifest_" + SteamMetadata.AppId + ".acf");
        foreach (string path in new[] { game, exe, manifest }) SteamBridge.GuardNoLinks(path);
        if (ContainsPath(game, stateDirectory) || ContainsPath(stateDirectory, game)) throw new InvalidDataException("State and Steam install must be separate.");
        return (game, exe, manifest);
    }

    private Receipt ReadReceipt()
    {
        SteamBridge.GuardNoLinks(ReceiptPath);
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(ReceiptPath)) ?? throw new InvalidDataException("Invalid receipt.");
        if (receipt.Product != Product || !Guid.TryParseExact(receipt.Token, "N", out _)) throw new InvalidDataException("Unknown receipt owner.");
        Paths(receipt.Library, receipt.Metadata);
        return receipt;
    }

    private static SteamVdf OwnedManifest(string path, Receipt receipt)
    {
        var root = SteamVdf.Parse(File.ReadAllText(path));
        var app = root.Node("AppState");
        if (app.Text("appid") != SteamMetadata.AppId || app.Text("installdir") != receipt.Metadata.InstallDirectory
            || app.Text("StarwardSteamCN") != receipt.Token) throw new IOException("The Steam manifest is no longer owned by this setup. No files changed.");
        return root;
    }

    private static void CheckPlaceholder(string game, string executable)
    {
        if (!Directory.Exists(game)) return;
        // Walk without following directory links. Every existing file must be our empty placeholder.
        void Visit(string directory)
        {
            SteamBridge.GuardNoLinks(directory);
            foreach (string item in Directory.EnumerateFileSystemEntries(directory))
            {
                SteamBridge.GuardNoLinks(item);
                if (Directory.Exists(item)) Visit(item);
                else if (!SamePath(item, executable) || new FileInfo(item).Length != 0)
                    throw new IOException("The Steam directory contains files not owned by Starward. They will not be replaced or deleted: " + item);
            }
        }
        Visit(game);
    }

    public void Apply(string library, SteamMetadata metadata, string nativeExecutable)
    {
        CheckIdle();
        using var operation = LockOperation();
        var paths = Paths(library, metadata);
        SteamBridge.ValidateConfig(new(1, nativeExecutable, ""));
        if (ContainsPath(paths.Game, nativeExecutable) || ContainsPath(Path.GetDirectoryName(nativeExecutable)!, paths.Game))
            throw new InvalidDataException("The native CN game and Steam placeholder directories must be separate.");
        foreach (string other in registeredLibraries)
        {
            if (!SamePath(other, library) && File.Exists(Path.Combine(other, "steamapps", "appmanifest_" + SteamMetadata.AppId + ".acf")))
                throw new IOException("This Steam app already exists in another library.");
        }
        Receipt receipt;
        SteamVdf root;
        if (IsManaged)
        {
            receipt = ReadReceipt();
            if (!SamePath(receipt.Library, library) || receipt.Metadata.InstallDirectory != metadata.InstallDirectory
                || receipt.Metadata.Executable != metadata.Executable) throw new IOException("Remove the managed setup before changing its Steam paths.");
            CheckPlaceholder(paths.Game, paths.Executable);
            root = File.Exists(paths.Manifest) ? OwnedManifest(paths.Manifest, receipt) : new SteamVdf { ["AppState"] = new SteamVdf() };
        }
        else
        {
            if (File.Exists(paths.Manifest) || Directory.Exists(paths.Game)) throw new IOException("An existing Steam install or directory will not be taken over.");
            receipt = new(Product, Guid.NewGuid().ToString("N"), Path.GetFullPath(library), metadata);
            root = new SteamVdf { ["AppState"] = new SteamVdf() };
            // Receipt is also an interruption journal, written before creating any library files.
            SteamBridge.WriteAtomic(ReceiptPath, JsonSerializer.Serialize(receipt));
        }
        var app = root.Node("AppState");
        app["appid"] = SteamMetadata.AppId;
        app["name"] = "Zenless Zone Zero";
        app["installdir"] = metadata.InstallDirectory;
        app["buildid"] = metadata.BuildId;
        app["StateFlags"] = "4";
        app["AutoUpdateBehavior"] = "1";
        app["StarwardSteamCN"] = receipt.Token;
        var depots = new SteamVdf();
        foreach (var depot in metadata.Depots) depots[depot.Key] = new SteamVdf { ["manifest"] = depot.Value.Manifest, ["size"] = depot.Value.Size };
        app["InstalledDepots"] = depots;
        CheckIdle();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Executable)!);
        if (!File.Exists(paths.Executable)) { using var empty = new FileStream(paths.Executable, FileMode.CreateNew); }
        CheckPlaceholder(paths.Game, paths.Executable);
        if (File.Exists(paths.Manifest))
        {
            OwnedManifest(paths.Manifest, receipt);
            string backup = Path.Combine(stateDirectory, "backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".acf");
            SteamBridge.WriteAtomic(backup, File.ReadAllText(paths.Manifest));
        }
        CheckIdle();
        SteamBridge.WriteAtomic(paths.Manifest, root.ToString());
        SteamBridge.WriteAtomic(ReceiptPath, JsonSerializer.Serialize(receipt with { Metadata = metadata }));
    }

    public void ValidateInstalled()
    {
        var receipt = ReadReceipt();
        var paths = Paths(receipt.Library, receipt.Metadata);
        OwnedManifest(paths.Manifest, receipt);
        CheckPlaceholder(paths.Game, paths.Executable);
        if (!File.Exists(paths.Executable)) throw new IOException("The Steam placeholder is missing. Apply the setup again with Steam closed.");
    }

    public void Remove()
    {
        CheckIdle();
        using var operation = LockOperation();
        if (!IsManaged) return;
        var receipt = ReadReceipt();
        var paths = Paths(receipt.Library, receipt.Metadata);
        CheckPlaceholder(paths.Game, paths.Executable);
        if (File.Exists(paths.Manifest))
        {
            OwnedManifest(paths.Manifest, receipt);
            SteamBridge.WriteAtomic(Path.Combine(stateDirectory, "backups", "removed-" + Guid.NewGuid().ToString("N") + ".acf"), File.ReadAllText(paths.Manifest));
            CheckIdle();
            File.Delete(paths.Manifest);
        }
        CheckIdle();
        if (File.Exists(paths.Executable)) File.Delete(paths.Executable);
        // Remove only empty directories, never recursively delete a directory tree.
        for (string? dir = Path.GetDirectoryName(paths.Executable); dir != null && ContainsPath(paths.Game, dir); dir = Path.GetDirectoryName(dir))
        {
            SteamBridge.GuardNoLinks(dir);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        File.Delete(ReceiptPath);
    }
}
