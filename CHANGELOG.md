# Changelog

All notable changes to the realvirtual MCP Server package.

## [Unreleased]

## [1.1.1] - 2026-07-22

### Added
- Unity Recorder is now declared as a package dependency and installs from the Unity Package Registry
- A **Copy MCP JSON** action and copyable README example for configuring non-Claude MCP clients

### Fixed
- Unity 6000.4+ object identifiers use `GetEntityId().ToString()` instead of the deprecated `GetInstanceID()` API, while older supported Unity versions retain a compatibility fallback

## [1.1.0] - 2026-07-12

### Added
- `editor_build` — build a player from a single scene for a target platform
- `video_record_start` / `video_record_stop` / `video_record_status` — MP4 recording of the Game View
- `camera_frame` — frame a GameObject or a position in the Scene or Game camera
- `debug_marker_add` / `debug_marker_clear` — transient visual markers (cross, axes, sphere, arrow) for pointing at positions in the scene
- `editor_get_selection` — the GameObjects currently selected in the Editor
- `editor_scan_missing_scripts` / `editor_remove_missing_scripts` — find and remove MonoBehaviours with missing scripts in loaded scenes and prefab assets
- `unity_kill` / `unity_restart` — Unity process control from the Python server. These run entirely OS-side, so they still work when the Editor is frozen or dead
- Main thread stall detection in `unity_status`: a frozen Editor is now reported as blocked. Previously the WebSocket heartbeat kept answering on a background thread, so a hung Editor still looked healthy
- Parameter aliases: common synonyms (`path`, `target`, `query`, `component`, ...) resolve to the canonical parameter instead of failing with "missing required argument"
- Automatic configuration of the WebViewer MCP bridge when a WebViewer dev build is present, on its own port alongside the Unity server

### Changed
- `component_set` no longer reports success silently. Unknown keys, unresolved references, and type mismatches are returned as errors, and the fields that were actually written come back in `applied`
- Component deserialization accepts `List<T>` in addition to arrays, and matches field names case-insensitively
- Component payloads are serialized through `McpSafeJson`, which handles values that previously broke the JSON response

### Fixed
- The WebSocket port is preserved across domain reloads instead of drifting to a new one

## [1.0.2] - 2026-03-10

### Added
- Domain-specific MCP tools for Drives, Sensors, PLC Signals, MUs, LogicSteps, and Scene Notes (shipped via `io.realvirtual.starter`)
- MCP Server listed as new feature in realvirtual 6.3.0 release notes

### Changed
- Updated documentation and Asset Store links

## [1.0.1] - 2026-03-05

### Added
- PNG toolbar icons replacing emoji-based status indicators
- Version system with `McpVersion.cs` central version constant
- Prefab editing support (open, save, close, stage info)
- Asset Store Publishing Tools validation integration
- Update instructions in README
- Support disclaimer in README

### Changed
- Python server deployment switched from zip download to git clone/pull for easier updates

## [1.0.0] - 2026-03-03

### Added
- Initial release of the realvirtual MCP Server
- WebSocket bridge between Unity Editor and MCP protocol
- 90+ built-in tools for scene, GameObject, component, transform, simulation, and screenshot control
- Auto-discovery of `[McpTool]` attributed methods via reflection
- Self-contained Python 3.12 distribution (no system Python required)
- One-click setup from Unity toolbar
- Multi-instance support with automatic port allocation
- Domain reload survival with auto-reconnect
- Authentication token system for secure connections
- Toolbar status indicator (gray/yellow/green) with activity label
