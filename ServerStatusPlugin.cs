using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json;

namespace ServerStatusPlugin;

[MinimumApiVersion(198)]
public class ServerStatusPlugin : BasePlugin
{
    public override string ModuleName => "ServerStatusPlugin";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Kampfhamster";
    public override string ModuleDescription => "Writes server status to JSON periodically and on round start";

    private const string DefaultStatusPath = "server_status/status.json";
    private const float DefaultInterval = 30.0f;

    private ConVar? _cvStatusPath;
    private ConVar? _cvUpdateInterval;

    private readonly object _fileLock = new();

    public override void Load(bool hotReload)
    {
        _cvStatusPath = ConVar.Register("sv_status_output", DefaultStatusPath, "Output path for status file", ConVarFlags.None);
        _cvUpdateInterval = ConVar.Register("sv_status_interval", DefaultInterval.ToString(), "Update interval in seconds", ConVarFlags.None);

        RegisterEventHandler<EventRoundStart>(OnRoundStart);

        AddTimer(GetUpdateInterval(), () => 
        {
            Task.Run(WriteStatusFile);
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private float GetUpdateInterval() => 
        float.TryParse(_cvUpdateInterval?.StringValue, out var interval) && interval > 0 
            ? interval 
            : DefaultInterval;

    private HookResult OnRoundStart(EventRoundStart e)
    {
        Task.Run(WriteStatusFile);
        return HookResult.Continue;
    }

    private async Task WriteStatusFile()
    {
        try
        {
            var serverInfo = new ServerStatusData
            {
                Map = Server.MapName,
                Players = Utilities.GetPlayers().Count(p => IsValidPlayer(p)),
                MaxPlayers = Server.MaxPlayers,
                ServerName = ConVar.Find("hostname")?.StringValue ?? "Unknown",
                PlayerList = Utilities.GetPlayers()
                    .Where(IsValidPlayer)
                    .Select(p => new PlayerData
                    {
                        Name = p.PlayerName,
                        SteamId = p.SteamID.ToString(),
                        Score = p.PlayerPawn.Value?.Score ?? 0,
                        Kills = p.PlayerPawn.Value?.Kills ?? 0,
                        Deaths = p.PlayerPawn.Value?.Deaths ?? 0,
                        Assists = p.PlayerPawn.Value?.Assists ?? 0,
                        DurationSeconds = p.ConnectedTime,
                        DurationFormatted = FormatDuration(p.ConnectedTime)
                    }).ToList()
            };

            var json = JsonSerializer.Serialize(serverInfo, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var outputPath = Path.Combine(Server.GameDirectory, _cvStatusPath?.StringValue ?? DefaultStatusPath);
            var directory = Path.GetDirectoryName(outputPath);

            lock (_fileLock)
            {
                if (!Directory.Exists(directory) && !string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(outputPath, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{ModuleName}] Fehler: {ex.Message}");
        }
    }

    private bool IsValidPlayer(CCSPlayerController player) => 
        player is { IsValid: true, IsBot: false, IsHLTV: false, IsReplay: false };

    private static string FormatDuration(float seconds) => 
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
}

public class ServerStatusData
{
    public string Map { get; set; } = "";
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerName { get; set; } = "";
    public List<PlayerData> PlayerList { get; set; } = new();
}

public class PlayerData
{
    public string Name { get; set; } = "";
    public string SteamId { get; set; } = "";
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public float DurationSeconds { get; set; }
    public string DurationFormatted { get; set; } = "";
}