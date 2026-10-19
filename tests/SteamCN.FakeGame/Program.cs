using System.Text.Json;

// Isolated launch fixture; no game code, network access or Steam API calls.
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Arguments = args, WorkingDirectory = Environment.CurrentDirectory,
    SteamAppId = Environment.GetEnvironmentVariable("SteamAppId") }));
Thread.Sleep(1200);
return 7;
