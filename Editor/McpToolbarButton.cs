// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

#if UNITY_2021_2_OR_NEWER
using UnityEngine;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;
using System;

namespace realvirtual.MCP
{
    #region doc
    //! Standalone MCP toolbar overlay with server status icon and live tool call activity display.

    //! Self-contained toolbar overlay for the SceneView. Shows the MCP brain icon with
    //! color-coded server status, and a live text label showing the currently executing
    //! or most recently completed MCP tool call. Fully independent of the realvirtual
    //! toolbar system so it can be delivered as a separate package.
    #endregion
    [Overlay(typeof(SceneView), OVERLAY_ID, "realvirtual MCP", true)]
    public class McpToolbarOverlay : ToolbarOverlay
    {
        public const string OVERLAY_ID = "mcp-toolbar-overlay";

        McpToolbarOverlay() : base(
            McpStatusButton.ID,
            McpActivityLabel.ID,
            McpConfigButton.ID
        ) { }

        public override void OnCreated()
        {
            base.OnCreated();
            collapsed = false;
        }
    }

    //! Toolbar button displaying MCP Server status with color-coded brain icon.
    //! Clicking opens the MCP popup with server details and tool list.
    [EditorToolbarElement(ID, typeof(SceneView))]
    sealed class McpStatusButton : EditorToolbarDropdown
    {
        public const string ID = "McpStatusButton";
        private static Font _mcpFont;

        public McpStatusButton()
        {
            // Set brain icon
            int unicode = Convert.ToInt32("e10e", 16);
            text = char.ConvertFromUtf32(unicode);

            if (_mcpFont == null)
                _mcpFont = AssetDatabase.LoadAssetAtPath<Font>(
                    "Packages/io.realvirtual.mcp/Editor/Fonts/MaterialSymbolsOutlined.ttf");

            if (_mcpFont != null)
            {
                style.unityFontDefinition = new StyleFontDefinition(_mcpFont);
                style.fontSize = 18;
            }

            tooltip = "MCP Server: Checking...";
            clicked += OnClicked;

            EditorApplication.update += UpdateStatus;
            UpdateStatus();
        }

        ~McpStatusButton()
        {
            EditorApplication.update -= UpdateStatus;
        }

        private void OnClicked()
        {
            var popup = new McpToolbarPopup();
            UnityEditor.PopupWindow.Show(worldBound, popup);
        }

        private void UpdateStatus()
        {
            if (EditorApplication.isCompiling)
            {
                tooltip = "MCP Server: Compiling...";
                style.color = new StyleColor(new Color(0.9f, 0.75f, 0.2f));
            }
            else if (McpEditorBridge.IsRunning)
            {
                int clients = McpEditorBridge.ConnectedClients;
                var hash = McpEditorBridge.InstanceHash ?? "";
                tooltip = $"MCP Server: Running | Port {McpEditorBridge.Port} | {McpEditorBridge.ToolCount} tools | {clients} client{(clients != 1 ? "s" : "")}" +
                          (string.IsNullOrEmpty(hash) ? "" : $" | #{hash}");
                if (clients > 0)
                    style.color = new StyleColor(new Color(0.3f, 0.69f, 0.31f));
                else
                    style.color = new StyleColor(new Color(0.9f, 0.75f, 0.2f));
            }
            else
            {
                tooltip = "MCP Server: Stopped";
                style.color = new StyleColor(new Color(0.62f, 0.62f, 0.62f));
            }
        }
    }

    //! Toolbar element showing live MCP tool call activity text.
    //! Always visible - shows "Idle" when no tool call is active, tool name during
    //! execution, and checkmark/X after completion. Fixed minimum width to prevent layout jumps.
    [EditorToolbarElement(ID, typeof(SceneView))]
    sealed class McpActivityLabel : VisualElement
    {
        public const string ID = "McpActivityLabel";

        private readonly Label _label;
        private McpToolCallTracker.CallState _lastState = McpToolCallTracker.CallState.Idle;
        private double _resetAfterTime;

        private const double DONE_DISPLAY_DELAY = 3.0;
        private const double ERROR_DISPLAY_DELAY = 5.0;

        // Colors
        private static readonly Color IDLE_COLOR = new Color(0.62f, 0.62f, 0.62f);
        private static readonly Color EXECUTING_COLOR = new Color(0.51f, 0.78f, 1f);
        private static readonly Color DONE_COLOR = new Color(0.51f, 0.82f, 0.51f);
        private static readonly Color ERROR_COLOR = new Color(0.94f, 0.51f, 0.51f);
        private static readonly Color COMPILING_COLOR = new Color(0.9f, 0.75f, 0.2f);

        public McpActivityLabel()
        {
            // Container box styling
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.paddingLeft = 8;
            style.paddingRight = 8;
            style.paddingTop = 2;
            style.paddingBottom = 2;
            style.marginLeft = 2;
            style.minWidth = 170;
            style.width = 170;

            _label = new Label("Idle");
            _label.style.fontSize = 11;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _label.style.overflow = Overflow.Hidden;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.color = IDLE_COLOR;
            _label.style.paddingLeft = 0;
            _label.style.paddingRight = 0;
            _label.style.marginTop = 0;
            _label.style.marginBottom = 0;
            Add(_label);

            EditorApplication.update += PollStatus;
        }

