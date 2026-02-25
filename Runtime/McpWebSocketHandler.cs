// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace realvirtual.MCP
{
    //! Handles WebSocket connections for MCP protocol communication using WebSocketSharp.
    //!
    //! Implements a WebSocket server using the WebSocketSharp library which provides
    //! automatic PING/PONG handling, proper frame parsing, and connection lifecycle management.
    //! Handles MCP protocol commands: __discover__, __call__, __auth__, __heartbeat__.
    public class McpWebSocketHandler
    {
        private WebSocketServer _wss;
        private readonly int _basePort;
        private int _actualPort;
        private readonly McpToolRegistry _registry;
        private readonly string _authToken;
        private bool _isRunning;
        private int _connectedClients;
        private volatile string _cachedSchemas;

        //! Maximum number of port retries when base port is busy
        private const int MAX_PORT_RETRIES = 10;

        //! Enables detailed debug logging of connections, messages, and tool calls
        public static bool DebugMode { get; set; }

        //! Set to true before domain reload so background threads can fail fast
        //! instead of blocking for the full timeout duration.
        public static volatile bool IsReloading;

        //! Number of currently connected clients
        public int ConnectedClients => _connectedClients;

        //! Whether the WebSocket server is running
        public bool IsRunning => _isRunning && _wss != null && _wss.IsListening;

        //! The port actually bound by the server (may differ from base port if it was busy)
        public int ActualPort => _actualPort;

        //! Creates a WebSocket handler for MCP protocol.
        //! @param port Base port to listen on (will auto-increment if busy)
        //! @param registry Tool registry for handling __discover__ and __call__ commands
        //! @param authToken Optional authentication token (GUID)
        public McpWebSocketHandler(int port, McpToolRegistry registry, string authToken = null)
        {
            _basePort = port;
            _actualPort = port;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _authToken = authToken;
        }

        //! Starts the WebSocket server with dynamic port allocation.
        //! Tries the base port first, then increments up to MAX_PORT_RETRIES times
        //! if the port is already in use (e.g. another Unity instance).
        public Task StartAsync()
        {
            if (_isRunning)
            {
                McpLog.Warn("WS: Server already running");
                return Task.CompletedTask;
            }

            McpClientBehavior.Handler = this;

            for (int offset = 0; offset <= MAX_PORT_RETRIES; offset++)
            {
                int tryPort = _basePort + offset;
                try
                {
                    _wss = new WebSocketServer($"ws://127.0.0.1:{tryPort}");
                    _wss.AddWebSocketService<McpClientBehavior>("/mcp");
                    _wss.Start();
                    _actualPort = tryPort;
                    _isRunning = true;

                    if (offset > 0)
                        McpLog.Info($"WS: Base port {_basePort} busy, using port {tryPort}");

                    McpLog.Debug($"WS: Server started on ws://127.0.0.1:{tryPort}/mcp");

                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _wss = null;
                    if (offset < MAX_PORT_RETRIES)
                    {
                        McpLog.Debug($"WS: Port {tryPort} busy, trying {tryPort + 1}...");
                        continue;
                    }
                    McpLog.Error($"WS: Failed to start server on ports {_basePort}-{tryPort}: {ex.Message}");
                }
            }

            _isRunning = false;
            return Task.CompletedTask;
        }

        //! Stops the WebSocket server.
        //! @param reason Optional close reason sent to all connected clients (e.g. "domain_reload")
        public void Stop(string reason = null)
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            try
            {
                if (_wss != null && !string.IsNullOrEmpty(reason))
                {
                    // Send close frame with reason to all connected clients before stopping
                    try
                    {
                        var service = _wss.WebSocketServices["/mcp"];
                        if (service != null)
                        {
                            foreach (var session in service.Sessions.Sessions)
                            {
                                try
                                {
                                    session.Context.WebSocket.Close(CloseStatusCode.Normal, reason);
                                }
                                catch (Exception)
                                {
                                    // Individual session close may fail if already disconnected
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Debug($"WS: Error sending close reason: {ex.Message}");
                    }
                }

                _wss?.Stop();
            }
            catch (Exception ex)
            {
                McpLog.Debug($"WS: Error during stop: {ex.Message}");
            }
            _wss = null;
            _connectedClients = 0;

            McpLog.Debug($"WS: Server stopped{(reason != null ? $" (reason: {reason})" : "")}");
        }

        //! Called by McpClientBehavior when a client connects
        internal void OnClientConnected(string sessionId)
        {
            var count = Interlocked.Increment(ref _connectedClients);
            McpLog.Debug($"WS: Client connected: {sessionId} (total: {count})");
        }

        //! Called by McpClientBehavior when a client disconnects
        internal void OnClientDisconnected(string sessionId, string reason)
        {
            var count = Interlocked.Decrement(ref _connectedClients);
            if (count < 0)
            {
                Interlocked.Exchange(ref _connectedClients, 0);
                count = 0;
            }
            McpLog.Debug($"WS: Client disconnected: {sessionId} (total: {count})");
        }

        //! Processes an incoming MCP message and returns the response
        internal string ProcessMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                var command = json.Value<string>("command");

                // Skip debug log for heartbeats (too noisy - every 5s from watchdog)
                if (command != "__heartbeat__")
                    McpLog.Debug($"WS: Command: {command}");

                switch (command)
                {
                    case "__discover__":
                        return HandleDiscover();

                    case "__call__":
                        var tool = json.Value<string>("tool");
                        var arguments = ParseArguments(json);
                        return HandleCall(tool, arguments);

                    case "__auth__":
                        var token = json.Value<string>("token");
                        return HandleAuth(token);

                    case "__heartbeat__":
                        return HandleHeartbeat();

                    default:
                        McpLog.Warn($"WS: Unknown command: {command}");
                        return "{\"error\":\"Unknown command\"}";
                }
            }
            catch (ThreadAbortException)
            {
                // Expected during domain reload – don't log as error
                return "{\"error\":\"Unity is reloading scripts.\"}";
            }
            catch (Exception ex)
            {
                McpLog.Error($"WS: Error processing message: {ex.Message}");
                return $"{{\"error\":\"Processing failed: {EscapeJson(ex.Message)}\"}}";
            }
        }

        //! Timeout for main thread dispatch in milliseconds.
        //! If EditorApplication.update doesn't process the queue within this time, the call fails
        //! instead of blocking the WebSocketSharp thread forever.
        private const int MAIN_THREAD_TIMEOUT_MS = 30000;

        //! Seconds of pump inactivity after which we consider the main thread stalled
        //! and fail fast instead of blocking the WebSocket thread for the full timeout.
        //! Set to 8.0 to avoid false positives when Unity is backgrounded (~2Hz update rate).
        //! At 2Hz, SecondsSinceLastPump is typically ~0.5s but can spike to ~2s during GC.
        //! Compilation/import stalls exceed 10s, so 8.0 cleanly separates the two cases.
        private const double PUMP_STALL_THRESHOLD_SEC = 8.0;

        //! Dispatches a function to the main thread with timeout.
        //! Fails fast when domain reload is in progress or main thread pump is stalled
        //! (during compilation, asset import, etc.) to avoid blocking WebSocket threads.
        private string DispatchToMainThread(Func<string> func, string label)
        {
            // Fast fail: domain reload in progress
            if (IsReloading)
            {
                return "{\"error\":\"Unity is reloading scripts. Try again in a few seconds.\"}";
            }

            var dispatcher = McpMainThreadDispatcher.Instance;
            if (dispatcher == null)
            {
                McpLog.Debug($"WS: MainThreadDispatcher not available for {label} (domain reload?)");
                return "{\"error\":\"Unity main thread dispatcher not ready (domain reload in progress?)\"}";
            }

            // Fast fail: main thread pump is stalled (compilation, asset import, heavy loading)
            var secsSinceLastPump = McpMainThreadDispatcher.SecondsSinceLastPump;
            if (secsSinceLastPump > PUMP_STALL_THRESHOLD_SEC)
            {
                McpLog.Debug($"WS: Main thread stalled ({secsSinceLastPump:F1}s since last pump) for {label}");
                return $"{{\"error\":\"Unity editor is busy (compiling or loading). Main thread inactive for {secsSinceLastPump:F0}s. Try again shortly.\"}}";
            }

            var task = dispatcher.EnqueueWithResult(func);
            if (task.Wait(MAIN_THREAD_TIMEOUT_MS))
            {
                return task.Result;
            }

            McpLog.Warn($"WS: Main thread dispatch timeout ({MAIN_THREAD_TIMEOUT_MS}ms) for {label}");
            return $"{{\"error\":\"Main thread dispatch timeout for {EscapeJson(label)}. Unity editor may be unresponsive.\"}}";
        }

        //! Caches the tool schemas JSON so __discover__ can respond without main thread.
        //! Called after tool discovery completes on the main thread.
        public void CacheToolSchemas()
        {
            _cachedSchemas = _registry.GetToolSchemas();
        }

        //! Handles __discover__ command. Returns cached schemas if available,
        //! otherwise dispatches to main thread.
        private string HandleDiscover()
        {
            McpLog.Debug("WS: Handling discover");

            // Return cached schemas immediately (no main thread needed)
            var cached = _cachedSchemas;
            if (!string.IsNullOrEmpty(cached))
                return cached;

            // Fallback: dispatch to main thread (first call before cache is populated)
            return DispatchToMainThread(() =>
            {
                var schemas = _registry.GetToolSchemas();
                _cachedSchemas = schemas;
                return schemas;
            }, "__discover__");
        }

        //! Handles __call__ command by dispatching tool execution to main thread
        private string HandleCall(string toolName, Dictionary<string, object> arguments)
        {
            McpToolCallTracker.OnCallStarted(toolName);

            var result = DispatchToMainThread(() =>
            {
                McpLog.Debug($"WS: Executing on main thread: {toolName}");
                var toolResult = _registry.CallTool(toolName, arguments);
                return $"{{\"result\":{toolResult}}}";
            }, toolName);

            var success = result != null && !result.Contains("\"error\"");
            McpToolCallTracker.OnCallCompleted(success);

            McpLog.Debug($"WS: Tool {toolName} done");

            return result;
        }

        //! Handles __auth__ command
        private string HandleAuth(string token)
        {
            if (string.IsNullOrEmpty(_authToken) || token == _authToken)
            {
                McpLog.Debug("WS: Authentication successful");
                return "{\"status\":\"ok\"}";
            }
            McpLog.Warn("WS: Authentication failed");
            return "{\"error\":\"invalid token\"}";
        }

        //! Handles __heartbeat__ command
        private string HandleHeartbeat()
        {
            return $"{{\"status\":\"ok\",\"tools_count\":{_registry.ToolCount}}}";
        }

        //! Parses arguments object from a parsed JObject message.
        //! Handles Claude Code MCP proxy which wraps all params into a single "kwargs" key
        //! as either a JSON string, a JSON object, or a key=value string.
        private Dictionary<string, object> ParseArguments(JObject json)
        {
            var result = new Dictionary<string, object>();
            var args = json["arguments"] as JObject;
            if (args == null)
                return result;

            // Claude Code MCP proxy wraps all params into {"kwargs": "..."} or {"kwargs": {...}}
            if (args.Count == 1 && args["kwargs"] != null)
            {
                var kwargsToken = args["kwargs"];

                // Case 1: kwargs is a JSON object {"kwargs": {"name": "value", ...}}
                if (kwargsToken.Type == JTokenType.Object)
                {
                    args = kwargsToken as JObject;
                }
                // Case 2: kwargs is a string - try JSON first, then key=value parsing
                else if (kwargsToken.Type == JTokenType.String)
                {
                    var raw = kwargsToken.Value<string>();

                    // Try parsing as JSON string
                    try
                    {
                        var parsed = JObject.Parse(raw);
                        args = parsed;
                    }
                    catch
                    {
                        // Parse key=value format: "name=Foo" or "name=Foo, x=1.5"
                        // Also handles multi-param: "name=value\nother=value2"
                        var pairs = raw.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pair in pairs)
                        {
                            var eqIdx = pair.IndexOf('=');
                            if (eqIdx > 0)
                            {
                                var key = pair.Substring(0, eqIdx).Trim();
                                var val = pair.Substring(eqIdx + 1).Trim();
                                result[key] = val;
                            }
                        }
                        return result;
                    }
                }
            }

            // Standard parsing of flat argument properties
            foreach (var prop in args.Properties())
            {
                switch (prop.Value.Type)
                {
                    case JTokenType.Boolean:
                        result[prop.Name] = prop.Value.Value<bool>();
                        break;
                    case JTokenType.Integer:
                        result[prop.Name] = prop.Value.Value<long>();
                        break;
                    case JTokenType.Float:
                        result[prop.Name] = prop.Value.Value<double>();
                        break;
                    default:
                        result[prop.Name] = prop.Value.ToString();
                        break;
                }
            }

            return result;
        }

        //! Escapes a string for safe inclusion in JSON
        private static string EscapeJson(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }

    //! WebSocket behavior for MCP client connections.
    //! Each connected client gets its own instance of this class managed by WebSocketSharp.
    //! WebSocketSharp automatically handles PING/PONG frames for connection keepalive.
    internal class McpClientBehavior : WebSocketBehavior
    {
        //! Static reference to the handler instance for processing messages
        internal static McpWebSocketHandler Handler;

        protected override void OnOpen()
        {
            Handler?.OnClientConnected(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (Handler == null)
                return;

            try
            {
                // Skip debug logging for heartbeats (every 5s from watchdog, too noisy)
                bool isHeartbeat = e.Data != null && e.Data.Contains("__heartbeat__");
                if (!isHeartbeat)
                    McpLog.Debug($"WS: << {Truncate(e.Data)}");

                var response = Handler.ProcessMessage(e.Data);

                if (response != null)
                {
                    if (!isHeartbeat)
                        McpLog.Debug($"WS: >> {Truncate(response)}");
                    Send(response);
                }
            }
            catch (ThreadAbortException)
            {
                // Expected during domain reload – silently ignore
            }
            catch (Exception ex)
            {
                McpLog.Warn($"WS: Error in OnMessage: {ex.Message}");
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Handler?.OnClientDisconnected(ID, e.Reason);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            McpLog.Warn($"WS: WebSocket error: {e.Message}");
        }

        private static string Truncate(string msg)
        {
            if (msg == null) return "<null>";
            return msg.Length > 300 ? msg.Substring(0, 300) + "..." : msg;
        }
    }
}
