// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Editor-level MCP server that auto-starts when Unity opens.
    //!
    //! Unlike McpBridge (MonoBehaviour), this does not require a scene component.
    //! The server starts automatically via [InitializeOnLoad] and survives play mode
    //! transitions, domain reloads, and scene changes.
    //!
    //! Uses EditorApplication.update to pump the main thread dispatch queue,
    //! enabling MCP tool calls in both editor and play mode.
    [InitializeOnLoad]
    internal static class McpEditorBridge
    {
        private static McpToolRegistry _registry;
        private static McpWebSocketHandler _handler;
        private static string _authToken;
        private static bool _isRunning;

        private const int DEFAULT_PORT = 18711;
        private const string DEBUG_MODE_PREF = "McpEditorBridge_DebugMode";
        private static bool _debugModeCached;
        private static bool _debugModeCacheValid;

        //! Enables detailed debug logging for MCP connections, messages, and tool calls.
        //! Persisted across Unity sessions via EditorPrefs. Value is cached in-memory
        //! to avoid EditorPrefs reads on every editor frame.
        public static bool DebugMode
        {
            get
            {
                if (!_debugModeCacheValid)
                {
                    _debugModeCached = EditorPrefs.GetBool(DEBUG_MODE_PREF, false);
                    _debugModeCacheValid = true;
                }
                return _debugModeCached;
            }
            set
            {
                _debugModeCached = value;
                _debugModeCacheValid = true;
                EditorPrefs.SetBool(DEBUG_MODE_PREF, value);
                McpWebSocketHandler.DebugMode = value;
                McpLog.DebugEnabled = value;
                McpLog.Info($"EditorBridge: Debug mode: {(value ? "ON" : "OFF")}");
            }
        }

        //! Whether the editor MCP server is running
        public static bool IsRunning => _isRunning && _handler != null && _handler.IsRunning;

        //! Number of connected clients
        public static int ConnectedClients => _handler?.ConnectedClients ?? 0;

        //! Number of discovered tools
        public static int ToolCount => _registry?.ToolCount ?? 0;

        //! The actual WebSocket port (may differ from DEFAULT_PORT if another instance is running)
        public static int Port => _handler?.ActualPort ?? DEFAULT_PORT;

        //! The instance hash for this Unity project (8-char hex)
        public static string InstanceHash => McpInstanceDiscovery.InstanceHash;

        //! The tool registry
        public static McpToolRegistry Registry => _registry;

        //! All registered tool names
        public static string[] GetToolNames()
        {
            if (_registry == null)
                return new string[0];
            return System.Linq.Enumerable.ToArray(_registry.ToolNames);
        }

        //! Info about a tool for display in the popup
        public struct ToolDisplayInfo
        {
            public string Name;
            public string Description;
            public string Category;
            public ParameterDisplayInfo[] Parameters;
            public string DeclaringTypeName; //!< Name of the class declaring the tool method
            public string MethodName; //!< Name of the tool method
        }

        //! Info about a tool parameter for display
        public struct ParameterDisplayInfo
        {
            public string Name;
            public string Type;
            public string Description;
            public bool IsOptional;
        }

        //! Gets rich display info for all tools, grouped by category
        public static ToolDisplayInfo[] GetToolDisplayInfos()
        {
            if (_registry == null)
                return new ToolDisplayInfo[0];

            var result = new List<ToolDisplayInfo>();
            foreach (var toolName in _registry.ToolNames)
            {
                var entry = _registry.GetTool(toolName);
                if (entry == null) continue;

                var underscoreIdx = toolName.IndexOf('_');
                var category = underscoreIdx > 0 ? toolName.Substring(0, underscoreIdx) : "other";

                var paramInfos = new List<ParameterDisplayInfo>();
                if (entry.Method != null)
                {
                    foreach (var param in entry.Method.GetParameters())
                    {
                        var paramAttr = param.GetCustomAttribute<McpParamAttribute>();
                        paramInfos.Add(new ParameterDisplayInfo
                        {
                            Name = param.Name,
                            Type = GetDisplayType(param.ParameterType),
                            Description = paramAttr?.Description ?? "",
                            IsOptional = param.HasDefaultValue
                        });
                    }
                }

                result.Add(new ToolDisplayInfo
                {
                    Name = toolName,
                    Description = entry.Description ?? "",
                    Category = category,
                    Parameters = paramInfos.ToArray(),
                    DeclaringTypeName = entry.Method?.DeclaringType?.Name ?? "",
                    MethodName = entry.Method?.Name ?? ""
                });
            }

            return result.OrderBy(t => t.Category).ThenBy(t => t.Name).ToArray();
        }

        private static string GetDisplayType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "int";
            if (type == typeof(float) || type == typeof(double)) return "number";
            if (type == typeof(bool)) return "bool";
            return type.Name;
        }

        static McpEditorBridge()
        {
            // Delay start to allow all assemblies to finish loading.
            // Uses EditorApplication.update instead of delayCall because delayCall does not
            // fire reliably when Unity is not in the foreground (MCP calls come from background).
            McpLog.Debug("EditorBridge: Static constructor (domain reload or first load)");
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.update += InitializeDeferred;
        }

        private static void InitializeDeferred()
        {
            EditorApplication.update -= InitializeDeferred;
            Initialize();
        }

        //! Clean shutdown before domain reload so Python receives a close frame
        //! and the port is released before the new server starts.
        private static void OnBeforeAssemblyReload()
        {
            McpLog.Debug("EditorBridge: Domain reload imminent - stopping server cleanly");
            McpWebSocketHandler.IsReloading = true;

            // Write reloading state so Python server knows to wait
            var port = _handler?.ActualPort ?? DEFAULT_PORT;
            McpInstanceDiscovery.SetReloading(port);

            if (_handler != null)
            {
                _handler.Stop("domain_reload");
                _handler = null;
            }
            _isRunning = false;
        }

        private static void Initialize()
        {
            if (_isRunning)
            {
                McpLog.Debug("EditorBridge: Initialize skipped - already running");
                return;
            }

            McpLog.Debug("EditorBridge: Initializing...");

            // Clear reload flag now that we're back on the main thread
            McpWebSocketHandler.IsReloading = false;

            // Sync debug mode from persisted preference
            McpWebSocketHandler.DebugMode = EditorPrefs.GetBool(DEBUG_MODE_PREF, false);
            McpLog.DebugEnabled = McpWebSocketHandler.DebugMode;

            // Initialize instance discovery (must be on main thread for Application.dataPath)
            McpInstanceDiscovery.Initialize();

            // Ensure the main thread dispatcher exists before the server starts accepting connections.
            // This MUST happen on the main thread (here) so that WebSocketSharp background threads
            // can safely use McpMainThreadDispatcher.Instance without trying to create GameObjects.
            McpMainThreadDispatcher.EnsureInstance();

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += StopServer;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            StartServer();
        }

        private static void StartServer()
        {
            try
            {
                // Ensure main thread pump is always registered (idempotent -= then +=)
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;

                // Ensure the main thread dispatcher exists
                McpMainThreadDispatcher.EnsureInstance();

                // Discover tools
                _registry = new McpToolRegistry();
                _registry.DiscoverTools();

                // Generate auth token
                _authToken = Guid.NewGuid().ToString();
                SaveAuthToken(_authToken);

                // Start WebSocket server (dynamic port allocation)
                _handler = new McpWebSocketHandler(DEFAULT_PORT, _registry, _authToken);
                _handler.CacheToolSchemas();
                _ = _handler.StartAsync();
                _isRunning = true;

                // Write discovery file so Python MCP server can find us
                McpInstanceDiscovery.WriteStatusFile(_handler.ActualPort);

                McpLog.Debug($"EditorBridge: Server started on port {_handler.ActualPort} with {_registry.ToolCount} tools (hash: {McpInstanceDiscovery.InstanceHash})");
            }
            catch (Exception ex)
            {
                McpLog.Error($"EditorBridge: Failed to start server: {ex.Message}");
                _isRunning = false;
            }
        }

        //! Stops the MCP server
        public static void StopServer()
        {
            if (!_isRunning)
                return;

            _handler?.Stop();
            _handler = null;
            _isRunning = false;

            // Cleanup discovery files
            McpInstanceDiscovery.CleanupFiles();

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= StopServer;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            McpLog.Debug("EditorBridge: Server stopped");
        }

        private static void Shutdown() => StopServer();

        //! Interval in seconds between discovery file heartbeat updates
        private const double HEARTBEAT_INTERVAL = 5.0;
        private static double _lastHeartbeatTime;

        //! Pumps the main thread dispatch queue in editor mode.
        //! Wrapped in try-catch because an unhandled exception in EditorApplication.update
        //! causes Unity to silently unsubscribe the callback, killing the pump permanently.
        //! Forces SceneView repaint when tool call activity is detected so the toolbar
        //! overlay updates even when Unity is not focused.
        private static void OnEditorUpdate()
        {
            try
            {
                // Ensure dispatcher exists (re-create after domain reload if needed)
                var dispatcher = McpMainThreadDispatcher.EnsureInstance();
                dispatcher?.ProcessPending();

                // Periodic heartbeat to update discovery file timestamp
                double now = EditorApplication.timeSinceStartup;
                if (_isRunning && _handler != null && now - _lastHeartbeatTime > HEARTBEAT_INTERVAL)
                {
                    _lastHeartbeatTime = now;
                    McpInstanceDiscovery.UpdateHeartbeat(_handler.ActualPort);
                }

                // Force UI update when tool call state changes or a call is in progress.
                // Without this, the toolbar overlay only updates at the (slow) unfocused
                // EditorApplication.update rate, so the user misses "Executing" / "Done" states.
                bool needsRepaint = McpToolCallTracker.ConsumeIsDirty()
                    || McpToolCallTracker.State == McpToolCallTracker.CallState.Executing;
                if (needsRepaint && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    SceneView.RepaintAll();
                    EditorApplication.RepaintHierarchyWindow();
                    EditorApplication.RepaintProjectWindow();
                    // Repaint all Inspector windows so property changes are visible immediately
                    var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
                    if (inspectorType != null)
                    {
                        var allInspectors = UnityEngine.Resources.FindObjectsOfTypeAll(inspectorType);
                        foreach (var insp in allInspectors)
                            ((EditorWindow)insp).Repaint();
                    }
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"EditorBridge: OnEditorUpdate error (pump preserved): {ex.Message}");
            }
        }

        //! Handles play mode transitions - restart server after domain reload
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            McpLog.Debug($"EditorBridge: Play mode: {state} (running={_isRunning})");

            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // Domain reload will follow - server stops via OnBeforeAssemblyReload
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    // After domain reload in play mode - verify server
                    if (!IsRunning)
                    {
                        McpLog.Debug("EditorBridge: Restarting after entering play mode");
                        McpMainThreadDispatcher.EnsureInstance();
                        StartServer();
                    }
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    // After domain reload back to edit mode - verify server
                    if (!IsRunning)
                    {
                        McpLog.Debug("EditorBridge: Restarting after exiting play mode");
                        McpMainThreadDispatcher.EnsureInstance();
                        StartServer();
                    }
                    break;
            }
        }

        //! Rediscovers all tools and restarts the server
        public static void RefreshTools()
        {
            var wasRunning = _isRunning;
            if (wasRunning)
            {
                _handler?.Stop();
                _handler = null;
                _isRunning = false;
            }

            StartServer();
        }

        //! Restarts the Python MCP server process by sending a shutdown close frame.
        //! The Python process exits cleanly, and the MCP client (Claude Desktop/Code)
        //! automatically restarts it. The C# WebSocket server is then restarted to
        //! accept the new connection.
        public static void RestartPythonServer()
        {
            McpLog.Info("EditorBridge: Restarting Python MCP server...");

            // Send "shutdown" close reason to Python so it exits cleanly
            if (_handler != null)
            {
                _handler.Stop("shutdown");
                _handler = null;
                _isRunning = false;
            }

            // Restart the C# WebSocket server to accept the new Python connection
            StartServer();
            McpLog.Info("EditorBridge: C# server restarted, waiting for Python reconnect...");
        }

        //! Saves the authentication token to a file
        private static void SaveAuthToken(string token)
        {
            try
            {
                var tokenPath = Path.Combine(Application.dataPath, ".mcp_auth_token");
                File.WriteAllText(tokenPath, token);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"EditorBridge: Could not save auth token: {ex.Message}");
            }
        }
    }
}
