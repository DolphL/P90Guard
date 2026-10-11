# P90Guard

Server-side anti-cheat for **SCP: Project 90** dedicated servers (BepInEx 6, IL2CPP).

[Русская версия](README.ru.md)

The game trusts the client with many network commands: giving items, setting HP, "I am dead", teleporting to a spawn point, admin-panel commands, placing sandbox props, triggering landmines. Mirror executes them on the server without any check, so a modified client can do all of that. P90Guard checks these commands on the server and blocks the ones a normal client can never send.

- Standalone: depends only on BepInEx and the game. Works next to any other plugin framework.
- Every check can be set to `Off`, `Log` or `Block` separately.
- Optional points, automatic kick and ban, its own ban list, Discord or JSON webhook.
- Public API (events) for websites, bots and panels, usable through reflection.
- Fixes two game bugs on dedicated servers: exploded grenades and landmines are never destroyed on the server.

> **Status: 1.0.0.** All checks are based on the game code (the exact commands a vanilla client sends and when). They were developed and run on one server. Watch the log for a few days before enabling automatic punishments.

## Supported game version

Game names are obfuscated and change with every game update. P90Guard is mapped to one exact build:

| P90Guard | Game | GameAssembly.dll SHA-256 |
|---|---|---|
| 1.0.0 | 0.4.2 [F05] | `ba69d16edce7b6fb2362823b8d7285b9905105c648289f0f25cbccdb9973164a` |

On start the plugin hashes `GameAssembly.dll`. On an unknown build it **does not patch anything** (with the default `GameHashCheck: "safe"`) and prints the hash to the log. That way a game update can never make the anti-cheat block the wrong commands.

## Installation

1. Install [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) on the dedicated server and start it once.
2. Put `P90Guard.dll` into `BepInEx/plugins/`.
3. Start the server. The config is created at `BepInEx/config/P90Guard.json`.

The plugin works only when the game runs with `-batchmode` (a dedicated server). On a client it does nothing.

Log lines look like this:

```
[P90Guard] BLOCKED add_item: Nick (7656119..., Class-D) - gave themselves "Keycard O5", which is not in the Class-D loadout
[P90Guard] SUSPICIOUS cuff_far: Nick (7656119..., MTF) - cuffed Other from 41 m away
```

They go to the BepInEx console and log and, with `LogToFile`, to `BepInEx/P90Guard/logs/YYYY-MM-DD.log`.

## Checks

| Check | What it catches | Default |
|---|---|---|
| `steamid_spoof` | The client reports someone else's SteamID (compared with the Steam P2P transport ID, which cannot be faked). Used to steal admin ranks and badges. | Block, instant kick |
| `role_spoof` | The client requests the role, badge or admin tools of another SteamID. | Block, instant kick |
| `admin_cmd` | Round lock on/off, round mode switch, map regeneration from a non-admin. These commands have no permission check in the game. In Block mode it also silently stops non-admins from seeing other players' inventories. | Block |
| `admin_action` | Built-in admin panel action without a rank. The game refuses it anyway, so this only reports. | Log |
| `fake_server` | The "I am the dedicated server" service command from a client. | Log |
| `add_item` | Giving yourself items. A vanilla client does it only right after a class change and only the items of the class loadout. Also catches giving items to another player. | Block |
| `clear_inv` | Clearing your inventory without a class change, or someone else's inventory. | Block |
| `set_hp` | Setting your own HP to anything other than the new class max HP (once per class change). | Block |
| `set_dead` | Setting "dead = true/false" without a matching class change (god mode, fake death). | Block |
| `set_ammo` | Giving yourself the ammo of a class you did not just become. | Block |
| `spawn_tp` | Using the spawn-position commands without a class change (teleport). | Block |
| `force_spectator` | Forcing another player into spectators. | Block |
| `door_close` | Closing a door remotely during the round (the command has no owner or distance check). | Block |
| `spawn_tool` | Using the sandbox SpawnTool commands (place, delete, move, rotate, scale props) without holding the tool. Every placed prop is logged with the player name. | Block |
| `landmine_far` | Triggering a landmine with the explosion point far from the mine (a grenade anywhere on the map). | Block |
| `cuff_far` | Cuffing a player farther than `Cuff.MaxDistance` (the game checks it only on the client). | Log |
| `use_missing` | Healing with a medkit or SCP-458, or throwing a grenade, without having it. | Log |
| `heal_spam` | Medkit heals faster than the heal duration allows. | Log |

How the spawn checks work: every class change on the server grants the player exactly the commands a vanilla client sends for it. A spectator gets 1 position, 1 HP, 1 clear, 1 "dead", 1 delayed position. Any other class gets 1 position, 1 HP, 2 clears, 1 "alive", 2 ammo and one AddItem per loadout item. The permissions expire after 120 s. Class changes within 60 s add up, because the previous class's coroutine may still be sending. Admins and server-side plugins that run these commands themselves are not affected.

A newly joined player is only reported during `JoinGraceSeconds`: never blocked, no points, no kick. The spoof checks are the exception.

Points, bans and `ExemptSteamIds` use only the SteamID from the Steam transport, never the one the client reports.

## Configuration

`BepInEx/config/P90Guard.json`. The file is **reloaded automatically** a few seconds after you save it, no restart needed. New checks from plugin updates are added to your file automatically with their defaults. JSON comments are allowed (the plugin rewrites the file, dropping comments, only when it adds new checks).

