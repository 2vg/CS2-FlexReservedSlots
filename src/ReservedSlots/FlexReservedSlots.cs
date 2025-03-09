using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Cvars;

namespace FlexReservedSlots;

public class FlagSet
{
    [JsonPropertyName("Name")] public string Name { get; set; } = "Default";
    [JsonPropertyName("Flags")] public List<string> Flags { get; set; } = new();
    [JsonPropertyName("Priority")] public int Priority { get; set; } = 0; // Higher means more priority
    [JsonPropertyName("AlwaysImmune")] public bool AlwaysImmune { get; set; } = false; // Never kicked if true
}

public class SlotConfig
{
    [JsonPropertyName("FlagSetName")] public string FlagSetName { get; set; } = "Default";
    [JsonPropertyName("SlotCount")] public int SlotCount { get; set; } = 1;
    [JsonPropertyName("EnabledMaps")] public List<string> EnabledMaps { get; set; } = new(); // Empty means enabled on all maps
    [JsonPropertyName("DisabledMaps")] public List<string> DisabledMaps { get; set; } = new(); // Maps where this slot is disabled
    [JsonPropertyName("EnabledEvents")] public List<string> EnabledEvents { get; set; } = new(); // Empty means enabled regardless of event
    [JsonPropertyName("DisabledEvents")] public List<string> DisabledEvents { get; set; } = new(); // Events where this slot is disabled
}

public class FlexReservedSlotsConfig : BasePluginConfig
{
    [JsonPropertyName("Flag Sets")] public List<FlagSet> FlagSets { get; set; } = new()
    {
        new FlagSet { Name = "Admin", Flags = new() { "@css/generic", "@css/root" }, Priority = 100, AlwaysImmune = true },
        new FlagSet { Name = "Default", Flags = new() { "@css/reservation", "@css/vip" }, Priority = 50, AlwaysImmune = false }
    };
    
    [JsonPropertyName("Slot Configurations")] public List<SlotConfig> SlotConfigurations { get; set; } = new()
    {
        new SlotConfig { FlagSetName = "Admin", SlotCount = 1 },
        new SlotConfig { FlagSetName = "Default", SlotCount = 1 }
    };
    
    [JsonPropertyName("Reserved Slots Method")] public int reservedSlotsMethod { get; set; } = 0;
    [JsonPropertyName("Leave One Slot Open")] public bool openSlot { get; set; } = false;
    [JsonPropertyName("Kick Reason")] public int kickReason { get; set; } = 135;
    [JsonPropertyName("Kick Delay")] public int kickDelay { get; set; } = 5;
    [JsonPropertyName("Kick Check Method")] public int kickCheckMethod { get; set; } = 0;
    [JsonPropertyName("Kick Type")] public int kickType { get; set; } = 0;
    [JsonPropertyName("Kick Players In Spectate")] public bool kickPlayersInSpectate { get; set; } = true;
    [JsonPropertyName("Log Kicked Players")] public bool logKickedPlayers { get; set; } = true;
    [JsonPropertyName("Display Kicked Players Message")] public int displayKickedPlayers { get; set; } = 2;
}

public class FlexReservedSlots : BasePlugin, IPluginConfig<FlexReservedSlotsConfig>
{
    public override string ModuleName => "Flex Reserved Slots";
    public override string ModuleAuthor => "Nocky (SourceFactory.eu), uru";
    public override string ModuleVersion => "2.0.0";

    public enum KickType
    {
        Random,
        HighestPing,
        HighestScore,
        LowestScore,
    }

    public enum KickReason
    {
        ServerIsFull,
        ReservedPlayerJoined,
    }

    public class PlayerReservationInfo
    {
        public string FlagSetName { get; set; } = "";
        public int Priority { get; set; } = 0;
        public bool AlwaysImmune { get; set; } = false;
    }

