using Starward.Core.Steam;
using System.Runtime.InteropServices;

try
{
    return SteamBridge.Run(args);
}
catch (Exception ex)
{
    string message = "Steam CN launch failed.\n\n" + ex.Message + "\n\nIf Windows requires elevation, exit Steam and start Steam as administrator using normal UAC. No game or security settings have been changed.";
    if (OperatingSystem.IsWindows()) MessageBoxW(0, message, "Starward · ZZZ Steam CN", 0x10);
    else Console.Error.WriteLine(message);
    return 1;
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(nint window, string text, string caption, uint type);
