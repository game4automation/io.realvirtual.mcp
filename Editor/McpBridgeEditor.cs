// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Custom inspector for McpBridge component.
    //!
    //! Provides a user-friendly interface for managing the MCP server with:
    //! - Server status display (running/stopped, port, tool count, connected clients)
    //! - Start/Stop server buttons
    //! - Refresh tools button
    //! - List of discovered tools
    //! - Configuration validation
    [CustomEditor(typeof(McpBridge))]
    public class McpBridgeEditor : Editor
    {
        private bool _showTools = false;
        private Vector2 _toolsScrollPosition;

        public override void OnInspectorGUI()
        {
            var bridge = (McpBridge)target;

            // Header
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("realvirtual MCP Server", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Status Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Server Status", EditorStyles.boldLabel);

            var statusColor = bridge.IsRunning ? Color.green : Color.gray;
            var prevColor = GUI.color;
            GUI.color = statusColor;
            var statusText = bridge.IsRunning ? "Running" : "Stopped";
            EditorGUILayout.LabelField("Status:", statusText);
            GUI.color = prevColor;

            EditorGUILayout.LabelField("Port:", bridge.Port.ToString());
            EditorGUILayout.LabelField("Tools Discovered:", bridge.ToolCount.ToString());
            EditorGUILayout.LabelField("Connected Clients:", bridge.ConnectedClients.ToString());

            if (!string.IsNullOrEmpty(bridge.AuthToken))
            {
                EditorGUILayout.LabelField("Auth Token:", bridge.AuthToken.Substring(0, 8) + "...");
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Controls Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (bridge.IsRunning)
            {
                if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                {
                    bridge.StopServer();
                }
            }
            else
            {
                if (GUILayout.Button("Start Server", GUILayout.Height(30)))
                {
                    bridge.StartServer();
                }
            }

            if (GUILayout.Button("Refresh Tools", GUILayout.Height(30)))
            {
                bridge.RefreshTools();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Configuration Section
            DrawDefaultInspector();

            EditorGUILayout.Space();

            // Tools List Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _showTools = EditorGUILayout.Foldout(_showTools, $"Discovered Tools ({bridge.ToolCount})", true);

            if (_showTools && bridge.ToolCount > 0)
            {
                EditorGUILayout.Space();
                _toolsScrollPosition = EditorGUILayout.BeginScrollView(_toolsScrollPosition, GUILayout.MaxHeight(200));

                var toolNames = bridge.GetToolNames();
                foreach (var toolName in toolNames)
                {
                    EditorGUILayout.LabelField("• " + toolName, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
            else if (_showTools && bridge.ToolCount == 0)
            {
                EditorGUILayout.HelpBox("No tools discovered. Make sure you have methods marked with [McpTool] attribute.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Validation Section
            if (!bridge.ValidateConfiguration())
            {
                EditorGUILayout.HelpBox("Configuration issues detected. Please check port number and tool discovery.", MessageType.Warning);
            }

            // Help Section
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Help", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The MCP Bridge enables AI agents to control Unity simulation.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("WebSocket URL: ws://localhost:" + bridge.Port + "/mcp", EditorStyles.miniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add [McpTool] attribute to static methods to create new tools.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();

            // Force repaint when playing to update status
            if (Application.isPlaying)
            {
                Repaint();
            }
        }
    }
}
#endif
