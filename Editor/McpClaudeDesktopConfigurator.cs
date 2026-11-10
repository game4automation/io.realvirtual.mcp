// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Configures Claude Desktop and Claude Code to use the deployed realvirtual MCP servers.
    //!
    //! Tool ownership (plan-327 AP1/AP5): the Python server owns the Unity Editor tools,
    //! realvirtual CONNECT (http://localhost:5100/mcp) owns the web_* browser tools. Exactly one
    //! host serves web_*, so this generator writes CONNECT as the web_* transport and never an
    //! ACTIVE Node-bridge entry — running it used to re-introduce the overlap AP1 removed.
    //!
    //! The Node bridge is NOT retired and NOT deprecated: it stays the documented emergency
    //! fallback. In the project .mcp.json it is parked under the top-level _disabledMcpServers
    //! key, which Claude Code never launches (verified in AP1 against its zod schema), together
    //! with a _howToReactivate note — moving that one block back into mcpServers restores it.
    //!
    //! Arming --no-webviewer on the Python server (so it stops announcing its historic web_*
    //! proxy at all) is the follow-up plan 348, not this one.
    internal static class McpClaudeDesktopConfigurator
    {
        private const string ServerName = "realvirtual-UnityMCP";
        private const string WebViewerServerNameDesktop = "realvirtual-WebViewerMCP";
        private const string WebViewerServerNameCode = "WebViewerMCP";

        //! MCP client entry name for realvirtual CONNECT — the default web_* transport.
        private const string ConnectServerName = "realvirtual-CONNECT";

        //! CONNECT's Streamable-HTTP MCP endpoint. The port is CONNECT's fixed standard; it only
        //! differs when REALVIRTUAL_CONNECT_PORT was set on the CONNECT side as well.
        private const string ConnectMcpUrl = "http://localhost:5100/mcp";

        //! Top-level key holding switched-off client entries. Claude Code reads mcpServers only,
        //! so an entry parked here is genuinely not launched — unlike a merely renamed key.
        private const string DisabledServersKey = "_disabledMcpServers";

        //! Node fallback port for Claude Code. Each MCP host needs its own port (sharing one is
        //! EADDRINUSE), and every document — .mcp.json, webmcp.md, doc-ai-integration.md, the
        //! AI-Bridge panel — states Desktop 18714 / Code 18715. The generator contradicted that
        //! by writing 18714 for both; fixed here in the direction the documented port model
        //! already had (plan-327 AP5). Desktop needs no constant: its Node entry is no longer
        //! generated at all, CONNECT is the web_* host there too.
        private const string NodeBridgePortCode = "18715";

        //! Absolute path to the WebViewer MCP bridge entry (dist/index.js), or null if it is not
        //! built. When present, the fallback entry is emitted in switched-off form (see the class
        //! remarks). When absent (e.g. a customer install without the dev WebViewer), nothing is
        //! written for it at all.
        private static string GetWebViewerBridgePath()
        {
            try
            {
                var p = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "realvirtual-WebViewer~", "mcp-bridge", "dist", "index.js"));
                return File.Exists(p) ? p.Replace('\\', '/') : null;
            }
            catch { return null; }
        }

        //! Returns true if Claude Desktop config contains the realvirtual MCP server entry.
        internal static bool IsConfigured
        {
            get
            {
                try
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var configPath = Path.Combine(appData, "Claude", "claude_desktop_config.json");
                    if (!File.Exists(configPath)) return false;
                    var config = JObject.Parse(File.ReadAllText(configPath, Encoding.UTF8));
                    return config["mcpServers"]?[ServerName] != null;
                }
                catch { return false; }
            }
        }

        [MenuItem("Tools/realvirtual/Settings/Configure Claude Desktop MCP", false, 924)]
        private static void ConfigureClaudeDesktopMenuItem()
        {
            var mcpRoot = McpPythonDownloader.GetPythonServerPath();
            var pythonExe = Path.GetFullPath(Path.Combine(mcpRoot, "python", "python.exe"));
            var serverScript = Path.GetFullPath(Path.Combine(mcpRoot, "unity_mcp_server.py"));

            if (!File.Exists(pythonExe) || !File.Exists(serverScript))
            {
                EditorUtility.DisplayDialog("MCP Configuration Error",
                    $"MCP Python server not found.\n\n" +
                    $"Expected:\n{pythonExe}\n{serverScript}\n\n" +
                    "Use Tools > realvirtual > MCP > Download Python Server to install it.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Configure Claude Desktop MCP",
                    "This will register the realvirtual MCP servers with Claude Desktop " +
                    "and update the project .mcp.json.\n\n" +
                    $"Unity Editor tools (Python): {serverScript}\n" +
                    $"Browser web_* tools: realvirtual CONNECT, {ConnectMcpUrl}\n\n" +
                    "Other MCP server entries are preserved. An active WebViewerMCP Node entry is " +
                    "switched off, because CONNECT is the web_* host — it stays available as the " +
                    "documented fallback.",
                    "Configure", "Cancel"))
                return;

            ConfigureClaudeDesktop();
        }

        //! Configures both Claude Desktop and Claude Code .mcp.json with absolute paths to the deployed MCP server.
        internal static void ConfigureClaudeDesktop()
        {
            var mcpRoot = McpPythonDownloader.GetPythonServerPath();
            var pythonExe = Path.GetFullPath(Path.Combine(mcpRoot, "python", "python.exe"));
            var serverScript = Path.GetFullPath(Path.Combine(mcpRoot, "unity_mcp_server.py"));

            if (!File.Exists(pythonExe) || !File.Exists(serverScript))
            {
                McpLog.Error("Python server not found. Use Tools > realvirtual > MCP > Download Python Server.");
                return;
            }

            bool desktopOk = WriteClaudeDesktopConfig(pythonExe, serverScript);
            bool codeOk = WriteClaudeCodeConfig(pythonExe, serverScript);

            var msg = "";
            msg += desktopOk
                ? "Claude Desktop: configured\n"
                : "Claude Desktop: failed (see console)\n";

            msg += codeOk
                ? "Claude Code .mcp.json: configured\n"
                : "Claude Code .mcp.json: failed (see console)\n";

            msg += $"\nweb_* tools come from realvirtual CONNECT ({ConnectMcpUrl}). " +
                   "Enable its MCP server via the CONNECT tray icon (MCP server > Enabled) " +
                   "and restart CONNECT.\n";

            if (desktopOk && FindOnPath("npx") == null)
                msg += "\nClaude Desktop (classic) has no native HTTP MCP client and reaches " +
                       "CONNECT through the 'mcp-remote' stdio shim, which needs Node.js/npx. " +
                       "npx was not found on PATH, so no CONNECT entry was written for Desktop. " +
                       "Install Node.js and run this again, or use Claude Code.\n";

            msg += "\nRestart Claude Desktop to apply changes.";

            EditorUtility.DisplayDialog("MCP Configuration", msg, "OK");
        }

        //! Resolves an executable on PATH (with the Windows PATHEXT variants), or null.
        //! Used to decide whether the classic-Desktop CONNECT entry can work at all: it runs
        //! through the mcp-remote stdio shim, so writing it without Node.js present would just
        //! register a server that fails to start.
        private static string FindOnPath(string command)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path)) return null;

                var extensions = Application.platform == RuntimePlatform.WindowsEditor
                    ? new[] { ".cmd", ".exe", ".bat", "" }
                    : new[] { "" };

                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    foreach (var ext in extensions)
                    {
                        var candidate = Path.Combine(dir.Trim(), command + ext);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { /* an unreadable PATH entry must not break configuration */ }
            return null;
        }

        //! Copies a complete MCP client configuration with the deployed server's absolute paths.
        internal static void CopyMcpJsonToClipboard()
        {
            var mcpRoot = McpPythonDownloader.GetPythonServerPath();
            var pythonExe = Path.GetFullPath(Path.Combine(mcpRoot, "python", "python.exe"));
            var serverScript = Path.GetFullPath(Path.Combine(mcpRoot, "unity_mcp_server.py"));

            if (!File.Exists(pythonExe) || !File.Exists(serverScript))
            {
                EditorUtility.DisplayDialog("MCP Configuration Error",
                    "Python server not found. Use Tools > realvirtual > MCP > Download Python Server first.",
                    "OK");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = BuildMcpJson(pythonExe, serverScript)
                .ToString(Newtonsoft.Json.Formatting.Indented);
            McpLog.Info("MCP.json configuration copied to the clipboard.");
            EditorUtility.DisplayDialog("MCP Configuration",
                "MCP.json copied to the clipboard. Paste it into any MCP client that speaks " +
                "Streamable HTTP (Claude Code, Claude Code Desktop).\n\n" +
                "UnityMCP serves the Unity Editor tools, realvirtual CONNECT the browser web_* " +
                "tools. Any '_disabledMcpServers' block is the switched-off Node fallback — it is " +
                "never launched and can be ignored.",
                "OK");
        }

        //! Builds the Claude Code / clipboard configuration: the Unity Python server for the
        //! Editor tools, realvirtual CONNECT for the web_* browser tools, and — only when the
        //! Node bridge is actually built — the switched-off fallback block.
        private static JObject BuildMcpJson(string pythonExe, string serverScript)
        {
            var servers = new JObject
            {
                ["UnityMCP"] = new JObject
                {
                    ["command"] = pythonExe.Replace('\\', '/'),
                    ["args"] = new JArray { serverScript.Replace('\\', '/') }
                },
                // Claude Code and Claude Code Desktop speak Streamable HTTP natively, so CONNECT
                // needs no shim here. Only the classic Claude Desktop config does (see below).
                [ConnectServerName] = new JObject
                {
                    ["type"] = "http",
                    ["url"] = ConnectMcpUrl
                }
            };

            var config = new JObject { ["mcpServers"] = servers };

            var bridgeJs = GetWebViewerBridgePath();
            if (bridgeJs != null)
                config[DisabledServersKey] = BuildParkedNodeBridge(bridgeJs, NodeBridgePortCode);

            return config;
        }

        //! The Node bridge in switched-off form, with the one-step way back next to it. A parked
        //! entry without a documented return path is not a fallback.
        private static JObject BuildParkedNodeBridge(string bridgeJs, string webPort)
        {
            var parked = NewParkedBlock();
            parked[WebViewerServerNameCode] = new JObject
            {
                ["command"] = "node",
                ["args"] = new JArray { bridgeJs, "--web-port", webPort }
            };
            return parked;
        }

        //! An empty parked block carrying only the reactivation note, so every code path that
        //! parks an entry ships the instructions with it.
        private static JObject NewParkedBlock() => new()
        {
            ["_howToReactivate"] = new JArray
            {
                "Emergency fallback only - CONNECT stays the default web_* host.",
                "One step to re-activate: move the WebViewerMCP entry below into mcpServers, then restart",
                "the Claude client. Build it first with 'npm run setup' in",
                "Assets/realvirtual-WebViewer~/mcp-bridge (dist/ is ignored by git and Plastic).",
                "One step back: move it here again and restart.",
                "Entries under " + DisabledServersKey + " are never launched - Claude Code reads mcpServers only.",
                "Caveat: 'claude mcp add/remove --scope project' rewrites this file from mcpServers alone",
                "and drops these keys, so re-add them by hand after using that command."
            }
        };

        private static bool WriteClaudeDesktopConfig(string pythonExe, string serverScript)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var claudeDir = Path.Combine(appData, "Claude");
                var configPath = Path.Combine(claudeDir, "claude_desktop_config.json");

                if (!Directory.Exists(claudeDir))
                    Directory.CreateDirectory(claudeDir);

                JObject config;
                if (File.Exists(configPath))
                {
                    File.Copy(configPath, configPath + ".backup", true);
                    try { config = JObject.Parse(File.ReadAllText(configPath, Encoding.UTF8)); }
                    catch { config = new JObject(); }
                }
                else
                {
                    config = new JObject();
                }

                if (config["mcpServers"] == null)
                    config["mcpServers"] = new JObject();

                var servers = (JObject)config["mcpServers"];

                servers[ServerName] = new JObject
                {
                    ["command"] = pythonExe.Replace('\\', '/'),
                    ["args"] = new JArray { serverScript.Replace('\\', '/') }
                };

                // Classic Claude Desktop has no native HTTP MCP client — its documented contract is
                // a local stdio process. mcp-remote bridges stdio to CONNECT's HTTP endpoint;
                // --allow-http is required because the target is plain http on localhost. Written
                // only when npx can actually launch it, otherwise it would be a dead entry.
                if (FindOnPath("npx") != null)
                {
                    servers[ConnectServerName] = new JObject
                    {
                        ["command"] = "npx",
                        ["args"] = new JArray { "-y", "mcp-remote", ConnectMcpUrl, "--allow-http" }
                    };
                }
                else
                {
                    McpLog.Warn("npx not found on PATH - no CONNECT entry written for Claude Desktop. " +
                                "Classic Desktop reaches CONNECT through the mcp-remote stdio shim, " +
                                "which needs Node.js. Claude Code is unaffected (native HTTP).");
                }

                // CONNECT owns web_*, so an older ACTIVE Node-bridge entry is switched off here.
                // Unlike the project .mcp.json this config belongs to a third-party app whose
                // tolerance for foreign top-level keys is unverified, so the entry is removed
                // rather than parked under _disabledMcpServers. The way back is the .backup copy
                // written above, or the snippet in doc-ai-integration.md.
                if (servers.Remove(WebViewerServerNameDesktop))
                {
                    McpLog.Info($"Switched off the active Node-bridge entry '{WebViewerServerNameDesktop}' " +
                                "in the Claude Desktop config - CONNECT is the web_* host. " +
                                $"Previous file kept as {Path.GetFileName(configPath)}.backup; the " +
                                "fallback snippet is in doc-ai-integration.md.");
                }

                File.WriteAllText(configPath, config.ToString(Newtonsoft.Json.Formatting.Indented),
                    new UTF8Encoding(false));

                McpLog.Info($"Claude Desktop config written to: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                McpLog.Error($"Failed to write Claude Desktop config: {e.Message}");
                return false;
            }
        }

        private static bool WriteClaudeCodeConfig(string pythonExe, string serverScript)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var mcpJsonPath = Path.Combine(projectRoot, ".mcp.json");

                JObject config;
                if (File.Exists(mcpJsonPath))
                {
                    try { config = JObject.Parse(File.ReadAllText(mcpJsonPath, Encoding.UTF8)); }
                    catch { config = new JObject(); }
                }
                else
                {
                    config = new JObject();
                }

                if (config["mcpServers"] == null)
                    config["mcpServers"] = new JObject();

                var servers = (JObject)config["mcpServers"];
                var generated = BuildMcpJson(pythonExe, serverScript);

                foreach (var server in ((JObject)generated["mcpServers"]).Properties())
                {
                    servers[server.Name] = server.Value.DeepClone();
                }

                // Move an already active Node-bridge entry into the parked block instead of leaving
                // a second live web_* host behind. Without this, every run of this menu item undid
                // the ownership split (Connect.Tests pins that: McpToolOwnershipTests
                // .ProjectConfig_DoesNotLaunchTheNodeBridgeAsASecondWebHost).
                var parked = generated[DisabledServersKey] as JObject;
                var activeFallback = servers[WebViewerServerNameCode];
                if (activeFallback != null)
                {
                    parked ??= NewParkedBlock();
                    // Preserve what was actually configured (its port, its path) - the fallback has
                    // to come back exactly as it was, not as a regenerated guess.
                    parked[WebViewerServerNameCode] = activeFallback.DeepClone();
                    servers.Remove(WebViewerServerNameCode);
                    McpLog.Info($"Switched off the active '{WebViewerServerNameCode}' entry in .mcp.json: " +
                                $"CONNECT is the web_* host. It is parked under {DisabledServersKey} " +
                                "and one move away from being live again.");
                }

                if (parked != null)
                {
                    if (config[DisabledServersKey] is not JObject existingParked)
                        config[DisabledServersKey] = parked;
                    else
                        foreach (var entry in parked.Properties())
                            existingParked[entry.Name] = entry.Value.DeepClone();
                }

                File.WriteAllText(mcpJsonPath, config.ToString(Newtonsoft.Json.Formatting.Indented),
                    new UTF8Encoding(false));

                McpLog.Info($"Claude Code .mcp.json written to: {mcpJsonPath}");
                return true;
            }
            catch (Exception e)
            {
                McpLog.Error($"Failed to write .mcp.json: {e.Message}");
                return false;
            }
        }
    }
}
