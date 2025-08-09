# ProximityScanner

**Version:** 2.3.0
**Author:** Skillu 
**Game:** Rust (uMod / Oxide)
**Type:** Server plugin (command-only)

---

## Overview

ProximityScanner adds a single chat command that tells players whether any other players are within a configurable radius.  
It does not reveal identities, positions, or exact counts—just a simple “detected / clear” result that’s fair and privacy-friendly.

- Works out of the box for everyone (no permissions required by default).
- Optional permission tiers let you give VIPs different radius/cooldown.
- Flexible filters (ignore teammates/admins/safezone/sleepers/dead/NPCs).
- Admin logging (file + console) and an admin test command.
- **Localization via uMod Lang API** with optional message overrides in config.

---

## Features

- `/scan` returns a boolean presence check within a radius.
- Configurable radius and cooldown (defaults: 100 m, 10 minutes).
- Per-player cooldown; optional tier overrides per permission.
- Filters:
  - `IgnoreTeammates`
  - `IgnoreAdmins`
  - `IgnoreSafezone`
  - `IgnoreSleeping`
  - `IgnoreDead`
  - `IgnoreNPC`
- Logs each scan (anonymized) to `oxide/logs/ProximityScanner*.txt`.
- Admin test command: simulate scanning from another player’s position.
- Messages are served via **Lang API** (English by default).

---

## Quick Start

1. Place `ProximityScanner.cs` in `oxide/plugins/`.
2. Restart the server or run:
```
oxide.reload ProximityScanner
```
3. Players can use `/scan` immediately.

> By default, no extra permissions are needed. If you want to gate usage, set `RequireUsePermission` to `true` in the config and grant `proximityscanner.use`.

---

## Commands

### Player
```
/scan
```
Replies with:
- `⚠️ Players detected nearby!` or
- `✅ Area is clear.`

Also informs about cooldown start and time remaining if it’s used early, and notifies when cooldown ends.

### Admin
```
/scan.for <steamIdOrName>
```
Simulates a scan from the target player’s position using the caller’s tier settings.  
Does not consume cooldown and does not affect the target. Useful for verification and testing.

---

## Permissions (optional)

| Permission                | Purpose                                                             |
|---------------------------|---------------------------------------------------------------------|
| `proximityscanner.use`    | Only needed if `RequireUsePermission: true`.                        |
| `proximityscanner.admin`  | Allows `/scan.for` and future admin tools.                          |
| `proximityscanner.tier1`  | Example tier: override radius/cooldown. Priority decides the winner.|
| `proximityscanner.tier2`  | Example tier with higher priority.                                  |
| `proximityscanner.tier3`  | Example tier with highest priority.                                 |

Grant examples:
```
oxide.grant group vip proximityscanner.tier2
oxide.grant user <steamId> proximityscanner.admin
```

---

## Localization

All player/admin messages are provided through the **uMod Lang API** (English by default).  
You can also set message overrides in the plugin config under `Msg`. On load, the plugin registers those overrides into the English locale so you can customize text without editing the code. Translations for other languages can be added by placing language files in `oxide/lang/` as usual.

---

## Configuration

File: `oxide/config/ProximityScanner.json` (generated on first run)

### Keys

- `RequireUsePermission` — set to `true` to restrict `/scan` to users with `proximityscanner.use`.
- `DefaultScanRadius` — meters.
- `DefaultCooldownSeconds` — seconds.
- `Filter` — booleans for common exclusions.
- `Log` — enable/disable logging and tweak details.
- `Msg` — message text overrides; pushed into Lang on load (English locale).
- `PermissionTiers` — optional override sets keyed by permissions. Highest `Priority` among a player’s granted tiers wins (overrides both radius and cooldown).

### Example config
```json
{
  "RequireUsePermission": false,
  "DefaultScanRadius": 100.0,
  "DefaultCooldownSeconds": 600.0,
  "Filter": {
    "IgnoreTeammates": true,
    "IgnoreAdmins": true,
    "IgnoreSafezone": true,
    "IgnoreSleeping": true,
    "IgnoreDead": true,
    "IgnoreNPC": true
  },
  "Log": {
    "Enabled": true,
    "EchoToConsole": true,
    "IncludePosition": true,
    "IncludeCount": false,
    "FileName": "ProximityScanner"
  },
  "Msg": {
    "Detected": "⚠️ Players detected nearby!",
    "Clear": "✅ Area is clear.",
    "CooldownStarted": "🔁 Cooldown started. You can scan again in {time}.",
    "CooldownLeft": "⏳ Scan is on cooldown. Time left: {time}.",
    "CooldownEnded": "✅ Cooldown expired. You can scan again.",
    "NoPermission": "You do not have permission to use this.",
    "AdminOnly": "This command is for admins only.",
    "NotFound": "Target player not found (online).",
    "TestHeader": "Test scan from {name}:"
  },
  "PermissionTiers": [
    {
      "Permission": "proximityscanner.tier1",
      "ScanRadius": 125.0,
      "CooldownSeconds": 480.0,
      "Priority": 1
    },
    {
      "Permission": "proximityscanner.tier2",
      "ScanRadius": 150.0,
      "CooldownSeconds": 360.0,
      "Priority": 2
    },
    {
      "Permission": "proximityscanner.tier3",
      "ScanRadius": 175.0,
      "CooldownSeconds": 240.0,
      "Priority": 3
    }
  ]
}
```

---

## Logging

- File path: `oxide/logs/ProximityScanner*.txt`
- Each line includes: mode (user/test), actor name, SteamID, radius, result, cooldown, and optionally position and match count.
- Example:
```
mode=user | actor="Alice" | steamid=76561198... | radius=150 | result=detected | cooldown=360s | pos=(123.4,45.6,789.0)
```
Logs never include identities of detected players.

---

## Notes & Compatibility

- The plugin provides a boolean proximity check only. No names, no positions, no distances for players.
- Intended for fair-play servers; configurable filters avoid noisy or unfair detections.
- Works with vanilla and most modded environments. If another plugin changes how NPC/admin flags are reported, adjust filters accordingly.

---

## Performance

- A single scan loops over `BasePlayer.activePlayerList`.
- With default cooldowns and typical populations, overhead is negligible.
- If you run very short cooldowns on high-pop servers, consider raising the cooldown or tightening filters.

---

## Changelog

**2.3.0**
- Lang API for all player/admin messages (EN by default).
- Config `Msg` overrides are now registered into Lang on load (EN locale).
- Cleanup on `Unload()` and minor polish for uMod submission guidelines.

**2.2.0**
- Public command-only release. Filters, per-player cooldown, permission tiers.
- Admin logs and `/scan.for` test command.

---

## License

This project is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**.  
You may use, run, and modify the plugin for any purpose, including on commercial servers.  
If you distribute modified versions, you must keep the copyright and license notices and provide the source of the modified files under MPL-2.0.  
Full text is included in the `LICENSE` file.