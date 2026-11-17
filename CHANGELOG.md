# Changelog

## 1.0.1

- Requires Simple Bed Spawn 1.4.0 and uses its existing bed registration and removal rules.
- Keeps a registered travel bed primary when another temporal gear sets a refuge.
- Selects a safe overworld fallback and lets the normal game respawn handler perform the teleport and revival.
- Keeps fallback anchors without a respawn charge limit.
- Limits simultaneous searches and ignores duplicate and stale requests.