    public List<int> waitingForSelectTeam = new();
    public Dictionary<int, PlayerReservationInfo> reservedPlayers = new();
    public Dictionary<int, KickReason> waitingForKick = new();
    public FlexReservedSlotsConfig Config { get; set; } = new();
    
    // Cache for slot configuration active status
    private Dictionary<string, bool> slotActiveCache = new();
    private string lastMapName = "";
    private string lastEventName = "";
    
    // FakeConVars
    private FakeConVar<string>? currentEventCvar;
    private Dictionary<string, FakeConVar<int>> slotEnabledCvars = new();
    private Dictionary<string, FakeConVar<string>> slotEnabledMapsCvars = new();
    private Dictionary<string, FakeConVar<string>> slotDisabledMapsCvars = new();
    private Dictionary<string, FakeConVar<string>> slotEnabledEventsCvars = new();
    private Dictionary<string, FakeConVar<string>> slotDisabledEventsCvars = new();
    
    public void OnConfigParsed(FlexReservedSlotsConfig config)
    {
        Config = config;
        
        // Validate flag sets
        foreach (var flagSet in Config.FlagSets)
        {
            if (!flagSet.Flags.Any())
            {
                SendConsoleMessage($"[Reserved Slots] Flag set '{flagSet.Name}' has no flags defined!", ConsoleColor.Yellow);
            }
        }
        
        // Validate slot configurations
        foreach (var slotConfig in Config.SlotConfigurations)
        {
            if (!Config.FlagSets.Any(fs => fs.Name == slotConfig.FlagSetName))
            {
                SendConsoleMessage($"[Reserved Slots] Slot configuration references non-existent flag set '{slotConfig.FlagSetName}'!", ConsoleColor.Red);
            }
        }
        
        if (!Config.FlagSets.Any() || !Config.SlotConfigurations.Any())
        {
            SendConsoleMessage("[Reserved Slots] No valid flag sets or slot configurations defined!", ConsoleColor.Red);
        }
        
        // Initialize FakeConVars
        InitializeFakeConVars();
    }
    
