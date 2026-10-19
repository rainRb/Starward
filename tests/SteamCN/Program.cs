using Starward.Core.Steam;
using System.Diagnostics;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
string fakeExe = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name)
{
    try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or JsonException)
    { Check(true, name); return; }
    throw new Exception("FAIL (accepted): " + name);
}
string Json(string dir = "ZZZ test", string exe = "launcher/HYP.exe", string build = "123") =>
    JsonSerializer.Serialize(new { data = new Dictionary<string, object> { [SteamMetadata.AppId] = new
    {
        config = new { installdir = dir, launch = new Dictionary<string, object> { ["0"] = new { config = new { oslist = "windows" }, executable = exe } } },
        depots = new Dictionary<string, object>
        {
            ["4162041"] = new { config = new { oslist = "windows" }, manifests = new { @public = new { gid = "18446744073709551614", size = "80000000000" } } },
            ["4162042"] = new { config = new { oslist = "linux" }, manifests = new { @public = new { gid = "8", size = "9" } } },
            ["branches"] = new { @public = new { buildid = build } }
        }
    } } });
var metadata = SteamMetadata.Parse(Json());
Check(metadata.Depots.Count == 1 && metadata.Depots["4162041"].Manifest == "18446744073709551614", "64-bit depot IDs and Windows filtering");
foreach (string path in new[] { "../CN", "C:\\Games", "foo/bar", "foo.", "CON", "foo\nbar", "" }) Reject(() => SteamMetadata.Parse(Json(path)), "reject install path " + JsonSerializer.Serialize(path));
foreach (string path in new[] { "../HYP.exe", "/HYP.exe", "C:\\HYP.exe", "foo//HYP.exe", "foo/../HYP.exe", "foo:bar.exe", "HYP.dll" }) Reject(() => SteamMetadata.Parse(Json(exe: path)), "reject executable path " + path);
Reject(() => SteamMetadata.Parse(Json(build: "0")), "reject missing real public BuildID");
Reject(() => SteamMetadata.Parse("{\"data\":{\"4732690\":{}}}"), "reject wrong app metadata");
string vdfText = "// comment\n\"root\" { \"path\" \"C:\\\\Games\\\\ZZZ\" \"unknown\" \"preserve me\" }";
var vdf = SteamVdf.Parse(vdfText);
Check(SteamVdf.Parse(vdf.ToString()).Node("root").Text("path") == "C:\\Games\\ZZZ", "VDF round trip preserves escaped paths");
foreach (string malformed in new[] { "\"a\"", "\"a\" {", "}", "\"a\" \"b\" \"a\" \"c\"", "a b", "\"a\" \"unterminated" }) Reject(() => SteamVdf.Parse(malformed), "reject malformed VDF " + malformed);

string library = Path.Combine(root, "library");
string otherLibrary = Path.Combine(root, "other-library");
Directory.CreateDirectory(Path.Combine(library, "steamapps"));
Directory.CreateDirectory(Path.Combine(otherLibrary, "steamapps"));
bool busy = false;
string state = Path.Combine(root, "state");
var manager = new SteamInstallManager(state, new[] { library, otherLibrary }, () => busy);
string manifest = Path.Combine(library, "steamapps", "appmanifest_4162040.acf");
string game = Path.Combine(library, "steamapps", "common", metadata.InstallDirectory);
string placeholder = Path.Combine(game, "launcher", "HYP.exe");
busy = true;
Reject(() => manager.Apply(library, metadata, fakeExe), "Steam running blocks install");
Check(!File.Exists(manifest) && !Directory.Exists(state), "busy check leaves no state or library changes");
busy = false;
Reject(() => manager.Apply(Path.Combine(root, "unregistered"), metadata, fakeExe), "reject unregistered library");
File.WriteAllText(manifest, "existing international install");
Reject(() => manager.Apply(library, metadata, fakeExe), "protect existing international manifest");
Check(File.ReadAllText(manifest) == "existing international install", "conflict manifest unchanged");
File.Delete(manifest);
Directory.CreateDirectory(game);
Reject(() => manager.Apply(library, metadata, fakeExe), "protect preexisting directory even when empty");
Directory.Delete(game);
string otherManifest = Path.Combine(otherLibrary, "steamapps", "appmanifest_4162040.acf");
File.WriteAllText(otherManifest, "other install");
Reject(() => manager.Apply(library, metadata, fakeExe), "protect another library's install");
File.Delete(otherManifest);
manager.Apply(library, metadata, fakeExe);
Check(new FileInfo(placeholder).Length == 0 && manager.IsManaged, "create journal and isolated empty placeholder");
manager.ValidateInstalled(); Check(true, "validate owned installation");
var acf = SteamVdf.Parse(File.ReadAllText(manifest));
Check(acf.Node("AppState").Text("buildid") == "123", "use imported public BuildID");
acf.Node("AppState")["LastPlayed"] = "1234567890";
acf.Node("AppState")["UserFutureField"] = new SteamVdf { ["Nested"] = "preserved" };
File.WriteAllText(manifest, acf.ToString());
manager.Apply(library, SteamMetadata.Parse(Json(build: "456")), fakeExe);
acf = SteamVdf.Parse(File.ReadAllText(manifest));
Check(acf.Node("AppState").Text("buildid") == "456" && acf.Node("AppState").Text("LastPlayed") == "1234567890"
    && acf.Node("AppState").Node("UserFutureField").Text("Nested") == "preserved", "metadata refresh preserves Steam runtime and unknown fields");
