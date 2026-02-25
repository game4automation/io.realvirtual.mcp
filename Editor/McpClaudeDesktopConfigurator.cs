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
    //! Configures Claude Desktop and Claude Code to use the deployed realvirtual MCP server.
    internal static class McpClaudeDesktopConfigurator
    {
        private const string ServerName = "realvirtual-UnityMCP";

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
                    $"This will register the realvirtual MCP server with Claude Desktop " +
                    $"and update the project .mcp.json.\n\n" +
                    $"Python: {pythonExe}\n" +
                    $"Server: {serverScript}\n\n" +
                    "Existing MCP server entries will be preserved.",
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
            if (desktopOk)
                msg += "Claude Desktop: configured\n";
            else
                msg += "Claude Desktop: failed (see console)\n";

            if (codeOk)
                msg += "Claude Code .mcp.json: configured\n";
            else
                msg += "Claude Code .mcp.json: failed (see console)\n";

            msg += "\nRestart Claude Desktop to apply changes.";

            EditorUtility.DisplayDialog("MCP Configuration", msg, "OK");
        }

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

                config["mcpServers"][ServerName] = new JObject
                {
                    ["command"] = pythonExe.Replace('\\', '/'),
                    ["args"] = new JArray { serverScript.Replace('\\', '/') }
                };

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

                config["mcpServers"]["UnityMCP"] = new JObject
                {
                    ["command"] = pythonExe.Replace('\\', '/'),
                    ["args"] = new JArray { serverScript.Replace('\\', '/') }
                };

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
