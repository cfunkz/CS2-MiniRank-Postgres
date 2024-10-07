﻿using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using System.Collections.Concurrent;
using Npgsql;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using DatabaseManager;

namespace ClassicExtender
{
    public enum Ranks
    {
        LVL1,
        LVL2,
        LVL3,
        LVL4,
        LVL5
    }

        public class PlayerStats
    {
        public ulong SteamId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int FAssists { get; set; }
        public int HS { get; set; }
        public int NS { get; set; }
        public int HeKill { get; set; }
        public int MollyKill { get; set; }
        public int Points { get; set; }
        public Ranks Rank { get; set; }
        public long LastConnected { get; set; }
        public long Playtime { get; set; }
    }

    public partial class RankPlugin(ILogger<RankPlugin> logger) : BasePlugin, IPluginConfig<ClassicExtenderConfig>
    {
        public ClassicExtenderConfig Config { get; set; } = new();
        public override string ModuleName => "Classic Extender";
        public override string ModuleVersion => "1.0.1";

        private readonly ILogger<RankPlugin> _logger = logger;
        private DatabaseConnectionManager? _connectionManager;

        public void OnConfigParsed(ClassicExtenderConfig config)
        {
            Config = config;
        }

        public async Task InitializeDatabase()
        {
            try
            {
                string connectionString = $"Host={Config.DatabaseHost};Port={Config.DatabasePort};Username={Config.DatabaseUser};Password={Config.DatabasePassword};Database={Config.DatabaseName}";
                _connectionManager = new DatabaseConnectionManager(connectionString);
                using var connection = await _connectionManager.GetConnectionAsync();

                string createTableSql = @"
                    CREATE TABLE IF NOT EXISTS PlayerStats (
                        SteamId BIGINT PRIMARY KEY,
                        Name VARCHAR(255),
                        Kills INT DEFAULT 0,
                        Deaths INT DEFAULT 0,
                        Assists INT DEFAULT 0,
                        FAssists INT DEFAULT 0,
                        HS INT DEFAULT 0,
                        NS INT DEFAULT 0,
                        HeKill INT DEFAULT 0,
                        MollyKill INT DEFAULT 0,
                        Points INT DEFAULT 0,
                        Rank INT DEFAULT 0,
                        LastConnected BIGINT DEFAULT EXTRACT(EPOCH FROM NOW()),
                        Playtime BIGINT DEFAULT 0
                    )";

                using var command = new NpgsqlCommand(createTableSql, connection);
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("PlayerStats table created or already exists.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing database: {ex.Message}");
            }
        }

        private readonly ConcurrentDictionary<ulong, PlayerStats> _playerStatsCache = new();

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventPlayerConnectFull>(OnFullConnect);
            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
            RegisterEventHandler<EventPlayerDisconnect>(OnDisconnect);
            RegisterEventHandler<EventServerShutdown>(OnServerOff);

            Task.Run(async () => 
            {
                try
                {
                    await InitializeDatabase();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to initialize database: {ex.Message}");
                }
            });
            _logger.LogInformation("Rank Plugin Loaded!");
        }

