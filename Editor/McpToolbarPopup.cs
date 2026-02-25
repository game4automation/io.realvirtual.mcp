// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace realvirtual.MCP
{
    #region doc
    //! Popup window displaying comprehensive MCP Server status and available tools list.

    //! This popup window provides a detailed view of the MCP (Model Context Protocol) Server
    //! status, configuration, and all available tools. It uses Unity's UI Toolkit for modern,
    //! responsive UI with color-coded status indicators and interactive controls.
    //!
    //! Key Features:
    //! - Real-time server status display with color indicators
    //! - Server information (port, connected clients, tool count)
    //! - Tools grouped by category with descriptions and parameter info
    //! - Collapsible category sections
    //! - Refresh tools functionality
    //!
    //! Uses McpEditorBridge (auto-starts, no scene component needed).
    #endregion
    internal class McpToolbarPopup : PopupWindowContent
    {
        private VisualElement root;
        private ScrollView toolListScrollView;
        private Label clientChipLabel;
        private VisualElement clientChip;
        private int lastClientCount = -1;

        private const float windowWidth = 380f;
        private const float windowHeight = 500f;

        //! Returns the popup window size
        public override Vector2 GetWindowSize()
        {
            return new Vector2(windowWidth, windowHeight);
        }

        //! Legacy IMGUI callback - not used as UI Toolkit handles all rendering
        public override void OnGUI(Rect rect)
        {
        }

        //! Called when popup opens - loads USS stylesheet and builds the UI
        public override void OnOpen()
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/io.realvirtual.mcp/Editor/McpToolbarPopup.uss");

            root = new VisualElement();
            root.AddToClassList("popup-root");
            if (uss != null)
                root.styleSheets.Add(uss);

            editorWindow.rootVisualElement.Add(root);
            BuildUI();

            EditorApplication.update += UpdateClientChip;
        }

        //! Called when popup closes - unregisters update callback
        public override void OnClose()
        {
            EditorApplication.update -= UpdateClientChip;
        }

        //! Updates the client chip text and style when the connected client count changes
        private void UpdateClientChip()
        {
            int clients = McpEditorBridge.ConnectedClients;
            if (clients == lastClientCount)
                return;

            lastClientCount = clients;

            if (clientChipLabel != null)
                clientChipLabel.text = $"{clients} client{(clients != 1 ? "s" : "")}";

            if (clientChip != null)
            {
                clientChip.EnableInClassList("info-chip-connected", clients > 0);
                clientChip.EnableInClassList("info-chip", clients == 0);
            }
        }

        //! Builds the complete UI hierarchy
        private void BuildUI()
        {
            bool isRunning = McpEditorBridge.IsRunning;

            // Status bar at top
            var statusBar = new VisualElement();
            statusBar.AddToClassList(isRunning ? "status-bar-running" : "status-bar-stopped");

            var statusRow = new VisualElement();
            statusRow.AddToClassList("status-row");

            var dot = new VisualElement();
            dot.AddToClassList("status-dot");
            dot.AddToClassList(isRunning ? "status-dot-running" : "status-dot-stopped");
            statusRow.Add(dot);

            var statusText = new Label(isRunning ? "MCP Server Running" : "MCP Server Stopped");
            statusText.AddToClassList("status-text");
            statusRow.Add(statusText);

            statusBar.Add(statusRow);

            // Info chips row
            var chipsRow = new VisualElement();
            chipsRow.AddToClassList("chips-row");

            chipsRow.Add(MakeChip($"Port {McpEditorBridge.Port}"));
            chipsRow.Add(MakeChip($"{McpEditorBridge.ToolCount} tools"));
            var hash = McpEditorBridge.InstanceHash;
            if (!string.IsNullOrEmpty(hash))
                chipsRow.Add(MakeChip($"#{hash}"));
            int clients = McpEditorBridge.ConnectedClients;
            lastClientCount = clients;
            clientChip = MakeChip($"{clients} client{(clients != 1 ? "s" : "")}");
            clientChipLabel = clientChip as Label;
            if (clients > 0)
            {
                clientChip.RemoveFromClassList("info-chip");
                clientChip.AddToClassList("info-chip-connected");
            }
            chipsRow.Add(clientChip);

            if (McpEditorBridge.DebugMode)
            {
                var debugChip = new Label("Debug");
                debugChip.AddToClassList("info-chip");
                debugChip.style.backgroundColor = new StyleColor(new Color(0.6f, 0.4f, 0.1f, 0.3f));
                debugChip.style.color = new StyleColor(new Color(1f, 0.8f, 0.3f));
                chipsRow.Add(debugChip);
            }

            statusBar.Add(chipsRow);
            root.Add(statusBar);

            // Tools header row with refresh button
            var toolsHeader = new VisualElement();
            toolsHeader.AddToClassList("tools-header");

            var toolsTitle = new Label("Available Tools");
            toolsTitle.AddToClassList("tools-title");
            toolsHeader.Add(toolsTitle);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            var toggleBtn = new Button { text = isRunning ? "Stop" : "Start" };
            toggleBtn.AddToClassList("refresh-btn");
            toggleBtn.style.backgroundColor = isRunning
                ? new StyleColor(new Color(0.6f, 0.2f, 0.2f))
                : new StyleColor(new Color(0.2f, 0.5f, 0.2f));
            toggleBtn.clicked += () =>
            {
                if (McpEditorBridge.IsRunning)
                    McpEditorBridge.StopServer();
                else
                    McpEditorBridge.RefreshTools(); // RefreshTools calls StartServer
                EditorApplication.delayCall += () =>
                {
                    root.Clear();
                    BuildUI();
                };
            };
            btnRow.Add(toggleBtn);

            var refreshBtn = new Button { text = "Refresh" };
            refreshBtn.AddToClassList("refresh-btn");
            refreshBtn.clicked += () =>
            {
                McpEditorBridge.RefreshTools();
                EditorApplication.delayCall += () =>
                {
                    root.Clear();
                    BuildUI();
                };
            };
            btnRow.Add(refreshBtn);

            var debugBtn = new Button { text = "Debug" };
            debugBtn.AddToClassList("refresh-btn");
            debugBtn.tooltip = McpEditorBridge.DebugMode
                ? "Debug logging enabled - click to disable"
                : "Click to enable debug logging";
            debugBtn.style.backgroundColor = McpEditorBridge.DebugMode
                ? new StyleColor(new Color(0.2f, 0.6f, 0.2f))
                : new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            debugBtn.clicked += () =>
            {
                McpEditorBridge.DebugMode = !McpEditorBridge.DebugMode;
                EditorApplication.delayCall += () =>
                {
                    root.Clear();
                    BuildUI();
                };
            };
            btnRow.Add(debugBtn);

            bool claudeConfigured = McpClaudeDesktopConfigurator.IsConfigured;
            var claudeBtn = new Button { text = "Claude" };
            claudeBtn.AddToClassList("refresh-btn");
            claudeBtn.tooltip = claudeConfigured
                ? "Claude Desktop MCP configured - click to reconfigure"
                : "Click to configure Claude Desktop MCP server";
            claudeBtn.style.backgroundColor = claudeConfigured
                ? new StyleColor(new Color(0.2f, 0.6f, 0.2f))
                : new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            claudeBtn.clicked += () =>
            {
                McpClaudeDesktopConfigurator.ConfigureClaudeDesktop();
                EditorApplication.delayCall += () =>
                {
                    root.Clear();
                    BuildUI();
                };
            };
            btnRow.Add(claudeBtn);

            toolsHeader.Add(btnRow);

            root.Add(toolsHeader);

            // Scrollable tool list
            toolListScrollView = new ScrollView(ScrollViewMode.Vertical);
            toolListScrollView.AddToClassList("tool-scroll");
            root.Add(toolListScrollView);

            BuildToolList();
        }

        //! Builds the grouped tool list
        private void BuildToolList()
        {
            toolListScrollView.Clear();

            var tools = McpEditorBridge.GetToolDisplayInfos();
            if (tools == null || tools.Length == 0)
            {
                var empty = new Label("No tools discovered");
                empty.AddToClassList("empty-label");
                toolListScrollView.Add(empty);
                return;
            }

            // Group by category
            var groups = new Dictionary<string, List<McpEditorBridge.ToolDisplayInfo>>();
            foreach (var tool in tools)
            {
                if (!groups.ContainsKey(tool.Category))
                    groups[tool.Category] = new List<McpEditorBridge.ToolDisplayInfo>();
                groups[tool.Category].Add(tool);
            }

            foreach (var kvp in groups.OrderBy(g => g.Key))
            {
                var categoryName = kvp.Key;
                var categoryTools = kvp.Value;

                // Category foldout
                var foldout = new Foldout();
                foldout.text = $"{categoryName.ToUpper()}  ({categoryTools.Count})";
                foldout.AddToClassList("category-foldout");
                foldout.value = true;

                foreach (var tool in categoryTools)
                {
                    var toolCard = new VisualElement();
                    toolCard.AddToClassList("tool-card");

                    // Tool name (clickable - opens source file)
                    var nameLabel = new Label(tool.Name);
                    nameLabel.AddToClassList("tool-name");
                    if (!string.IsNullOrEmpty(tool.DeclaringTypeName))
                    {
                        nameLabel.tooltip = $"Click to open {tool.DeclaringTypeName}.{tool.MethodName}";
                        var typeName = tool.DeclaringTypeName;
                        var methodName = tool.MethodName;
                        nameLabel.AddManipulator(new Clickable(() =>
                        {
                            OpenToolSource(typeName, methodName);
                        }));
                    }
                    toolCard.Add(nameLabel);

                    // Description
                    if (!string.IsNullOrEmpty(tool.Description))
                    {
                        var descLabel = new Label(tool.Description);
                        descLabel.AddToClassList("tool-desc");
                        toolCard.Add(descLabel);
                    }

                    // Parameters
                    if (tool.Parameters != null && tool.Parameters.Length > 0)
                    {
                        var paramsContainer = new VisualElement();
                        paramsContainer.AddToClassList("params-container");

                        foreach (var param in tool.Parameters)
                        {
                            var paramRow = new VisualElement();
                            paramRow.AddToClassList("param-row");

                            var paramName = new Label(param.Name);
                            paramName.AddToClassList("param-name");
                            paramRow.Add(paramName);

                            var paramType = new Label(param.Type);
                            paramType.AddToClassList("param-type");
                            paramRow.Add(paramType);

                            if (param.IsOptional)
                            {
                                var optLabel = new Label("optional");
                                optLabel.AddToClassList("param-optional");
                                paramRow.Add(optLabel);
                            }

                            paramsContainer.Add(paramRow);

                            if (!string.IsNullOrEmpty(param.Description))
                            {
                                var paramDesc = new Label(param.Description);
                                paramDesc.AddToClassList("param-desc");
                                paramsContainer.Add(paramDesc);
                            }
                        }

                        toolCard.Add(paramsContainer);
                    }

                    foldout.Add(toolCard);
                }

                toolListScrollView.Add(foldout);
            }
        }

        //! Opens the source file for an MCP tool method in the code editor
        private void OpenToolSource(string declaringTypeName, string methodName)
        {
            var guids = AssetDatabase.FindAssets($"t:MonoScript {declaringTypeName}",
                new[] { "Assets", "Packages" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.name == declaringTypeName)
                {
                    // Try to find the method line number
                    var text = script.text;
                    int line = 1;
                    if (!string.IsNullOrEmpty(methodName))
                    {
                        var lines = text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Contains(methodName))
                            {
                                line = i + 1;
                                break;
                            }
                        }
                    }
                    AssetDatabase.OpenAsset(script, line);
                    return;
                }
            }
            McpLog.Warn($"Could not find source file for {declaringTypeName}.{methodName}");
        }

        private VisualElement MakeChip(string text)
        {
            var chip = new Label(text);
            chip.AddToClassList("info-chip");
            return chip;
        }
    }
}
