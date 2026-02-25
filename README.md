# realvirtual Unity MCP Server

**Python MCP bridge for AI agents to control Unity Digital Twin simulations.**

This is the **Python server component** of the [realvirtual Unity MCP integration](https://assetstore.unity.com/packages/slug/311006). The Unity C# side (WebSocket handler, tool registry, editor integration) is available as a separate Unity package.

## What This Does

This Python server bridges AI agents (Claude Desktop, Claude Code, Cursor, etc.) with a running Unity Editor via WebSocket. Unity defines MCP tools in C# using `[McpTool]` attributes. This server discovers them automatically and exposes them as standard MCP tools.

```
AI Agent (Claude Desktop / Claude Code / Cursor)
    |
    | MCP Protocol (stdio or SSE)
    |
    v
This Python Server (FastMCP)
    |
    | WebSocket (JSON, Port 18711)
    |
    v
Unity Editor (realvirtual MCP C# Package)
```

## Self-Contained

This repository ships with an **embedded Python 3.12 runtime** and all dependencies pre-installed. No system Python installation required.

```
python/              Embedded Python 3.12 (Windows x64)
Lib/                 Pre-installed packages (mcp, websockets, etc.)
unity_mcp_server.py  The MCP server
start.bat            One-click launcher
requirements.txt     Dependency list (for reference)
```

## Quick Start

### Option A: Use with Claude Desktop or Claude Code

The Unity package configures this automatically via the setup button. Manual configuration:

**Claude Desktop** (`%APPDATA%/Claude/claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "C:/.../python/python.exe",
      "args": ["C:/.../unity_mcp_server.py"],
      "env": { "PYTHONPATH": "C:/.../Lib" }
    }
  }
}
```

**Claude Code** (`.mcp.json` in project root):
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "C:/.../python/python.exe",
      "args": ["C:/.../unity_mcp_server.py"],
      "env": { "PYTHONPATH": "C:/.../Lib" }
    }
  }
}
```

Replace `C:/...` with the actual path to your `StreamingAssets/realvirtual-MCP/` directory.

### Option B: Run Manually

```batch
start.bat
```

Or with explicit options:
```bash
python/python.exe unity_mcp_server.py --mode stdio
python/python.exe unity_mcp_server.py --mode sse --http-port 8080
python/python.exe unity_mcp_server.py --ws-port 18712
```

## Server Modes

| Mode | Flag | Use Case |
|------|------|----------|
| **stdio** | `--mode stdio` (default) | Claude Desktop, Claude Code |
| **SSE** | `--mode sse` | Network clients, web integrations |

## Command Line Options

```
--mode stdio|sse       Server mode (default: stdio)
--ws-port PORT         Unity WebSocket port (default: auto-discover)
--http-port PORT       HTTP port for SSE mode (default: 8080)
--project-path PATH    Connect to specific Unity instance
```

## WebSocket Protocol

The server communicates with Unity via WebSocket on port 18711.

### Discovery
```json
{"command": "__discover__"}
// Response: {"tools": [...], "schema_version": "1.0.0"}
```

### Tool Call
```json
{"command": "__call__", "tool": "sim_play", "arguments": {}}
// Response: {"result": {"status": "playing"}}
```

### Authentication
```json
{"command": "__auth__", "token": "..."}
// Response: {"status": "ok"}
```

## Available Tools

The server auto-discovers all tools defined in Unity. Typical tools include:

| Category | Examples | Description |
|----------|----------|-------------|
| **Simulation** | `sim_play`, `sim_stop`, `sim_status` | Control simulation lifecycle |
| **Scene** | `scene_hierarchy`, `scene_find` | Navigate scene structure |
| **GameObjects** | `game_object_create`, `game_object_destroy` | Create and manage objects |
| **Components** | `component_get`, `component_set` | Read and modify components |
| **Transforms** | `transform_set_position`, `transform_set_rotation` | Move and rotate objects |
| **Editor** | `editor_recompile`, `editor_read_log` | Editor operations |
| **Drives** | `drive_list`, `drive_to`, `drive_stop` | Control motion drives* |
| **Sensors** | `sensor_list`, `sensor_get` | Read sensor states* |
| **Signals** | `signal_list`, `signal_set_bool` | PLC signal I/O* |

*Drive, Sensor, and Signal tools require the [realvirtual](https://assetstore.unity.com/packages/slug/311006) Unity package.

## Creating Custom Tools (Unity Side)

Add tools in any C# class:

```csharp
using realvirtual.MCP;

public static class MyTools
{
    [McpTool("Get current time")]
    public static string GetTime()
    {
        return $"{{\"time\":\"{System.DateTime.Now}\"}}";
    }

    [McpTool("Add two numbers")]
    public static string Add(
        [McpParam("First number")] float a,
        [McpParam("Second number")] float b)
    {
        return $"{{\"result\":{a + b}}}";
    }
}
```

Tools are discovered automatically via reflection. No registration needed.

## Unity Package

The C# Unity side of this integration is available at:

- **Unity Asset Store**: [realvirtual MCP](https://assetstore.unity.com/packages/slug/311006)
- **Git URL**: `https://github.com/realvirtual/unity-mcp.git`

Install via Unity Package Manager > Add package from git URL.

## Using with Your Own Python

If you prefer your system Python instead of the embedded one:

```bash
pip install -r requirements.txt
python unity_mcp_server.py --mode stdio
```

Requirements: Python 3.10+, `websockets>=12.0`, `mcp>=1.8.0`

## Troubleshooting

**Server can't connect to Unity**
- Ensure Unity Editor is running with the MCP package installed
- Check that port 18711 is not blocked by firewall
- Verify the MCP WebSocket server is running (green icon in Unity toolbar)

**"python.exe blocked by antivirus"**
- Add an exception for the embedded `python/python.exe` in your antivirus
- Or use your system Python installation instead

**No tools discovered**
- Check Unity Console for compile errors
- Click "Refresh" in the Unity MCP toolbar popup

## License

MIT License

Copyright (c) 2026 realvirtual GmbH

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Links

- Website: https://realvirtual.io
- Documentation: https://doc.realvirtual.io
- Unity Asset Store: https://assetstore.unity.com/packages/slug/311006
- Support: https://realvirtual.io/support
