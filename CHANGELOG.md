# Changelog

All notable changes to the realvirtual MCP Server package.

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
