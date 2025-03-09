<p align="center">
  <b>Flex Reserved Slots</b> is a CS2 plugin that is used to reserve slots for VIP players or Admins,<br>
  <b>And we can do now more Flexible configuration!</b><br>
  Designed for <a href="https://github.com/roflmuffin/CounterStrikeSharp">CounterStrikeSharp</a> framework<br>
  Original was made by <a href="https://github.com/NockyCZ/CS2-ReservedSlots">NockyCZ/CS2-ReservedSlots</a> ♡<br>
</p>

> [!NOTE]:
> This plugin Version 2.0.0 is not backward compatible with NockyCZ's versions. You will need to update your configuration file to the new format.  
> And from this version, No longer supported Discord now.  

> [!CAUTION]
> Not yet fully tested, use with caution.

> [!WARNING]
> There is no release yet.

### Installation
1. Download the lastest release https://github.com/2vg/CS2-FlexReservedSlots/releases/latest
2. Unzip into your servers `csgo/addons/counterstrikesharp/plugins/` dir
3. Restart the server

## Configuration
```configs/plugins/FlexReservedSlots/FlexReservedSlots.json```

### New in v2.0.0

Version 2.0.0 introduces a completely redesigned Reserved Slots system with the following features:

1. **Multiple Flag Sets**: Define different sets of flags with varying priority levels
2. **Slot Configurations**: Configure multiple slot types with different flag sets
3. **Map and Event-based Activation**: Enable/disable slots based on specific maps or like while an event
4. **Priority System**: Players with higher priority are less likely to be kicked
5. **Always Immune**: Flag sets can be set to never be kicked
6. **Dynamic Configuration via ConVars**: Change some settings on-the-fly without editing config files

### Configuration Format

<details>

<summary>Click to view</summary>

```json
{
  "Flag Sets": [
    {
      "Name": "Admin",
      "Flags": ["@css/ban", "@css/admin"],
      "Priority": 100,
      "AlwaysImmune": true
    },
    {
      "Name": "VIPTier1",
      "Flags": ["@css/vip_tier1"],
      "Priority": 50,
      "AlwaysImmune": false
    },
    {
      "Name": "VIPTier2",
      "Flags": ["@css/vip_tier2"],
      "Priority": 30,
      "AlwaysImmune": false
    }
  ],
  "Slot Configurations": [
    {
      "FlagSetName": "Admin",
      "SlotCount": 1
    },
    {
      "FlagSetName": "VIPTier1",
      "SlotCount": 3,
      "DisabledMaps": ["de_dust2_event"]
    },
    {
      "FlagSetName": "VIPTier2",
      "SlotCount": 5,
      "EnabledMaps": ["de_dust2", "de_mirage", "de_inferno"],
      "DisabledEvents": ["tournament"]
    }
  ],
  "Reserved Slots Method": 0,
  "Leave One Slot Open": false,
  "Kick Reason": 135,
  "Kick Delay": 5,
  "Kick Check Method": 0,
  "Kick Type": 0,
  "Kick Players In Spectate": true,
  "Log Kicked Players": true,
  "Display Kicked Players Message": 2
}
```

</details>

### Configuration Options

#### Flag Sets

| Option | Description |
|--------|-------------|
| `Name` | Unique name for the flag set |
| `Flags` | List of flags that belong to this set |
| `Priority` | Priority level (higher number = higher priority) |
| `AlwaysImmune` | If true, players with these flags will never be kicked |

#### Slot Configurations

| Option | Description |
|--------|-------------|
| `FlagSetName` | Name of the flag set this slot configuration uses |
| `SlotCount` | Number of slots reserved for this configuration |
| `EnabledMaps` | List of maps where this slot is enabled (empty = all maps) |
| `DisabledMaps` | List of maps where this slot is disabled |
| `EnabledEvents` | List of events where this slot is enabled (empty = all events) |
| `DisabledEvents` | List of events where this slot is disabled |

#### General Options

