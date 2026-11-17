# Temporal Fallback

Temporal Fallback gives Vintage Story players an unlimited backup spawn for when their travel bed is gone.

## How it works

1. Sleep in a bed to register it with Simple Bed Spawn. You will respawn there while it is valid.
2. Use a temporal gear in the overworld to save a personal refuge. Another successful gear use moves the refuge and preserves your registered bed.
3. If the bed is broken, picked up, or otherwise cleared, respawn at a fresh safe spot within 2,000 blocks of the refuge anchor. The anchor survives restarts, and fallback respawns never use up charges.

Players without a saved refuge use the existing game and bed-mod rules. Everyone can establish a refuge by using a gear after installation. When a search finds no safe ground or runs out of time, the player can retry.

## Requirements and installation

- Vintage Story dedicated server 1.22.7.
- Simple Bed Spawn 1.4.0 (`palmiersamasimplebedspawn`), enabled on the server.
- Clients can join without installing Temporal Fallback.

Back up your world, put `TemporalFallback-1.0.2.zip` in your server data directory's `Mods` folder, and restart. Check `Logs/server-main.log` for `[TemporalFallback] Enabled with Simple Bed Spawn`. The mod uses Simple Bed Spawn's existing settings and bed registration.

Remove the Temporal Fallback ZIP and restart to uninstall. Simple Bed Spawn resumes its usual flow, and saved refuge anchors are dormant.

## Configuration

The server writes `ModConfig/TemporalRefuge.json` on first run. Restart after changing it.

| Setting | Default | Allowed range |
| --- | ---: | ---: |
| `Radius` | 2000 | 1–2000 blocks |
| `SearchAttempts` | 40 | 1–100 per respawn |

The search checks solid support, standing clearance, liquid, common block hazards, and claim access. Hostile mobs can still be nearby. Generation of unexplored terrain can increase search time. Up to two refuge searches can run at once. Additional players can retry shortly.

## Build from source

Use the .NET 10 SDK and the game assemblies from your own Vintage Story 1.22.7 installation.

```sh
dotnet build TemporalRefuge.csproj -c Release -p:VintageStoryPath="/path/to/VintageStory"
```

On Windows the path defaults to `%APPDATA%/Vintagestory`. Put the resulting `bin/Release/net10.0/TemporalRefuge.dll`, `modinfo.json` and this README at the root of the release ZIP. The game and Simple Bed Spawn assemblies are supplied by the installation.

## Compatibility and credits

This release uses the Vintage Story respawn handler and Simple Bed Spawn 1.4.0's registration data. Retest after updating either one or adding another mod that changes respawns. Existing forced role spawns still use the game's own rules.

Design and original handover by **BiggDill**. Implementation and verification assisted by Codex (GPT-6). Uses Harmony supplied by the game.

[Simple Bed Spawn on ModDB](https://mods.vintagestory.at/palmiersamasimplebedspawn) · [Vintage Story API](https://apidocs.vintagestory.at/) · [Harmony](https://github.com/pardeike/Harmony)