        [ConsoleCommand("css_top")]
        public void TopCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player == null)
                return;

            Task.Run(async () =>
            {
                var topPlayersSorted = await GetTopPlayers();

                await Server.NextFrameAsync(() =>
                {
                    player.PrintToChat("--- Top 10 Players by Points ---");
                    if (topPlayersSorted != null && topPlayersSorted.Count > 0)
                    {
                        int rank = 1;
                        foreach (var userData in topPlayersSorted)
                        {
                            double kdRatio = userData.Deaths > 0 
                                ? (userData.Kills + (0.5 * userData.Assists)) / userData.Deaths 
                                : userData.Kills;

                            string formattedRatio = kdRatio.ToString("0.##");
                            
                            player.PrintToChat($"#{rank++}: [{userData.Rank}] {ChatColors.Blue}{userData.Name} {ChatColors.Default}- {ChatColors.Gold}Points: {userData.Points}{ChatColors.Default}, {ChatColors.Green}K/D: {formattedRatio}{ChatColors.Default}, {ChatColors.Blue}K: {userData.Kills}{ChatColors.Default}, {ChatColors.Red}D: {userData.Deaths}{ChatColors.Default}, {ChatColors.LightYellow}A: {userData.Assists}");
                        }
                    }
                    else
                    {
                        player.PrintToChat("No top players available.");
                    }
                });
            });
        }

        private async Task<List<PlayerStats>> GetTopPlayers()
        {
            var topPlayers = new List<PlayerStats>();

            try
            {
                using var connection = await _connectionManager!.GetConnectionAsync();
                string query = @"
                    SELECT SteamId, Name, Kills, Deaths, Assists, FAssists, HS, NS, HeKill, MollyKill, Points, Rank, LastConnected, Playtime
                    FROM PlayerStats
                    ORDER BY Points DESC
                    LIMIT 10";

                await using var command = new NpgsqlCommand(query, connection);

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var playerStats = new PlayerStats
                    {
                        SteamId = (ulong)reader.GetInt64(0),
                        Name = reader.GetString(1),
                        Kills = reader.GetInt32(2),
                        Deaths = reader.GetInt32(3),
                        Assists = reader.GetInt32(4),
                        FAssists = reader.GetInt32(5),
                        HS = reader.GetInt32(6),
                        NS = reader.GetInt32(7),
                        HeKill = reader.GetInt32(8),
                        MollyKill = reader.GetInt32(9),
                        Points = reader.GetInt32(10),
                        Rank = (Ranks)reader.GetInt32(11),
                        LastConnected = reader.GetInt64(12),
                        Playtime = reader.GetInt64(13)
                    };

                    topPlayers.Add(playerStats);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching top players from the database: {ex.Message}");
            }

            return topPlayers;
        }


        [ConsoleCommand("css_rank")]
        public void RankCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player == null)
                return;

            var steamId = player.SteamID;
            
            if (_playerStatsCache.TryGetValue(steamId, out var playerStats))
            {
                // Calculate K/D ratio
                double kdRatio = playerStats.Deaths > 0 
                    ? (playerStats.Kills + (0.5 * playerStats.Assists)) / playerStats.Deaths 
                    : playerStats.Kills;

                string formattedRatio = kdRatio.ToString("0.##");
                var playtime = TimeSpan.FromSeconds(playerStats.Playtime);

                player.PrintToChat($"{ChatColors.Grey}################################################\u2029" +
                    $"[{GetRankString(playerStats)}] {ChatColors.Default}| {ChatColors.Gold}Points: {playerStats.Points} {ChatColors.Default}| {ChatColors.Green}K/D: {formattedRatio} {ChatColors.Default}| {ChatColors.Blue}Kills: {playerStats.Kills} {ChatColors.Default}| {ChatColors.Red}Deaths: {playerStats.Deaths} {ChatColors.Default}| {ChatColors.LightYellow}Assists: {playerStats.Assists}\u2029" +
                    $"{ChatColors.Silver}NoScopes: {playerStats.NS} {ChatColors.Default}| {ChatColors.LightRed}Headshots: {playerStats.HS} {ChatColors.Default}| {ChatColors.Magenta}HE Kills: {playerStats.HeKill} {ChatColors.Default}| {ChatColors.Orange}Molly Kills: {playerStats.MollyKill} {ChatColors.Default}| {ChatColors.Grey}Flash Assists: {playerStats.FAssists}\u2029" +
                    $"{ChatColors.LightBlue}Playtime: {playtime.Hours}h {playtime.Minutes}m {playtime.Seconds}s\u2029" +
                    $"{ChatColors.Default}################################################");
            }
            else
            {
                player.PrintToChat("You have no recorded stats yet.");
            }
        }

        public HookResult OnFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            ulong playerID = @event.Userid?.SteamID ?? 0;
            string playerName = @event.Userid!.PlayerName;

            if (playerID != 0 && playerName != null)
            {
                if (!_playerStatsCache.ContainsKey(playerID))
                {
                    
                    Task.Run(async () => 
                    {
                        try
                        {
                            await LoadStats(playerID, playerName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to load user {ex.Message}");
                        }
                    });
                }

                _logger.LogInformation($"Player {playerName} (SteamID: {playerID}) connected and added to cache.");
            }
            return HookResult.Continue;
        }

        private async Task LoadStats(ulong steamId, string playerName)
        {
            using var connection = await _connectionManager!.GetConnectionAsync();
            string selectSql = "SELECT * FROM PlayerStats WHERE SteamId = @SteamId";

            await using var command = new NpgsqlCommand(selectSql, connection);
            command.Parameters.AddWithValue("@SteamId", (long)steamId);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var playerStats = new PlayerStats
                {
                    SteamId = (ulong)reader.GetInt64(0),
                    Name = playerName,
                    Kills = reader.GetInt32(2),
                    Deaths = reader.GetInt32(3),
                    Assists = reader.GetInt32(4),
                    FAssists = reader.GetInt32(5),
                    HS = reader.GetInt32(6),
                    NS = reader.GetInt32(7),
                    HeKill = reader.GetInt32(8),
                    MollyKill = reader.GetInt32(9),
                    Points = reader.GetInt32(10),
                    Rank = (Ranks)reader.GetInt32(11),
                    LastConnected = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Playtime = reader.GetInt64(13)
                };

                _playerStatsCache[steamId] = playerStats;
            }
            else
            {
                NewUser(steamId, playerName);
            }
        }

        public async Task SaveCacheToDatabase()
        {
            using var connection = await _connectionManager!.GetConnectionAsync();

            foreach (var playerStats in _playerStatsCache.Values)
            {
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                playerStats.Playtime += currentTime - playerStats.LastConnected;
                playerStats.LastConnected = currentTime;

                string upsertSql = @"
                    INSERT INTO PlayerStats (SteamId, Name, Kills, Deaths, Assists, FAssists, HS, NS, HeKill, MollyKill, Points, Rank, LastConnected, Playtime)
                    VALUES (@SteamId, @Name, @Kills, @Deaths, @Assists, @FAssists, @HS, @NS, @HeKill, @MollyKill, @Points, @Rank, @LastConnected, @Playtime)
                    ON CONFLICT (SteamId) DO UPDATE SET
                        Name = EXCLUDED.Name,
                        Kills = EXCLUDED.Kills,
                        Deaths = EXCLUDED.Deaths,
                        Assists = EXCLUDED.Assists,
                        FAssists = EXCLUDED.FAssists,
                        HS = EXCLUDED.HS,
                        NS = EXCLUDED.NS,
                        HeKill = EXCLUDED.HeKill,
                        MollyKill = EXCLUDED.MollyKill,
                        Points = EXCLUDED.Points,
                        Rank = EXCLUDED.Rank,
                        LastConnected = EXCLUDED.LastConnected,
                        Playtime = EXCLUDED.Playtime";

                await using var command = new NpgsqlCommand(upsertSql, connection);
                command.Parameters.AddWithValue("@SteamId", (long)playerStats.SteamId);
                command.Parameters.AddWithValue("@Name", playerStats.Name);
                command.Parameters.AddWithValue("@Kills", playerStats.Kills);
                command.Parameters.AddWithValue("@Deaths", playerStats.Deaths);
                command.Parameters.AddWithValue("@Assists", playerStats.Assists);
                command.Parameters.AddWithValue("@FAssists", playerStats.FAssists);
                command.Parameters.AddWithValue("@HS", playerStats.HS);
                command.Parameters.AddWithValue("@NS", playerStats.NS);
                command.Parameters.AddWithValue("@HeKill", playerStats.HeKill);
                command.Parameters.AddWithValue("@MollyKill", playerStats.MollyKill);
                command.Parameters.AddWithValue("@Points", playerStats.Points);
                command.Parameters.AddWithValue("@Rank", (int)playerStats.Rank);
                command.Parameters.AddWithValue("@LastConnected", playerStats.LastConnected);
                command.Parameters.AddWithValue("@Playtime", playerStats.Playtime);
                await command.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Player stats cache saved to the database.");
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult OnEventRoundStartPre(EventRoundStart @event, GameEventInfo info)
        {
            Task.Run(() => SaveCacheToDatabase());
            return HookResult.Continue;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            ulong victimSteamID = @event.Userid?.SteamID ?? 0;
            ulong attackerSteamID = @event.Attacker?.SteamID ?? 0;
            ulong assisterSteamID = @event.Assister?.SteamID ?? 0;
            bool isHeadshot = @event.Headshot;
            bool isNoScope = @event.Noscope;
            bool isMO = @event.Weapon == "inferno";
            bool isHE = @event.Weapon == "hegrenade";

            if (victimSteamID == 0 && attackerSteamID == 0 && assisterSteamID == 0)
            {
                return HookResult.Continue;
            }
            else 
            {
                UpdatePlayerStats(attackerSteamID, victimSteamID, assisterSteamID, isHeadshot, isNoScope, isMO, isHE);
                return HookResult.Continue;
            }
        }

        private void UpdatePlayerStats(ulong attackerSteamID, ulong victimSteamID, ulong assisterSteamID, bool isHeadshot, bool isNoScope, bool isMO, bool isHE)
        {
            if (attackerSteamID != 0) 
            {
                var attackerStats = GetStats(attackerSteamID);
                attackerStats.Kills++;
                attackerStats.Points += Config.KillPoints;
                if (isHeadshot) {
                    attackerStats.Points += Config.HSPoints;
                    attackerStats.HS++;
                }
                if (isNoScope) {
                    attackerStats.Points += Config.NSPoints;
                    attackerStats.NS++;
                }
                if (isMO){
                    attackerStats.Points += Config.MOPoints;
                    attackerStats.MollyKill++;
                }
                if (isHE){
                    attackerStats.Points += Config.HEPoints;
                    attackerStats.HeKill++;
                }
            }

            if (victimSteamID != 0) 
            {
                var victimStats = GetStats(victimSteamID);
                victimStats.Deaths++;
                victimStats.Points -= Config.DeathPoints;
            }

            if (assisterSteamID != 0) 
            { 
                var assisterStats = GetStats(assisterSteamID);
                assisterStats.Assists++;
                assisterStats.Points += Config.AssistPoints;
            }
        }

        private PlayerStats GetStats(ulong steamId)
        {
            _playerStatsCache.TryGetValue(steamId, out var playerStats);
            return playerStats!;
        }

        private PlayerStats NewUser(ulong steamId, string playerName)
        {
            return _playerStatsCache.GetOrAdd(steamId, _ => new PlayerStats
            {
                SteamId = steamId,
                Name = playerName,
                Kills = 0,
                Deaths = 0,
                Assists = 0,
                FAssists = 0,
                HS = 0,
                NS = 0,
                HeKill = 0,
                MollyKill = 0,
                Points = 0,
                Rank = Ranks.LVL1,
                LastConnected = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        private static string GetRankString(PlayerStats stats)
        {
            UpdateRank(stats);
            return stats.Rank.ToString();
        }

        private static void UpdateRank(PlayerStats stats)
        {
            if (stats.Points < 100)
            {
                stats.Rank = Ranks.LVL1;
            }
            else if (stats.Points < 1000)
            {
                stats.Rank = Ranks.LVL2;
            }
            else if (stats.Points < 2000)
            {
                stats.Rank = Ranks.LVL3;
            }
            else if (stats.Points < 3000)
            {
                stats.Rank = Ranks.LVL4;
            }
            else
            {
                stats.Rank = Ranks.LVL5;
            }
        }

        public HookResult OnDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            ulong playerID = @event.Userid?.AuthorizedSteamID?.SteamId64 ?? 0;
            if (playerID != 0)
            {
                Task.Run(async () =>
                {
                    if (_playerStatsCache.TryRemove(playerID, out var playerStats))
                    {
                        await SavePlayerStats(playerStats);
                    }
                });
            }

            return HookResult.Continue;
        }

        private async Task SavePlayerStats(PlayerStats playerStats)
        {
            using var connection = await _connectionManager!.GetConnectionAsync();

            string upsertSql = @"
                INSERT INTO PlayerStats (SteamId, Name, Kills, Deaths, Assists, FAssists, HS, NS, HeKill, MollyKill, Points, Rank, LastConnected, Playtime)
                VALUES (@SteamId, @Name, @Kills, @Deaths, @Assists, @FAssists, @HS, @NS, @HeKill, @MollyKill, @Points, @Rank, @LastConnected, @Playtime)
                ON CONFLICT (SteamId) DO UPDATE SET
                    Name = EXCLUDED.Name,
                    Kills = EXCLUDED.Kills,
                    Deaths = EXCLUDED.Deaths,
                    Assists = EXCLUDED.Assists,
                    FAssists = EXCLUDED.FAssists,
                    HS = EXCLUDED.HS,
                    NS = EXCLUDED.NS,
                    HeKill = EXCLUDED.HeKill,
                    MollyKill = EXCLUDED.MollyKill,
                    Points = EXCLUDED.Points,
                    Rank = EXCLUDED.Rank,
                    LastConnected = EXCLUDED.LastConnected,
                    Playtime = EXCLUDED.Playtime";

            await using var command = new NpgsqlCommand(upsertSql, connection);
            command.Parameters.AddWithValue("@SteamId", (long)playerStats.SteamId);
            command.Parameters.AddWithValue("@Name", playerStats.Name);
            command.Parameters.AddWithValue("@Kills", playerStats.Kills);
            command.Parameters.AddWithValue("@Deaths", playerStats.Deaths);
            command.Parameters.AddWithValue("@Assists", playerStats.Assists);
            command.Parameters.AddWithValue("@FAssists", playerStats.FAssists);
            command.Parameters.AddWithValue("@HS", playerStats.HS);
            command.Parameters.AddWithValue("@NS", playerStats.NS);
            command.Parameters.AddWithValue("@HeKill", playerStats.HeKill);
            command.Parameters.AddWithValue("@MollyKill", playerStats.MollyKill);
            command.Parameters.AddWithValue("@Points", playerStats.Points);
            command.Parameters.AddWithValue("@Rank", (int)playerStats.Rank);
            command.Parameters.AddWithValue("@LastConnected", playerStats.LastConnected);
            command.Parameters.AddWithValue("@Playtime", playerStats.Playtime);
            await command.ExecuteNonQueryAsync();
        }

        public HookResult OnServerOff(EventServerShutdown @event, GameEventInfo info)
        {
                Task.Run(() => SaveCacheToDatabase());
                return HookResult.Continue;
        }
    }
}