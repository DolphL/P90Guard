# Changelog

## 1.0.0

First public release.

- 18 server-side checks: identity spoofing, admin commands without permissions, spawn commands (items, HP, dead state, ammo, spawn teleport, inventory clear, force spectator), remote door closing, sandbox SpawnTool, far landmine triggers, far cuffing, consumables used without the item, heal spam.
- Per-check mode `Off` / `Log` / `Block`, points, optional automatic kick and ban.
- Own ban list (`P90Guard.bans.json`) with timed and permanent bans, hot reload.
- Discord or JSON webhook.
- Public API events `Flagged` / `Punished` for other plugins.
- Game build check by `GameAssembly.dll` SHA-256 (supports SCP: Project 90 0.4.2 [F05]).
- Fixes for dedicated servers: exploded grenades and landmines are destroyed on the server.