        ~McpActivityLabel()
        {
            EditorApplication.update -= PollStatus;
        }

        private void PollStatus()
        {
            var state = McpToolCallTracker.State;
            var toolName = McpToolCallTracker.CurrentToolName ?? "";
            double now = EditorApplication.timeSinceStartup;
            double elapsed = McpToolCallTracker.ElapsedSeconds;
            string elapsedStr = elapsed >= 0.1 ? $" ({elapsed:F1}s)" : "";

            switch (state)
            {
                case McpToolCallTracker.CallState.Executing:
                    _label.text = toolName + elapsedStr;
                    _label.style.color = EXECUTING_COLOR;
                    style.backgroundColor = new Color(0.18f, 0.22f, 0.30f, 0.9f);
                    _lastState = state;
                    break;

                case McpToolCallTracker.CallState.Done:
                    if (_lastState != McpToolCallTracker.CallState.Done)
                        _resetAfterTime = now + DONE_DISPLAY_DELAY;
                    _label.text = "\u2713 " + toolName + elapsedStr;
                    _label.style.color = DONE_COLOR;
                    style.backgroundColor = new Color(0.18f, 0.25f, 0.18f, 0.9f);
                    _lastState = state;
                    if (now >= _resetAfterTime)
                        McpToolCallTracker.Reset();
                    break;

                case McpToolCallTracker.CallState.Error:
                    if (_lastState != McpToolCallTracker.CallState.Error)
                        _resetAfterTime = now + ERROR_DISPLAY_DELAY;
                    _label.text = "\u2717 " + toolName + elapsedStr;
                    _label.style.color = ERROR_COLOR;
                    style.backgroundColor = new Color(0.28f, 0.18f, 0.18f, 0.9f);
                    _lastState = state;
                    if (now >= _resetAfterTime)
                        McpToolCallTracker.Reset();
                    break;

                case McpToolCallTracker.CallState.Idle:
                    if (EditorApplication.isCompiling)
                    {
                        _label.text = "Compiling\u2026";
                        _label.style.color = COMPILING_COLOR;
                        style.backgroundColor = new Color(0.25f, 0.22f, 0.14f, 0.9f);
                    }
                    else if (EditorApplication.isUpdating)
                    {
                        _label.text = "Updating\u2026";
                        _label.style.color = COMPILING_COLOR;
                        style.backgroundColor = new Color(0.25f, 0.22f, 0.14f, 0.9f);
                    }
                    else
                    {
                        _label.text = "Idle";
                        _label.style.color = IDLE_COLOR;
                        style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
                    }
                    _lastState = state;
                    break;
            }
        }
    }
    //! Toolbar button showing MCP setup/config status with gear icon.
    //! Green when fully configured, orange when setup needed.
    //! Opens a styled popup window with configuration options.
    [EditorToolbarElement(ID, typeof(SceneView))]
    sealed class McpConfigButton : VisualElement, IAccessContainerWindow
    {
        public const string ID = "McpConfigButton";
        public EditorWindow containerWindow { get; set; }
        private static Font _mcpFont;
        private readonly Button _button;

        public McpConfigButton()
        {
            _button = new Button(OnClicked);
            // Gear icon (Material Symbols: settings = e8b8)
            _button.text = char.ConvertFromUtf32(0xe8b8);

            if (_mcpFont == null)
                _mcpFont = AssetDatabase.LoadAssetAtPath<Font>(
                    "Packages/io.realvirtual.mcp/Editor/Fonts/MaterialSymbolsOutlined.ttf");

            if (_mcpFont != null)
            {
                _button.style.unityFontDefinition = new StyleFontDefinition(_mcpFont);
                _button.style.fontSize = 18;
            }

            _button.style.borderLeftWidth = 0;
            _button.style.borderRightWidth = 0;
            _button.style.borderTopWidth = 0;
            _button.style.borderBottomWidth = 0;
            _button.style.backgroundColor = new StyleColor(Color.clear);
            _button.style.paddingLeft = 4;
            _button.style.paddingRight = 4;

            Add(_button);

            EditorApplication.update += UpdateStatus;
            UpdateStatus();
        }

        ~McpConfigButton()
        {
            EditorApplication.update -= UpdateStatus;
        }

        private void OnClicked()
        {
            var popup = new McpConfigPopup();
            UnityEditor.PopupWindow.Show(_button.worldBound, popup);
        }

        private void UpdateStatus()
        {
            bool deployed = McpPythonDownloader.IsDeployed();

            if (!deployed)
            {
                _button.style.color = new StyleColor(new Color(0.94f, 0.35f, 0.35f));
                _button.tooltip = "MCP Setup needed - Python not installed";
            }
            else
            {
                _button.style.color = new StyleColor(new Color(0.62f, 0.62f, 0.62f));
                _button.tooltip = "MCP Settings";
            }
        }
    }