Check(Directory.GetFiles(Path.Combine(state, "backups")).Length == 1, "manifest backup before update");
using (var operationLock = new FileStream(Path.Combine(state, "operation.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    Reject(manager.Remove, "concurrent Starward operation is refused");
busy = true; Reject(manager.Remove, "Steam running blocks removal"); busy = false;
File.WriteAllText(placeholder, "downloaded international executable");
Reject(manager.Remove, "never delete downloaded international files");
Check(File.ReadAllText(placeholder) == "downloaded international executable", "downloaded file survives rejected removal");
File.WriteAllText(placeholder, "");
string unknown = Path.Combine(game, "unknown.bin"); File.WriteAllText(unknown, "user data");
Reject(manager.Remove, "unknown files block removal"); File.Delete(unknown);
var token = acf.Node("AppState").Text("StarwardSteamCN");
acf.Node("AppState")["StarwardSteamCN"] = "someone else"; File.WriteAllText(manifest, acf.ToString());
Reject(manager.Remove, "ownership token mismatch blocks removal");
acf.Node("AppState")["StarwardSteamCN"] = token; File.WriteAllText(manifest, acf.ToString());
manager.Remove();
Check(!File.Exists(manifest) && !Directory.Exists(game) && !manager.IsManaged, "reversible removal deletes only owned files");
Check(Directory.GetFiles(Path.Combine(state, "backups")).Length == 2, "retain rollback evidence after removal");
manager.Remove(); Check(true, "removal is idempotent");

int idleCalls = 0;
var interrupted = new SteamInstallManager(Path.Combine(root, "interrupted"), new[] { library }, () => ++idleCalls >= 2);
Reject(() => interrupted.Apply(library, metadata, fakeExe), "client starting mid-setup interrupts writes");
Check(interrupted.IsManaged && !File.Exists(manifest), "interrupted setup retains recovery journal");
new SteamInstallManager(Path.Combine(root, "interrupted"), new[] { library }, () => false).Remove();
Check(!interrupted.IsManaged, "remove recovers interrupted setup");

string linkTarget = Path.Combine(root, "junction-target");
string link = Path.Combine(root, "junction");
Directory.CreateDirectory(linkTarget);
if (OperatingSystem.IsWindows())
{
    using var junction = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{linkTarget}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
    junction.WaitForExit();
    Check(junction.ExitCode == 0, "create isolated junction fixture");
    Reject(() => SteamBridge.GuardNoLinks(Path.Combine(link, "missing-file.json")), "reject links in path ancestors even when final file is absent");
    Directory.Delete(link); // Removes only the junction itself, with no recursive traversal.
}

var config = new SteamBridgeConfig(1, fakeExe, "");
Reject(() => SteamBridge.ValidateConfig(config with { GameExecutable = Path.Combine(root, "HYP.exe") }), "reject wrong native executable");
Reject(() => SteamBridge.ValidateConfig(config with { Arguments = "login_auth_ticket=secret" }), "do not persist authentication tickets");
Reject(() => SteamBridge.ValidateConfig(config with { Arguments = "%command%" }), "do not forward Steam command to native game");
Reject(() => SteamBridge.ValidateConfig(config with { Arguments = "x\r\ny" }), "reject multiline game arguments");
Environment.SetEnvironmentVariable("SteamAppId", null);
Reject(() => SteamBridge.Run(new[] { "--config", "unused", "--", "HYP.exe" }), "direct helper launch without Steam app context fails");
Environment.SetEnvironmentVariable("SteamAppId", SteamMetadata.AppId);
Environment.SetEnvironmentVariable("SteamGameId", SteamMetadata.AppId);
string result = Path.Combine(root, "native-result.json");
string configPath = Path.Combine(root, "launch.json");
File.WriteAllText(configPath, JsonSerializer.Serialize(config with { Arguments = $"\"{result}\" \"CN argument with spaces\"" }));
var clock = Stopwatch.StartNew();
int exit = SteamBridge.Run(new[] { "--config", configPath, "--", "HYP.exe", "--international-ticket=never-forward" });
using var resultDocument = JsonDocument.Parse(File.ReadAllText(result));
Check(exit == 7 && clock.ElapsedMilliseconds >= 1100, "helper stays alive for native process and returns its exit code");
var output = resultDocument.RootElement;
Check(output.GetProperty("Arguments").GetArrayLength() == 2 && output.GetProperty("Arguments")[1].GetString() == "CN argument with spaces", "preserve native quoting and discard original Steam command arguments");
Check(output.GetProperty("SteamAppId").GetString() == SteamMetadata.AppId && output.GetProperty("WorkingDirectory").GetString() == Path.GetDirectoryName(fakeExe), "inherit Steam app environment and native working directory");
if (args.Length >= 3)
{
    using var packaged = Process.Start(new ProcessStartInfo(Path.GetFullPath(args[2]), $"--config \"{configPath}\" -- \"HYP.exe\" --ignored-steam-argument")
        { UseShellExecute = false })!;
    clock.Restart(); packaged.WaitForExit();
    Check(packaged.ExitCode == 7 && clock.ElapsedMilliseconds >= 1100, "published standalone helper runs native fixture and waits for exit");
}
if (args.Length >= 4)
{
    var live = SteamMetadata.Parse(File.ReadAllText(args[3]));
    Check(live.InstallDirectory == "Zenless Zone Zero" && live.Executable == "HYP.exe", "parse saved real AppID 4162040 metadata snapshot");
}
Environment.SetEnvironmentVariable("SteamAppId", null); Environment.SetEnvironmentVariable("SteamGameId", null);
Console.WriteLine($"{passed} checks passed. Fixtures: {root}");