    private void InitializeFakeConVars()
    {
        try
        {
            // Clear existing FakeConVars to prevent duplicates on hot reload
            currentEventCvar = null;
            slotEnabledCvars.Clear();
            slotEnabledMapsCvars.Clear();
            slotDisabledMapsCvars.Clear();
            slotEnabledEventsCvars.Clear();
            slotDisabledEventsCvars.Clear();
            
            // Create current event FakeConVar
            currentEventCvar = new FakeConVar<string>("css_rs_current_event", "Current event for reserved slots", "");
            
            // Create FakeConVars for each slot configuration
            foreach (var slotConfig in Config.SlotConfigurations)
            {
                string safeName = GetSafeName(slotConfig.FlagSetName);
                
                // Slot enabled FakeConVar
                var enabledCvar = new FakeConVar<int>($"css_rs_slot_{safeName}_enabled", $"Enable/disable the {slotConfig.FlagSetName} slot configuration", 1);
                slotEnabledCvars[slotConfig.FlagSetName] = enabledCvar;
                
                // Enabled maps FakeConVar
                var enabledMapsCvar = new FakeConVar<string>($"css_rs_slot_{safeName}_maps", 
                    $"Comma-separated list of maps where the {slotConfig.FlagSetName} slot is enabled (empty = all maps)", 
                    string.Join(",", slotConfig.EnabledMaps));
                slotEnabledMapsCvars[slotConfig.FlagSetName] = enabledMapsCvar;
                
                // Disabled maps FakeConVar
                var disabledMapsCvar = new FakeConVar<string>($"css_rs_slot_{safeName}_disabled_maps", 
                    $"Comma-separated list of maps where the {slotConfig.FlagSetName} slot is disabled", 
                    string.Join(",", slotConfig.DisabledMaps));
                slotDisabledMapsCvars[slotConfig.FlagSetName] = disabledMapsCvar;
                
                // Enabled events FakeConVar
                var enabledEventsCvar = new FakeConVar<string>($"css_rs_slot_{safeName}_events", 
                    $"Comma-separated list of events where the {slotConfig.FlagSetName} slot is enabled (empty = all events)", 
                    string.Join(",", slotConfig.EnabledEvents));
                slotEnabledEventsCvars[slotConfig.FlagSetName] = enabledEventsCvar;
                
                // Disabled events FakeConVar
                var disabledEventsCvar = new FakeConVar<string>($"css_rs_slot_{safeName}_disabled_events", 
                    $"Comma-separated list of events where the {slotConfig.FlagSetName} slot is disabled", 
                    string.Join(",", slotConfig.DisabledEvents));
                slotDisabledEventsCvars[slotConfig.FlagSetName] = disabledEventsCvar;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error initializing FakeConVars: {ex.Message}");
        }
    }
    
    private string GetSafeName(string name)
    {
        // Convert name to a safe string for FakeConVar names (lowercase, no spaces, only alphanumeric and underscore)
        return new string(name.ToLower().Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (waitingForKick.Count > 0)
            {
                foreach (var item in waitingForKick)
                {
                    var player = Utilities.GetPlayerFromSlot(item.Key);
                    if (player != null && player.IsValid)
                    {
                        var kickMessage = item.Value == KickReason.ServerIsFull ? Localizer["Hud.ServerIsFull"] : Localizer["Hud.ReservedPlayerJoined"];
                        player.PrintToCenterHtml(kickMessage);
                    }
                }
            }
        });
        
        RegisterEventHandler<EventGameNewmap>((@event, info) =>
        {
            // Clear reserved players on map change
            reservedPlayers.Clear();
            waitingForSelectTeam.Clear();
            waitingForKick.Clear();
            
            // Clear slot active cache on map change
            slotActiveCache.Clear();
            lastMapName = Server.MapName;
            lastEventName = GetCurrentEvent();
            
            return HookResult.Continue;
        });
        
        AddCommand("css_event", "Set the current event for reserved slots", (player, info) =>
        {
            if (player == null || !AdminManager.PlayerHasPermissions(player, "@css/admin"))
            {
                SendConsoleMessage("[Reserved Slots] Only admins can set events!", ConsoleColor.Red);
                return;
            }
            
            if (info.ArgCount < 2)
            {
                player.PrintToChat($"[Reserved Slots] Current event: {(string.IsNullOrEmpty(GetCurrentEvent()) ? "None" : GetCurrentEvent())}");
                player.PrintToChat("[Reserved Slots] Usage: css_event <event_name or 'none'>");
                return;
            }
            
            string eventName = info.ArgByIndex(1);
            if (eventName.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                SetCurrentEvent("");
                player.PrintToChat("[Reserved Slots] Event cleared.");
            }
            else
            {
                SetCurrentEvent(eventName);
                player.PrintToChat($"[Reserved Slots] Event set to: {eventName}");
            }
            
            // Clear slot active cache when event changes
            slotActiveCache.Clear();
            lastEventName = GetCurrentEvent();
        });
        
        AddCommand("css_rs_status", "Show the status of reserved slots", (player, info) =>
        {
            if (player == null || !AdminManager.PlayerHasPermissions(player, "@css/admin"))
            {
                SendConsoleMessage("[Reserved Slots] Only admins can view status!", ConsoleColor.Red);
                return;
            }
            
            player.PrintToChat($"[Reserved Slots] Current event: {(string.IsNullOrEmpty(GetCurrentEvent()) ? "None" : GetCurrentEvent())}");
            player.PrintToChat($"[Reserved Slots] Current map: {Server.MapName}");
            player.PrintToChat("[Reserved Slots] Active slot configurations:");
            
            foreach (var slotConfig in Config.SlotConfigurations)
            {
                bool isActive = IsSlotConfigurationActive(slotConfig.FlagSetName);
                player.PrintToChat($"  - {slotConfig.FlagSetName}: {(isActive ? "Active" : "Inactive")} ({slotConfig.SlotCount} slots)");
            }
            
            player.PrintToChat($"[Reserved Slots] Total active reserved slots: {GetTotalActiveReservedSlots()}");
        });
    }
    
