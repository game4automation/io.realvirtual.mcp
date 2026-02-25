// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Manages instance discovery files in ~/.unity-mcp/ so the Python MCP server
    //! can find which port each Unity Editor instance is listening on.
    //!
    //! Each Unity instance writes a status file identified by a hash of its project path.
    //! The Python server scans these files to connect to the correct instance.
    internal static class McpInstanceDiscovery
    {
        private static string _instanceHash;
        private static string _projectPath;
        private static string _discoveryDir;

        //! 8-char hex hash identifying this Unity instance
        public static string InstanceHash => _instanceHash;

        //! Initializes the discovery system with the current project path.
        //! Must be called from the main thread (uses Application.dataPath).
        public static void Initialize()
        {
            _projectPath = Application.dataPath;
            _instanceHash = ComputeHash(_projectPath);
            _discoveryDir = GetDiscoveryDirectory();

            try
            {
                if (!Directory.Exists(_discoveryDir))
                    Directory.CreateDirectory(_discoveryDir);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Discovery: Could not create discovery directory: {ex.Message}");
            }
        }

        //! Writes/updates the status file with current port and state.
        //! @param wsPort The WebSocket port the server is listening on
        //! @param reloading True if Unity is currently reloading scripts
        public static void WriteStatusFile(int wsPort, bool reloading = false)
        {
            if (string.IsNullOrEmpty(_instanceHash))
                return;

            try
            {
                var statusPath = Path.Combine(_discoveryDir, $"unity-mcp-status-{_instanceHash}.json");
                var json = $@"{{
  ""ws_port"": {wsPort},
  ""project_path"": ""{EscapeJsonString(_projectPath)}"",
  ""reloading"": {(reloading ? "true" : "false")},
  ""last_heartbeat"": ""{DateTime.UtcNow:O}"",
  ""pid"": {System.Diagnostics.Process.GetCurrentProcess().Id}
}}";
                File.WriteAllText(statusPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                McpLog.Debug($"Discovery: Could not write status file: {ex.Message}");
            }
        }

        //! Updates the heartbeat timestamp in the status file
        //! @param wsPort The WebSocket port the server is listening on
        public static void UpdateHeartbeat(int wsPort)
        {
            WriteStatusFile(wsPort, false);
        }

        //! Marks the instance as reloading (during domain reload)
        //! @param wsPort The WebSocket port the server was listening on
        public static void SetReloading(int wsPort)
        {
            WriteStatusFile(wsPort, true);
        }

        //! Deletes the status file for this instance (on quit)
        public static void CleanupFiles()
        {
            if (string.IsNullOrEmpty(_instanceHash) || string.IsNullOrEmpty(_discoveryDir))
                return;

            try
            {
                var statusPath = Path.Combine(_discoveryDir, $"unity-mcp-status-{_instanceHash}.json");
                if (File.Exists(statusPath))
                    File.Delete(statusPath);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Discovery: Could not cleanup files: {ex.Message}");
            }
        }

        //! Gets the discovery directory path (~/.unity-mcp/)
        private static string GetDiscoveryDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".unity-mcp");
        }

        //! Computes a deterministic 8-char hex hash from a project path
        private static string ComputeHash(string projectPath)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(projectPath);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        //! Escapes a string for JSON (handles backslashes and quotes)
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
