// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Legacy MCP Bridge component (optional scene-based server).
    //!
    //! In Editor mode, McpEditorBridge auto-starts the MCP server without needing a scene component.
    //! This MonoBehaviour is only needed for standalone builds where the editor bridge is not available.
    //! If McpEditorBridge is already running on the same port, this component skips server startup.
    [AddComponentMenu("realvirtual/MCP/MCP Bridge")]
    public class McpBridge : MonoBehaviour
    {
        [Header("Server Configuration")]
        [Tooltip("Port for WebSocket server (default: 18711)")]
        [SerializeField]
        private int port = 18711; //!< WebSocket server port

        [Tooltip("Start server automatically on Awake")]
        [SerializeField]
        private bool autoStart = true; //!< Whether to start server automatically

        [Tooltip("Generate and save authentication token")]
        [SerializeField]
        private bool useAuthToken = true; //!< Whether to use authentication token

        [Header("Embedded MCP Server")]
        [Tooltip("Launch embedded Python MCP server")]
        [SerializeField]
        private bool launchMcpServer = true; //!< Whether to launch embedded Python MCP server

        [Tooltip("HTTP port for MCP SSE endpoint (default: 8080)")]
        [SerializeField]
        private int mcpHttpPort = 8080; //!< HTTP port for SSE mode

        [Header("Status (Read-Only)")]
        [SerializeField]
        private bool isRunning = false; //!< Server running status

        [SerializeField]
        private int toolCount = 0; //!< Number of discovered tools

        [SerializeField]
        private int connectedClients = 0; //!< Number of connected clients

        [SerializeField]
        private bool mcpServerRunning = false; //!< Embedded MCP server process status

        private McpToolRegistry _registry;
        private McpWebSocketHandler _handler;
        private Task _serverTask;
        private string _authToken;
        private Process _mcpProcess; //!< Embedded Python MCP server process

        //! Gets the authentication token (GUID)
        public string AuthToken => _authToken;

        //! Gets the WebSocket port
        public int Port => port;

        //! Gets whether the server is running
        public bool IsRunning => isRunning;

        //! Gets the number of discovered tools
        public int ToolCount => toolCount;

        //! Gets the number of connected clients
        public int ConnectedClients => connectedClients;

        //! Gets whether the embedded MCP server process is running
        public bool McpServerRunning => mcpServerRunning;

        //! Gets the MCP SSE endpoint URL
        public string McpEndpointUrl => $"http://localhost:{mcpHttpPort}/sse";

        void Awake()
        {
            DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
            // In editor mode, McpEditorBridge handles the server - skip startup
            McpLog.Info("Bridge: Editor mode - McpEditorBridge handles the MCP server");
            return;
#endif

            // Ensure main thread dispatcher exists (must be on main thread)
            var dispatcher = McpMainThreadDispatcher.EnsureInstance();

            // Generate auth token
            if (useAuthToken)
            {
                _authToken = Guid.NewGuid().ToString();
                SaveAuthToken();
            }

            // Initialize registry and discover tools
            _registry = new McpToolRegistry();
            _registry.DiscoverTools();
            toolCount = _registry.ToolCount;

            McpLog.Info($"Bridge: Initialized with {toolCount} tools");

            if (autoStart)
            {
                StartServer();
            }

            if (launchMcpServer)
            {
                StartMcpServer();
            }
        }

        void OnEnable()
        {
#if UNITY_EDITOR
            return;
#endif
            if (!isRunning && autoStart)
            {
                StartServer();
            }
        }

        void OnDisable()
        {
            StopMcpServer();
            StopServer();
        }

        void OnDestroy()
        {
            StopMcpServer();
            StopServer();
        }

        void Update()
        {
            // Update status fields for Inspector
            if (_handler != null)
            {
                connectedClients = _handler.ConnectedClients;
                isRunning = _handler.IsRunning;
            }

            // Check if MCP process is still alive
            if (_mcpProcess != null)
            {
                mcpServerRunning = !_mcpProcess.HasExited;
            }
        }

        //! Starts the WebSocket server
        public void StartServer()
        {
            if (isRunning)
            {
                McpLog.Warn("Bridge: Server already running");
                return;
            }

            try
            {
                // Validate port range
                if (port < 1024 || port > 65535)
                {
                    McpLog.Error($"Bridge: Invalid port {port}. Must be between 1024 and 65535");
                    return;
                }

                _handler = new McpWebSocketHandler(port, _registry, _authToken);
                _serverTask = _handler.StartAsync();
                isRunning = true;

                McpLog.Info($"Bridge: Server started on port {port}");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Bridge: Failed to start server: {ex.Message}\n{ex.StackTrace}");
                isRunning = false;
            }
        }

        //! Stops the WebSocket server
        public void StopServer()
        {
            if (!isRunning)
                return;

            try
            {
                _handler?.Stop();
                _handler = null;
                isRunning = false;

                McpLog.Info("Bridge: Server stopped");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Bridge: Error stopping server: {ex.Message}");
            }
        }

        //! Rediscovers all tools and restarts the server
        public void RefreshTools()
        {
            var wasRunning = isRunning;

            if (wasRunning)
            {
                StopServer();
            }

            _registry = new McpToolRegistry();
            _registry.DiscoverTools();
            toolCount = _registry.ToolCount;

            McpLog.Info($"Bridge: Refreshed tools: {toolCount} found");

            if (wasRunning)
            {
                StartServer();
            }
        }

        //! Returns the Python server root path.
        //! Uses %LOCALAPPDATA%/realvirtual-MCP/ (external to Unity project).
        private static string GetPythonServerPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "realvirtual-MCP");
        }

        //! Starts the embedded Python MCP server from the external deployment directory.
        public void StartMcpServer()
        {
            if (_mcpProcess != null && !_mcpProcess.HasExited)
            {
                McpLog.Warn("Bridge: MCP server process already running");
                return;
            }

            try
            {
                var mcpRoot = GetPythonServerPath();
                var pythonExe = Path.Combine(mcpRoot, "python", "python.exe");
                var serverScript = Path.Combine(mcpRoot, "unity_mcp_server.py");

                if (!File.Exists(pythonExe))
                {
                    McpLog.Warn($"Bridge: Embedded Python not found at {pythonExe} - MCP server not started");
                    return;
                }

                if (!File.Exists(serverScript))
                {
                    McpLog.Warn($"Bridge: MCP server script not found at {serverScript}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{serverScript}\" --mode sse --http-port {mcpHttpPort} --ws-port {port}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = mcpRoot
                };

                _mcpProcess = new Process { StartInfo = startInfo };
                _mcpProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        McpLog.Info($"Server: {e.Data}");
                };
                _mcpProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        McpLog.Info($"Server: {e.Data}");
                };

                _mcpProcess.Start();
                _mcpProcess.BeginOutputReadLine();
                _mcpProcess.BeginErrorReadLine();

                mcpServerRunning = true;
                McpLog.Info($"Bridge: MCP server started (PID {_mcpProcess.Id}) - SSE endpoint: http://localhost:{mcpHttpPort}/sse");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Bridge: Failed to start MCP server: {ex.Message}");
                mcpServerRunning = false;
            }
        }

        //! Stops the embedded Python MCP server process
        public void StopMcpServer()
        {
            if (_mcpProcess == null)
                return;

            try
            {
                if (!_mcpProcess.HasExited)
                {
                    _mcpProcess.Kill();
                    _mcpProcess.WaitForExit(3000);
                    McpLog.Info("Bridge: MCP server process stopped");
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Bridge: Error stopping MCP server: {ex.Message}");
            }
            finally
            {
                _mcpProcess.Dispose();
                _mcpProcess = null;
                mcpServerRunning = false;
            }
        }

        //! Saves the authentication token to a file for Python to read
        private void SaveAuthToken()
        {
            try
            {
                var tokenPath = Path.Combine(Application.dataPath, ".mcp_auth_token");
                File.WriteAllText(tokenPath, _authToken);
                McpLog.Info($"Bridge: Auth token saved to {tokenPath}");
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Bridge: Could not save auth token: {ex.Message}");
            }
        }

        //! Gets all registered tool names
        public string[] GetToolNames()
        {
            if (_registry == null)
                return new string[0];

            return System.Linq.Enumerable.ToArray(_registry.ToolNames);
        }

        //! Validates the server configuration
        public bool ValidateConfiguration()
        {
            if (port < 1024 || port > 65535)
            {
                McpLog.Error("Bridge: Invalid port configuration");
                return false;
            }

            if (_registry == null || _registry.ToolCount == 0)
            {
                McpLog.Warn("Bridge: No tools discovered");
                return false;
            }

            return true;
        }
    }
}
