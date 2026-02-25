# realvirtual Unity MCP Package

**Give AI agents full control over your Unity Editor - scenes, GameObjects, components, simulation, and more.**

This open-source Unity package implements a [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that lets AI agents like Claude, Cursor, or any MCP-compatible client interact with Unity in real time.

### Why This MCP Server Is Different

Most MCP servers for Unity require you to **edit Python code** every time you add a new tool. This one doesn't. You define tools entirely in C# with a simple attribute - the Python server discovers them automatically:

```csharp
[McpTool("Spawn an enemy at position")]
public static string SpawnEnemy(
    [McpParam("Prefab name")] string prefab,
    [McpParam("X position")] float x,
    [McpParam("Z position")] float z)
{
    // Your Unity code here - runs on main thread
}
```

That's it. No Python changes, no server restart, no tool registration. Just recompile in Unity and the AI agent sees your new tool.

**Key advantages:**
- **Zero Python knowledge needed** - Define tools in C#, the language you're already using
- **Auto-discovery** - Tools are found via reflection, no manual registration
- **60+ built-in tools** - Scene, GameObjects, components, transforms, simulation, screenshots, prefabs, and more
- **Extensible in minutes** - Add `[McpTool]` to any static method and it's available to AI agents
- **Self-contained** - Ships with embedded Python 3.12, no system Python required
- **One-click setup** - Download Python + configure Claude from the Unity toolbar
- **Survives domain reloads** - Auto-reconnects after Unity recompiles scripts
- **Multi-instance support** - Run multiple Unity instances, each with its own MCP server

```
AI Agent (Claude Desktop / Claude Code / Cursor)
    |
    | MCP Protocol (stdio or SSE)
    v
Python MCP Server  -->  github.com/game4automation/realvirtual-MCP
    |
    | WebSocket (JSON, Port 18711)
    v
This Unity Package (C# WebSocket server + tool registry)
```

## Installation

### Via Unity Package Manager (Git URL)

1. Open Unity **Window > Package Manager**
2. Click **+ > Add package from git URL**
3. Enter: `https://github.com/game4automation/io.realvirtual.mcp.git`

### Requirements

- Unity 6000.0+
- Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`)

## Setup

After installing the Unity package:

1. A **brain icon** appears in the Unity toolbar - this is the MCP status indicator
2. Click the **gear icon** next to it to open the setup popup
3. Click **Download Python Server** - this downloads the embedded Python runtime (~70 MB) to `Assets/StreamingAssets/realvirtual-MCP/`
4. Click **Configure Claude** - this writes the MCP configuration to Claude Desktop and/or Claude Code

<img src="docs/mcp-setup.png" alt="MCP Setup Popup" width="500">

The Python MCP server is available separately at **[github.com/game4automation/realvirtual-MCP](https://github.com/game4automation/realvirtual-MCP)**.

You can also access setup via the Unity menu: **realvirtual > MCP**

<img src="docs/mcp-menu.png" alt="MCP Menu" width="500">

## How It Works

This package runs a **WebSocket server** inside the Unity Editor. When an AI agent sends a tool call, the Python MCP server forwards it over WebSocket to Unity, which executes it on the main thread and returns the result.

**Key components:**

- **McpWebSocketHandler** - WebSocket server (port 18711, auto-increments if busy)
- **McpToolRegistry** - Discovers all `[McpTool]` methods via reflection at startup
- **McpMainThreadDispatcher** - Bridges WebSocket threads to Unity's main thread
- **McpEditorBridge** - Auto-starts the server when Unity opens (`[InitializeOnLoad]`)
- **McpToolbarButton** - Status indicator with color-coded connection state

## Built-in Tools

<img src="docs/mcp-tools.png" alt="MCP Tools Panel" width="400">

The package includes 60+ tools organized by category:

| Category | Examples |
|----------|----------|
| **Simulation** | `sim_play`, `sim_stop`, `sim_pause`, `sim_status` |
| **Scene** | `scene_hierarchy`, `scene_find`, `scene_get_info` |
| **GameObjects** | `game_object_create`, `game_object_destroy`, `game_object_rename` |
| **Components** | `component_get`, `component_set`, `component_add`, `component_remove` |
| **Transforms** | `transform_set_position`, `transform_set_rotation`, `transform_set_scale` |
| **Materials** | `material_set_color`, `material_get_color` |
| **Physics** | `physics_add_rigidbody`, `physics_add_collider` |
| **Prefabs** | `prefab_instantiate`, `prefab_find`, `prefab_open`, `prefab_save` |
| **Editor** | `editor_recompile`, `editor_read_log`, `editor_save_scene`, `editor_wait_ready` |
| **Screenshots** | `screenshot_editor`, `screenshot_game`, `screenshot_scene` |

When used with the [realvirtual](https://assetstore.unity.com/packages/slug/311006) framework, additional tools are available:

| Category | Examples |
|----------|----------|
| **Drives** | `drive_list`, `drive_to`, `drive_jog_forward`, `drive_stop` |
| **Sensors** | `sensor_list`, `sensor_get`, `sensor_get_occupied` |
| **Signals** | `signal_list`, `signal_set_bool`, `signal_set_int`, `signal_set_float` |
| **IK** | `ik_get_state`, `ik_solve_target`, `ik_verify_fk` |

## Creating Custom Tools

Add `[McpTool]` to any `public static string` method. Tools are discovered automatically via reflection - no registration needed.

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

**Rules:**
- Method must be `public static` and return `string` (JSON)
- Tool names auto-convert from PascalCase to snake_case (`GetTime` -> `get_time`)
- Use `[McpParam("description")]` on parameters for AI agent context
- Optional parameters need default values
- Use `ToolHelpers.FindGameObject()`, `ToolHelpers.Ok()`, `ToolHelpers.Error()` for common patterns

## Toolbar Status

The toolbar brain icon shows connection state:

| Color | Meaning |
|-------|---------|
| Gray | Server stopped |
| Yellow | Server running, no clients connected |
| Green | Client(s) connected |
| Orange | Unity compiling scripts |

The activity label next to it shows the currently executing tool with elapsed time.

## Troubleshooting

**Server not starting**
- Check Unity Console for `[MCP]` log entries
- Toggle debug mode via the gear popup for verbose logging

**Tools not discovered**
- Ensure methods are `public static string` with `[McpTool]` attribute
- Check for compile errors in Unity Console
- Click "Refresh" in the toolbar popup

**Timeouts during play mode**
- Unity throttles editor updates in play mode - tool calls may be slower
- Some operations (`component_set`) don't work during play mode

## Python MCP Server

The Python server that bridges MCP clients to this Unity package is maintained separately:

**[github.com/game4automation/realvirtual-MCP](https://github.com/game4automation/realvirtual-MCP)**

It ships with an embedded Python 3.12 runtime and can be downloaded directly from the Unity toolbar popup.

## License

MIT License - Copyright (c) 2026 realvirtual GmbH

See [LICENSE.md](LICENSE.md) for full text.

## Links

- Website: https://realvirtual.io
- Documentation: https://doc.realvirtual.io
- Python MCP Server: https://github.com/game4automation/realvirtual-MCP
- Unity Asset Store: https://assetstore.unity.com/packages/slug/311006