    private string GetCurrentEvent()
    {
        try
        {
            return currentEventCvar?.Value ?? "";
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error getting current event: {ex.Message}");
            return "";
        }
    }
    
    private void SetCurrentEvent(string eventName)
    {
        try
        {
            if (currentEventCvar != null)
                currentEventCvar.Value = eventName;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error setting current event: {ex.Message}");
        }
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && waitingForSelectTeam.Contains(player.Slot))
        {
            waitingForSelectTeam.Remove(player.Slot);
            var kickedPlayer = GetPlayerToKick(player);
            if (kickedPlayer != null)
            {
                PerformKick(kickedPlayer, KickReason.ReservedPlayerJoined);
            }
            else
            {
                SendConsoleMessage(text: $"[Reserved Slots] Selected player is NULL, no one is kicked!", ConsoleColor.Red);
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            if (waitingForSelectTeam.Contains(player.Slot))
                waitingForSelectTeam.Remove(player.Slot);

            if (waitingForKick.ContainsKey(player.Slot))
                waitingForKick.Remove(player.Slot);

            if (reservedPlayers.ContainsKey(player.Slot))
                reservedPlayers.Remove(player.Slot);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && player.SteamID.ToString().Length == 17)
        {
            if (!Config.FlagSets.Any(fs => fs.Flags.Any()))
                return HookResult.Continue;

            int maxPlayers = Server.MaxPlayers;
            var playerReservationInfo = GetPlayerReservationInfo(player);
            if (playerReservationInfo != null)
                SetPlayerReservation(player, playerReservationInfo);
                
            // Calculate total reserved slots for active slot configurations
            int totalReservedSlots = GetTotalActiveReservedSlots();
            
            switch (Config.reservedSlotsMethod)
            {
                case 1: // Hide slots
                    if (GetPlayersCount() > maxPlayers - totalReservedSlots)
                    {
                        if (playerReservationInfo != null)
                        {
                            if ((Config.openSlot && GetPlayersCount() >= maxPlayers) || (!Config.openSlot && GetPlayersCount() > maxPlayers))
                                PerformKickCheckMethod(player);
                        }
                        else
                            PerformKick(player, KickReason.ServerIsFull);
                    }
                    break;
                case 2: // Take slots
                    if (GetPlayersCount() - GetPlayersCountWithReservation() > maxPlayers - totalReservedSlots)
                    {
                        if (playerReservationInfo != null)
                        {
                            if ((Config.openSlot && GetPlayersCount() >= maxPlayers) || (!Config.openSlot && GetPlayersCount() > maxPlayers))
                                PerformKickCheckMethod(player);
                        }
                        else
                            PerformKick(player, KickReason.ServerIsFull);
                    }
                    break;

                default: // Join full
                    if (GetPlayersCount() >= maxPlayers)
                    {
                        if (playerReservationInfo != null)
                            PerformKickCheckMethod(player);
                        else
                            PerformKick(player, KickReason.ServerIsFull);
                    }
                    break;
            }
        }
        return HookResult.Continue;
    }

