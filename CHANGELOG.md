# Changelog

## 1.2.0

- The network points to the FLX network and connects as soon as you put in an `ApiKey` issued by the network owner (no key = nothing is sent). The console shows at every start what is sent and how to turn it off.
- Reports from servers the network owner has not verified are shown, but never count for automatic Kick/Ban.
- Configs from earlier versions keep `Network.Enabled: false` - set it to true together with the key.

## 1.1.1

- `Api.RunCommand("cheatban ...")`: other plugins (web panels) can run `pg` commands and get the output.
- `pg cheatban` raises the `Punished` event (action `ban`), so panels see admin cheat bans too.

## 1.1.0

- **Cheater network** (off by default): a shared database of cheaters for servers that join a network (`Network` in the config, API key from the network site).
  - `pg cheatban <nick|SteamID> <minutes|perm> <reason>` bans locally and reports to the network with the player's recent anti-cheat lines as evidence.
  - Every joining player is checked; if other servers reported them, the console, log, webhook and in-game admins are warned. `Network.OnReportedJoin`: Log / NotifyAdmins / Kick / Ban (Kick and Ban only with reports from `ActionMinServers` different servers; never for exempt players).
  - `pg unban` revokes this server's report, `pg check` looks a player up, `pg status` shows the connection.
- Server console commands `pg ...`. Protocol: `docs/NETWORK_API.md`.

## 1.0.1

- New fix `Fixes.BlockSpectatorItemUse` (on by default): after death the game client may still think a grenade or medkit is in hand and send "throw" / "heal" on a mouse click while spectating. The server executed it (grenades from nowhere) and it was reported as `use_missing`. These commands from dead players and spectators are now ignored silently.

- `use_missing`: removed the SCP-458 check. The item it compared against is a runtime field of the game, so the check could misfire; the gain for a cheater is 13 HP.
- `use_missing` / `heal_spam`: the medkit check now handles Pills correctly (the player has two Medkit components: Medkit and Pills). Detections name the item.

## 1.0.0

First public release.

- 18 server-side checks: identity spoofing, admin commands without permissions, spawn commands (items, HP, dead state, ammo, spawn teleport, inventory clear, force spectator), remote door closing, sandbox SpawnTool, far landmine triggers, far cuffing, consumables used without the item, heal spam.
- Per-check mode `Off` / `Log` / `Block`, points, optional automatic kick and ban.
- Own ban list (`P90Guard.bans.json`) with timed and permanent bans, hot reload.
- Discord or JSON webhook.
- Public API events `Flagged` / `Punished` for other plugins.
- Game build check by `GameAssembly.dll` SHA-256 (supports SCP: Project 90 0.4.2 [F05]).
- Fixes for dedicated servers: exploded grenades and landmines are destroyed on the server.
