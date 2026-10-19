using System.Text.Json;
using System.Text.RegularExpressions;

namespace Starward.Core.Steam;

/// <summary>Public app-info data only. No game files or authentication data.</summary>
public sealed record SteamMetadata(string InstallDirectory, string Executable, string BuildId, Dictionary<string, SteamDepot> Depots)
{
    public const string AppId = "4162040";

    public static SteamMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var app = document.RootElement.GetProperty("data").GetProperty(AppId);
        var config = app.GetProperty("config");
        string directory = config.GetProperty("installdir").GetString() ?? "";
        ValidateRelative(directory, false);
        var launches = config.GetProperty("launch").EnumerateObject().Select(x => x.Value)
            .Where(x => x.TryGetProperty("config", out var c) && c.TryGetProperty("oslist", out var os)
                && (os.GetString() ?? "").Split(',').Contains("windows")).ToArray();
        if (launches.Length != 1) throw new InvalidDataException("Expected one Windows launch entry.");
        string executable = launches[0].GetProperty("executable").GetString() ?? "";
        ValidateRelative(executable, true);
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid Windows executable.");
        var depots = app.GetProperty("depots");
        string build = PositiveId(depots.GetProperty("branches").GetProperty("public").GetProperty("buildid").GetString());
        var installed = new Dictionary<string, SteamDepot>();
        foreach (var entry in depots.EnumerateObject())
        {
            if (!Regex.IsMatch(entry.Name, "^[0-9]+$")) continue;
            var depot = entry.Value;
            if (depot.TryGetProperty("config", out var c) && c.TryGetProperty("oslist", out var os)
                && !(os.GetString() ?? "").Split(',').Contains("windows")) continue;
            if (!depot.TryGetProperty("manifests", out var manifests) || !manifests.TryGetProperty("public", out var manifest)) continue;
            installed.Add(PositiveId(entry.Name), new SteamDepot(PositiveId(manifest.GetProperty("gid").GetString()),
                PositiveId(manifest.GetProperty("size").GetString())));
        }
        if (installed.Count == 0) throw new InvalidDataException("Public Windows depot manifests are missing.");
        return new(directory, executable, build, installed);
    }

    public void Validate()
    {
        ValidateRelative(InstallDirectory, false);
        ValidateRelative(Executable, true);
        if (!Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid executable.");
        PositiveId(BuildId);
        if (Depots.Count == 0) throw new InvalidDataException("Missing depots.");
        foreach (var depot in Depots) { PositiveId(depot.Key); PositiveId(depot.Value.Manifest); PositiveId(depot.Value.Size); }
    }

    private static string PositiveId(string? value) => ulong.TryParse(value, out ulong id) && id > 0
        && value == id.ToString(System.Globalization.CultureInfo.InvariantCulture) ? value : throw new InvalidDataException("Invalid numeric metadata.");

    internal static void ValidateRelative(string value, bool nested)
    {
        var parts = value.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(value) || (!nested && parts.Length != 1) || parts.Any(p => p.Length == 0
            || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny("<>:\"|?*\r\n\t".ToCharArray()) >= 0
            || p.Any(char.IsControl) || Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\\.)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe relative Steam path.");
    }
}

public sealed record SteamDepot(string Manifest, string Size);