    public PlayerReservationInfo? GetPlayerReservationInfo(CCSPlayerController player)
    {
        try
        {
            var adminData = AdminManager.GetPlayerAdminData(player);
            if (adminData == null)
                return null;

            var playerFlags = adminData.GetAllFlags();
            if (!playerFlags.Any())
                return null;

            // Check each flag set to find a match, prioritizing by priority value
            foreach (var flagSet in Config.FlagSets.OrderByDescending(fs => fs.Priority))
            {
                var reservedFlags = flagSet.Flags
                    .Where(item => !ulong.TryParse(item, out _))
                    .ToHashSet();

                if (playerFlags.Any(flag => reservedFlags.Contains(flag)))
                {
                    // Check if this flag set has an active slot configuration
                    if (IsSlotConfigurationActive(flagSet.Name))
                    {
                        return new PlayerReservationInfo
                        {
                            FlagSetName = flagSet.Name,
                            Priority = flagSet.Priority,
                            AlwaysImmune = flagSet.AlwaysImmune
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error getting player reservation info: {ex.Message}");
        }

        return null;
    }

    public bool IsSlotConfigurationActive(string flagSetName)
    {
        try
        {
            string currentMap = Server.MapName;
            string currentEvent = GetCurrentEvent();
            
            // Check if map or event has changed, if so clear the cache
            if (currentMap != lastMapName || currentEvent != lastEventName)
            {
                slotActiveCache.Clear();
                lastMapName = currentMap;
                lastEventName = currentEvent;
            }
            
            // Check if result is cached
            if (slotActiveCache.TryGetValue(flagSetName, out bool isActive))
                return isActive;
                
            var slotConfig = Config.SlotConfigurations.FirstOrDefault(sc => sc.FlagSetName == flagSetName);
            if (slotConfig == null)
            {
                slotActiveCache[flagSetName] = false;
                return false;
            }
                
            // Check if this slot is enabled via FakeConVar
            if (slotEnabledCvars.TryGetValue(flagSetName, out var enabledCvar) && enabledCvar.Value == 0)
            {
                slotActiveCache[flagSetName] = false;
                return false;
            }
                
            // Check map-based activation using FakeConVars
            if (slotEnabledMapsCvars.TryGetValue(flagSetName, out var enabledMapsCvar))
            {
                string enabledMapsStr = enabledMapsCvar.Value;
                if (!string.IsNullOrEmpty(enabledMapsStr))
                {
                    var enabledMaps = enabledMapsStr.Split(',').Select(m => m.Trim()).ToList();
                    if (enabledMaps.Any() && !enabledMaps.Contains(currentMap, StringComparer.OrdinalIgnoreCase))
                    {
                        slotActiveCache[flagSetName] = false;
                        return false;
                    }
                }
            }
            
            if (slotDisabledMapsCvars.TryGetValue(flagSetName, out var disabledMapsCvar))
            {
                string disabledMapsStr = disabledMapsCvar.Value;
                if (!string.IsNullOrEmpty(disabledMapsStr))
                {
                    var disabledMaps = disabledMapsStr.Split(',').Select(m => m.Trim()).ToList();
                    if (disabledMaps.Contains(currentMap, StringComparer.OrdinalIgnoreCase))
                    {
                        slotActiveCache[flagSetName] = false;
                        return false;
                    }
                }
            }
            
            // Check event-based activation using FakeConVars
            if (!string.IsNullOrEmpty(currentEvent))
            {
                if (slotEnabledEventsCvars.TryGetValue(flagSetName, out var enabledEventsCvar))
                {
                    string enabledEventsStr = enabledEventsCvar.Value;
                    if (!string.IsNullOrEmpty(enabledEventsStr))
                    {
                        var enabledEvents = enabledEventsStr.Split(',').Select(e => e.Trim()).ToList();
                        if (enabledEvents.Any() && !enabledEvents.Contains(currentEvent, StringComparer.OrdinalIgnoreCase))
                        {
                            slotActiveCache[flagSetName] = false;
                            return false;
                        }
                    }
                }
                
                if (slotDisabledEventsCvars.TryGetValue(flagSetName, out var disabledEventsCvar))
                {
                    string disabledEventsStr = disabledEventsCvar.Value;
                    if (!string.IsNullOrEmpty(disabledEventsStr))
                    {
                        var disabledEvents = disabledEventsStr.Split(',').Select(e => e.Trim()).ToList();
                        if (disabledEvents.Contains(currentEvent, StringComparer.OrdinalIgnoreCase))
                        {
                            slotActiveCache[flagSetName] = false;
                            return false;
                        }
                    }
                }
            }
            
            // If we got here, the slot is active
            slotActiveCache[flagSetName] = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error checking if slot configuration is active: {ex.Message}");
            return false;
        }
    }

    public int GetTotalActiveReservedSlots()
    {
        try
        {
            return Config.SlotConfigurations
                .Where(sc => IsSlotConfigurationActive(sc.FlagSetName))
                .Sum(sc => sc.SlotCount);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error getting total active reserved slots: {ex.Message}");
            return 0;
        }
    }

    public void SetPlayerReservation(CCSPlayerController player, PlayerReservationInfo info)
    {
        if (reservedPlayers.ContainsKey(player.Slot))
            return;

        reservedPlayers.Add(player.Slot, info);
    }

    public void PerformKickCheckMethod(CCSPlayerController player)
    {
        switch (Config.kickCheckMethod)
        {
            case 1:
                if (!waitingForSelectTeam.Contains(player.Slot))
                    waitingForSelectTeam.Add(player.Slot);
                break;
            default:
                var kickedPlayer = GetPlayerToKick(player);
                if (kickedPlayer != null)
                {
                    PerformKick(kickedPlayer, KickReason.ReservedPlayerJoined);
                }
                else
                {
                    SendConsoleMessage(text: $"[Reserved Slots] Selected player is NULL, no one is kicked!", ConsoleColor.Red);
                }
                break;
        }
    }

    public void PerformKick(CCSPlayerController? player, KickReason reason)
    {
        if (player == null || !player.IsValid)
            return;

        var name = player.PlayerName;
        var steamid = player.SteamID.ToString();
        if (Config.kickDelay > 1)
        {
            var slot = player.Slot;
            waitingForKick.Add(slot, reason);
            AddTimer(Config.kickDelay, () =>
            {
                player = Utilities.GetPlayerFromSlot(slot);
                if (player != null && player.IsValid)
                {
                    player.Disconnect((CounterStrikeSharp.API.ValveConstants.Protobuf.NetworkDisconnectionReason)Config.kickReason);
                    LogMessage(name, steamid, reason);
                }

                if (waitingForKick.ContainsKey(slot))
                    waitingForKick.Remove(slot);

            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        else
        {
            player.Disconnect((CounterStrikeSharp.API.ValveConstants.Protobuf.NetworkDisconnectionReason)Config.kickReason);
            LogMessage(name, steamid, reason);
        }
    }

    public void LogMessage(string name, string steamid, KickReason reason)
    {
        string message = reason == KickReason.ServerIsFull
            ? $"Player {name} ({steamid}) was kicked, because the server is full."
            : $"Player {name} ({steamid}) was kicked, because player with a reservation slot joined.";
        
        string localizerKey = reason == KickReason.ServerIsFull
            ? "Chat.PlayerWasKicked.ServerIsFull"
            : "Chat.PlayerWasKicked.ReservedPlayerJoined";
        
        if (Config.logKickedPlayers)
            Logger.LogInformation(message);
        
        if (Config.displayKickedPlayers == 1)
            Server.PrintToChatAll(Localizer[localizerKey, name]);
        else if (Config.displayKickedPlayers == 2)
        {
            foreach (var admin in Utilities.GetPlayers().Where(p => AdminManager.PlayerHasPermissions(p, "@css/generic")))
            {
                admin.PrintToChat(Localizer[localizerKey, name]);
            }
        }
    }

    private CCSPlayerController? GetPlayerToKick(CCSPlayerController client)
    {
        try
        {
            var allPlayers = Utilities.GetPlayers();
            
            // First, check if we should prioritize kicking spectators
            if (Config.kickPlayersInSpectate)
            {
                var specPlayers = allPlayers
                    .Where(p => !p.IsBot && !p.IsHLTV && p.PlayerPawn.IsValid && 
                               p.Connected == PlayerConnectedState.PlayerConnected && 
                               p.SteamID.ToString().Length == 17 && p != client && 
                               (p.Team == CsTeam.None || p.Team == CsTeam.Spectator) &&
                               IsPlayerKickable(p))
                    .ToList();
                    
                if (specPlayers.Count > 0)
                {
                    // If there are spectators, pick one based on the kick type
                    return SelectPlayerByKickType(specPlayers);
                }
            }
            
            // If no spectators or not prioritizing them, select from all players
            var playersList = allPlayers
                .Where(p => !p.IsBot && !p.IsHLTV && p.PlayerPawn.IsValid && 
                           p.Connected == PlayerConnectedState.PlayerConnected && 
                           p.SteamID.ToString().Length == 17 && p != client &&
                           IsPlayerKickable(p))
                .ToList();

            if (!playersList.Any())
                return null;

            return SelectPlayerByKickType(playersList);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error getting player to kick: {ex.Message}");
            return null;
        }
    }
    
    private bool IsPlayerKickable(CCSPlayerController player)
    {
        try
        {
            // Player is kickable if they don't have reservation
            if (!reservedPlayers.ContainsKey(player.Slot))
                return true;
                
            var playerInfo = reservedPlayers[player.Slot];
            
            // Players with AlwaysImmune are never kicked
            if (playerInfo.AlwaysImmune)
                return false;
                
            // Check if the player's flag set is still active
            if (!IsSlotConfigurationActive(playerInfo.FlagSetName))
                return true;
                
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error checking if player is kickable: {ex.Message}");
            return false;
        }
    }
    
    private CCSPlayerController? SelectPlayerByKickType(List<CCSPlayerController> players)
    {
        try
        {
            if (!players.Any())
                return null;
                
            // Sort players by priority (lower priority gets kicked first)
            players = players.OrderBy(p => 
            {
                if (!reservedPlayers.ContainsKey(p.Slot))
                    return -1; // Non-reserved players get kicked first
                    
                return reservedPlayers[p.Slot].Priority;
            }).ToList();
            
            // Get the lowest priority players
            int lowestPriority = players.First().Slot < 0 || !reservedPlayers.ContainsKey(players.First().Slot) 
                ? -1 
                : reservedPlayers[players.First().Slot].Priority;
                
            var lowestPriorityPlayers = players.Where(p => 
                !reservedPlayers.ContainsKey(p.Slot) || 
                reservedPlayers[p.Slot].Priority == lowestPriority
            ).ToList();
            
            // Apply kick type to the lowest priority players
            switch (Config.kickType)
            {
                case (int)KickType.HighestPing:
                    return lowestPriorityPlayers.OrderByDescending(p => p.Ping).FirstOrDefault();
                    
                case (int)KickType.HighestScore:
                    return lowestPriorityPlayers.OrderByDescending(p => p.Score).FirstOrDefault();
                    
                case (int)KickType.LowestScore:
                    return lowestPriorityPlayers.OrderBy(p => p.Score).FirstOrDefault();
                    
                default: // Random
                    return lowestPriorityPlayers.OrderBy(_ => Guid.NewGuid()).FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error selecting player by kick type: {ex.Message}");
            return null;
        }
    }

    private static int GetPlayersCount()
    {
        return Utilities.GetPlayers().Count(p => !p.IsHLTV && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected && p.SteamID.ToString().Length == 17);
    }

    private int GetPlayersCountWithReservation()
    {
        return Utilities.GetPlayers().Count(p => !p.IsHLTV && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected && 
                                               p.SteamID.ToString().Length == 17 && reservedPlayers.ContainsKey(p.Slot));
    }

    private static void SendConsoleMessage(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