    //! Popup window for MCP configuration options.
    //! Shows setup status and action buttons in a styled UI Toolkit popup.
    internal class McpConfigPopup : PopupWindowContent
    {
        private const float WINDOW_WIDTH = 260f;
        private const float WINDOW_HEIGHT = 190f;

        public override Vector2 GetWindowSize() => new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);

        public override void OnGUI(Rect rect) { }

        public override void OnOpen()
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/io.realvirtual.mcp/Editor/McpToolbarPopup.uss");

            var root = new VisualElement();
            root.AddToClassList("popup-root");
            if (uss != null)
                root.styleSheets.Add(uss);

            bool deployed = McpPythonDownloader.IsDeployed();
            bool configured = McpClaudeDesktopConfigurator.IsConfigured;
            bool allGood = deployed && configured;

            // Status bar
            var statusBar = new VisualElement();
            statusBar.AddToClassList(allGood ? "status-bar-running" : "status-bar-stopped");

            var statusRow = new VisualElement();
            statusRow.AddToClassList("status-row");

            var dot = new VisualElement();
            dot.AddToClassList("status-dot");
            dot.AddToClassList(allGood ? "status-dot-running" : "status-dot-stopped");
            statusRow.Add(dot);

            var statusText = new Label(allGood ? "MCP Configured" : "MCP Setup Needed");
            statusText.AddToClassList("status-text");
            statusRow.Add(statusText);

            statusBar.Add(statusRow);

            // Status chips
            var chipsRow = new VisualElement();
            chipsRow.AddToClassList("chips-row");

            var pythonChip = new Label(deployed ? "Python: Installed" : "Python: Not installed");
            pythonChip.AddToClassList(deployed ? "info-chip-connected" : "info-chip");
            chipsRow.Add(pythonChip);

            var claudeChip = new Label(configured ? "Claude: Configured" : "Claude: Not configured");
            claudeChip.AddToClassList(configured ? "info-chip-connected" : "info-chip");
            chipsRow.Add(claudeChip);

            statusBar.Add(chipsRow);
            root.Add(statusBar);

            // Action buttons
            var btnContainer = new VisualElement();
            btnContainer.style.paddingLeft = 12;
            btnContainer.style.paddingRight = 12;
            btnContainer.style.paddingTop = 8;
            btnContainer.style.paddingBottom = 8;

            var configBtn = new Button(() =>
            {
                McpClaudeDesktopConfigurator.ConfigureClaudeDesktop();
                editorWindow.Close();
            });
            configBtn.text = "Configure Claude Code & Desktop";
            configBtn.AddToClassList("refresh-btn");
            configBtn.style.marginBottom = 4;
            configBtn.style.height = 24;
            btnContainer.Add(configBtn);

            var pythonBtn = new Button(() =>
            {
                if (!deployed)
                    McpPythonDownloader.DownloadPythonServer();
                else
                    McpPythonDownloader.UpdatePythonServer();
                editorWindow.Close();
            });
            pythonBtn.text = deployed ? "Update Python Server" : "Download Python Server";
            pythonBtn.AddToClassList("refresh-btn");
            pythonBtn.style.marginBottom = 4;
            pythonBtn.style.height = 24;
            btnContainer.Add(pythonBtn);

            var restartBtn = new Button(() =>
            {
                McpEditorBridge.RestartPythonServer();
                editorWindow.Close();
            });
            restartBtn.text = "Restart Python Server";
            restartBtn.AddToClassList("refresh-btn");
            restartBtn.style.marginBottom = 4;
            restartBtn.style.height = 24;
            btnContainer.Add(restartBtn);

            var folderBtn = new Button(() =>
            {
                EditorUtility.RevealInFinder(McpPythonDownloader.GetPythonServerPath());
                editorWindow.Close();
            });
            folderBtn.text = "Open MCP Folder";
            folderBtn.AddToClassList("refresh-btn");
            folderBtn.style.height = 24;
            btnContainer.Add(folderBtn);

            root.Add(btnContainer);
            editorWindow.rootVisualElement.Add(root);
        }
    }

    //! Menu items for MCP setup and Python server management.
    static class McpMenuItems
    {
        [MenuItem("Tools/realvirtual/MCP/Configure Claude Code && Desktop", false, 900)]
        private static void ConfigureAll()
        {
            McpClaudeDesktopConfigurator.ConfigureClaudeDesktop();
        }

        [MenuItem("Tools/realvirtual/MCP/Download Python Server", false, 902)]
        private static void DownloadPython()
        {
            McpPythonDownloader.DownloadPythonServer();
        }

        [MenuItem("Tools/realvirtual/MCP/Update Python Server", false, 903)]
        private static void UpdatePython()
        {
            McpPythonDownloader.UpdatePythonServer();
        }

        [MenuItem("Tools/realvirtual/MCP/Restart Python Server", false, 904)]
        private static void RestartPython()
        {
            McpEditorBridge.RestartPythonServer();
        }

        [MenuItem("Tools/realvirtual/MCP/Open MCP Folder", false, 920)]
        private static void OpenFolder()
        {
            EditorUtility.RevealInFinder(McpPythonDownloader.GetPythonServerPath());
        }
    }
}
#endif