```jsonc
{
  "Enabled": true,
  "GameHashCheck": "safe",          // safe | warn | off
  "AllowedGameHashes": [],          // extra GameAssembly.dll hashes you trust
  "ExemptMinAdminRank": 1,          // admin rank (ServerAdmins.json) >= this is not checked; 0 = check everyone
  "ExemptAdminToolsAccess": true,   // players with the built-in admin tools are not checked
  "ExemptSteamIds": [],             // SteamID64 strings that are never checked
  "JoinGraceSeconds": 30,           // a loading client is only reported, never blocked
  "NoSteamIdGraceSeconds": 60,
  "RepeatLogSeconds": 10,           // same report for the same player not more often than this
  "LogToFile": true,

  "Checks": {
    "add_item": { "Mode": "Block", "Points": 5, "InstantKick": false },
    "cuff_far": { "Mode": "Log",   "Points": 3, "InstantKick": false }
    // ... every check from the table above
  },

  "Punishments": {
    "Enabled": false,               // automatic kick/ban by points. Off by default!
    "WindowMinutes": 10,            // points are summed over this sliding window
    "KickAtPoints": 15,             // 0 = never
    "BanAtPoints": 40,              // 0 = never
    "BanMinutes": 1440,             // 0 = permanent
    "BanAfterKicks": 3,             // the N-th kick within KickMemoryHours becomes a ban (0 = off)
    "KickMemoryHours": 6
  },

  "Webhook": {
    "Url": "",                      // Discord webhook URL or any HTTP endpoint; empty = off
    "Format": "discord",            // discord | json
    "OnlyBlockedAndPunishments": true,
    "MaxPerMinute": 20,
    "DiscordMention": "",           // e.g. "<@&123456789>"
    "ServerName": "SCP: Project 90 server"
  },

  "Landmine": { "MaxDistance": 5 },
  "Cuff": { "MaxDistance": 12 },

  "Fixes": {
    "DestroyExplodedGrenades": true,  // set to false if another plugin already fixes it
    "DestroyExplodedLandmines": true
  },

  "Messages": {
    "KickSpoof": "Anti-cheat: identity spoofing",
    "Kick": "Anti-cheat: kicked ({reason})",
    "Ban": "Anti-cheat: banned until {until} ({reason})",
    "BanPermanent": "Anti-cheat: permanently banned ({reason})"
  },

  "DisabledPatches": []             // Harmony patch class names to skip (troubleshooting)
}
```

### Check modes

- `Off`: the check does nothing.
- `Log`: the command is executed, the player is reported and gets points.
- `Block`: the command is **not** executed, the player is reported and gets points.

`InstantKick: true` kicks on the first hit regardless of points (with punishments disabled too).

### Recommended rollout

1. Run with the defaults (`Punishments.Enabled: false`) for a few days and read the log.
2. If a check fires on honest players, set it to `Log` or `Off` and open an issue with the log line.
3. Then enable `Punishments`.

## Ban list

`BepInEx/config/P90Guard.bans.json` holds the anti-cheat's own bans. A banned player is kicked within about 2 seconds of connecting. The SteamID comes from the transport, so it cannot be faked. To unban, delete the entry and save the file; it is re-read automatically. If the file has a syntax error, the plugin logs it and never overwrites the file until you fix it. You can also add bans by hand:

```json
[
  { "SteamId": "76561198000000000", "Nick": "cheater", "Reason": "manual", "By": "admin",
    "Created": "2026-10-01T12:00:00Z", "Until": null }
]
```

`Until` is UTC; `null` means permanent.

## API for other plugins

`P90Guard.Api` uses only primitive types, so you can subscribe through reflection without referencing P90Guard:

```csharp
var api = Type.GetType("P90Guard.Api, P90Guard");
api?.GetEvent("Flagged")?.AddEventHandler(null,
    new Action<IReadOnlyDictionary<string, object>>(d =>
        Log.LogInfo($"{d["nick"]} {d["check"]} blocked={d["blocked"]}: {d["details"]}")));
```

- `Flagged` fires on every reported detection (after the `RepeatLogSeconds` rate limit). Keys: `steam_id` (ulong), `nick`, `role`, `check`, `details`, `blocked` (bool), `count` (int), `points` (int).
- `Punished` fires on kick or ban. Keys: `steam_id`, `nick`, `action` (`"kick"` / `"ban"`), `reason`.
- `IsBanned(ulong steamId)`, `GetPoints(ulong steamId)`, `Version`.

Handlers run on the main thread, so keep them fast. Load order does not matter if you subscribe a few seconds after start or retry.

## Building

Requirements: .NET 6 SDK or newer, and a game folder with BepInEx 6 IL2CPP that has been started once (so `BepInEx/interop` exists).

```
dotnet build -c Release -p:GameDir="C:\path\to\SCP Project 90"
```

Without `-p:GameDir` the build looks in `./libs` (same layout: `libs/BepInEx/core`, `libs/BepInEx/interop`). Game and BepInEx DLLs are not part of this repository.

### Porting to a new game version

All obfuscated names are in `src/P90Guard/Core/Game.cs`. After a game update:

1. Regenerate the interop assemblies (start the server with BepInEx once).
2. Find the new names of the methods listed in `Game.cs` (for example with Il2CppDumper or Cpp2IL plus dnSpy). The `Invoke...` methods are Mirror command invokers with the signature `(NetworkBehaviour, NetworkReader, NetworkConnectionToClient)`.
3. Update the constants, add the new `GameAssembly.dll` hash to `GameVersion.KnownHashes`, build and test every check.

## Limitations

- Only commands are checked. Movement and aim (speedhack, noclip, aimbot) are client-authoritative in this game and are not covered.
- `use_missing` and `heal_spam` are `Log` by default: the game can remove a consumable a moment before the use command arrives.
- A server-side plugin that sends client commands on behalf of players from outside the normal server call path may need `DisabledPatches` or `Off` for that check.

## License

[GPL-3.0](LICENSE). SCP: Project 90 and the SCP Foundation are the property of their respective owners. This project is not affiliated with the game developers.