| Option | Description |
|--------|-------------|
| `Reserved Slots Method` | `0` - There will always be one slot open. For example, if your maxplayers is set to 10, the server can have a maximum of 9 players. If a 10th player joins with a Reservation flag/role, it will kick a player based on the Kick type. If the 10th player doesn't have a reservation flag/role, they will be kicked |
| | `1` - Maintains the number of available slots according to the reservation slots setting, allowing only players with a Reservation flag/role to join. For example, if you have maxplayers set to 10 and Reserved slots set to 3, when there are 7/10 players on the server, additional players can only join if they have a Reservation flag/role. If they don't, they will be kicked. If the server is already full and a player with a Reservation flag/role attempts to join, it will kick a player based on the Kick type |
| | `2` - It works the same way as in method 2, except players with a Reservation flag/role are not counted towards the total player count. For example, if there are 7/10 players on the server, and Reserved slots are set to 3. Out of those 7 players, two players have a Reservation flag/role. The plugin will then consider that there are 5 players on the server, allowing two more players without a Reservation flag/role to connect. If the server is already full and a player with a Reservation flag/role attempts to join, it will kick a player based on the Kick type |
| `Leave One Slot Open` | Works only if reserved slots method is set to 1 or 2. If set to `true`, there will always be one slot open. (`true` or `false`) |
| `Kick Reason` | Reason for the kick (Use the number from [NetworkDisconnectionReason](https://docs.cssharp.dev/api/CounterStrikeSharp.API.ValveConstants.Protobuf.NetworkDisconnectionReason.html?q=NetworkDisconnectionReason)) |
| `Kick Delay` | This means that the player will be kicked after a certain time (`seconds`) (value less than 1 means the player will be kicked immediately) |
| `Kick Check Method` | When a player will be selected for kick when a player with a Reserved flag/role joins? |
| | `0` - When a player with a Reserved flag/role joins |
| | `1` - When a player with a Reserved flag/role choose a team |
| `Kick Type` | How is a players selected to be kicked? |
| | `0` - Players will be kicked randomly |
| | `1` - Players will be kicked by highest ping |
| | `2` - Players will be kicked by highest score |
| | `3` - Players will be kicked by lowest score |
| `Kick Players In Spectate` | Kick players who are in spectate first? (`true` or `false`) |
| `Log Kicked Players` | (`true` or `false`) |
| `Display Kicked Players Message` | Who will see the message when a player is kicked due to a reserved slot |
| | `0` - None |
| | `1` - All players |
| | `2` - Only Admins with the `@css/generic` flag |

### Commands

| Command | Description |
|---------|-------------|
| `css_event <event_name>` | Set the current event (e.g., "tournament") |
| `css_event none` | Clear the current event |
| `css_rs_status` | Show the status of all slot configurations |

### ConVar Integration

The plugin exposes ConVars that can be modified by other plugins or through the server console:

| ConVar | Description |
|--------|-------------|
| `css_rs_current_event` | Current event name |
| `css_rs_slot_<name>_enabled` | Enable/disable a slot configuration (1=enabled, 0=disabled) |
| `css_rs_slot_<name>_maps` | Comma-separated list of maps where the slot is enabled |
| `css_rs_slot_<name>_disabled_maps` | Comma-separated list of maps where the slot is disabled |
| `css_rs_slot_<name>_events` | Comma-separated list of events where the slot is enabled |
| `css_rs_slot_<name>_disabled_events` | Comma-separated list of events where the slot is disabled |

### Examples

#### Basic Configuration
reservedFlags

<details>

<summary>Click to view</summary>

```json
{
  "Flag Sets": [
    {
      "Name": "Admin",
      "Flags": ["@css/ban", "@css/admin"],
      "Priority": 100,
      "AlwaysImmune": true
    },
    {
      "Name": "VIP",
      "Flags": ["@css/vip"],
      "Priority": 50,
      "AlwaysImmune": false
    }
  ],
  "Slot Configurations": [
    {
      "FlagSetName": "Admin",
      "SlotCount": 2
    },
    {
      "FlagSetName": "VIP",
      "SlotCount": 3
    }
  ],
  "Reserved Slots Method": 1,
  "Leave One Slot Open": true
}
```

</details>

#### Advanced Configuration with Map and Event Restrictions

<details>

<summary>Click to view</summary>

```json
{
  "Flag Sets": [
    {
      "Name": "Admin",
      "Flags": ["@css/ban", "@css/admin"],
      "Priority": 100,
      "AlwaysImmune": true
    },
    {
      "Name": "VIPGold",
      "Flags": ["@css/vip_gold"],
      "Priority": 80,
      "AlwaysImmune": false
    },
    {
      "Name": "VIPSilver",
      "Flags": ["@css/vip_silver"],
      "Priority": 50,
      "AlwaysImmune": false
    },
    {
      "Name": "VIPBronze",
      "Flags": ["@css/vip_bronze"],
      "Priority": 30,
      "AlwaysImmune": false
    }
  ],
  "Slot Configurations": [
    {
      "FlagSetName": "Admin",
      "SlotCount": 2
    },
    {
      "FlagSetName": "VIPGold",
      "SlotCount": 3,
      "DisabledEvents": ["tournament"]
    },
    {
      "FlagSetName": "VIPSilver",
      "SlotCount": 5,
      "EnabledMaps": ["de_dust2", "de_mirage", "de_inferno"]
    },
    {
      "FlagSetName": "VIPBronze",
      "SlotCount": 2,
      "EnabledMaps": ["de_dust2", "de_mirage"],
      "DisabledEvents": ["tournament", "special_event"]
    }
  ],
  "Reserved Slots Method": 2,
  "Leave One Slot Open": true,
  "Kick Type": 1,
  "Kick Players In Spectate": true
}
```

</details>

### Supported Languages

The plugin currently supports the following languages:
- English (en.json)
- Portuguese-Brazil (pt-BR.json)

You can contribute additional translations by adding new language files to the `lang/` directory.
