// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for capturing screenshots of the Unity Editor, Game View and Scene View.
    //!
    //! Screenshots are returned as base64-encoded images via the _image convention,
    //! which the Python bridge automatically converts to MCP ImageContent.
    //! Files are also saved to the .screenshots/ directory in the project root.
    public static class ScreenshotTools
    {
        private static readonly string ScreenshotDir =
            Path.Combine(Application.dataPath, "..", ".screenshots");

        //! Captures the full Unity Editor window or a specific panel using Unity's internal API.
        //! This is DPI-aware and works correctly on high-DPI displays.
        [McpTool("Capture Unity Editor window screenshot")]
        public static string ScreenshotEditor(
            [McpParam("Optional file path to save the screenshot")] string save_path = "",
            [McpParam("Panel to capture: all, inspector, scene, game, console, hierarchy, project (default: all)")] string panel = "all")
        {
            try
            {
                Rect captureRect;
                string panelName;

                if (panel.Equals("all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(panel))
                {
                    captureRect = GetMainWindowRect();
                    panelName = "editor";
                }
                else
                {
                    captureRect = GetPanelRect(panel, out panelName);
                }

                if (captureRect.width <= 0 || captureRect.height <= 0)
                    return ToolHelpers.Error($"Could not determine rect for panel '{panel}'");

                // ReadScreenPixel uses logical screen coordinates for position,
                // but returns physical pixels when size is scaled by DPI.
                // For the full editor window this works correctly, but for individual
                // panels we use logical sizes to avoid capturing beyond panel bounds.
                float dpiScale = EditorGUIUtility.pixelsPerPoint;
                bool isFullEditor = panel.Equals("all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(panel);
                int width = isFullEditor ? (int)(captureRect.width * dpiScale) : (int)captureRect.width;
                int height = isFullEditor ? (int)(captureRect.height * dpiScale) : (int)captureRect.height;
                var screenPos = captureRect.position;

                // Use InternalEditorUtility.ReadScreenPixel to capture screen content
                Color[] pixels = InternalEditorUtility.ReadScreenPixel(screenPos, width, height);

                // Create texture from pixel data
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.SetPixels(pixels);
                tex.Apply();

                byte[] imageBytes = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                string base64 = Convert.ToBase64String(imageBytes);
                string savedPath = null;

                if (!string.IsNullOrEmpty(save_path))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(save_path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllBytes(save_path, imageBytes);
                        savedPath = save_path;
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Failed to save screenshot to '{save_path}': {ex.Message}");
                    }
                }

                if (savedPath == null)
                    savedPath = SaveScreenshot(imageBytes, panelName, "png");

                return new JObject
                {
                    ["status"] = "ok",
                    ["_image"] = base64,
                    ["_mimeType"] = "image/png",
                    ["width"] = width,
                    ["height"] = height,
                    ["panel"] = panelName,
                    ["format"] = "png",
                    ["savedTo"] = savedPath
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return ToolHelpers.Error($"Editor screenshot failed: {ex.Message}");
            }
        }

        private static Rect GetMainWindowRect()
        {
            // Use reflection to get the main Unity Editor ContainerWindow position
            var containerWindowType = typeof(Editor).Assembly.GetType("UnityEditor.ContainerWindow");
            if (containerWindowType != null)
            {
                var windowsField = containerWindowType.GetProperty("windows",
                    BindingFlags.Static | BindingFlags.Public);
                if (windowsField != null)
                {
                    var windows = windowsField.GetValue(null) as Array;
                    if (windows != null)
                    {
                        foreach (var win in windows)
                        {
                            var showModeField = containerWindowType.GetField("m_ShowMode",
                                BindingFlags.Instance | BindingFlags.NonPublic);
                            if (showModeField != null)
                            {
                                int showMode = (int)showModeField.GetValue(win);
                                // ShowMode 4 = MainWindow
                                if (showMode == 4)
                                {
                                    var positionProp = containerWindowType.GetProperty("position",
                                        BindingFlags.Instance | BindingFlags.Public);
                                    if (positionProp != null)
                                    {
                                        return (Rect)positionProp.GetValue(win);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Fallback: combine all visible EditorWindows to get bounds
            return GetCombinedWindowBounds();
        }

        private static Rect GetCombinedWindowBounds()
        {
            var allWindows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
            if (allWindows.Length == 0)
                return new Rect(0, 0, 1920, 1080);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var win in allWindows)
            {
                var pos = win.position;
                if (pos.width <= 0 || pos.height <= 0) continue;
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x + pos.width);
                maxY = Mathf.Max(maxY, pos.y + pos.height);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static Rect GetPanelRect(string panel, out string panelName)
        {
            panelName = panel.ToLowerInvariant();
            Type windowType = null;

            switch (panelName)
            {
                case "inspector":
                    windowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
                    break;
                case "scene":
                    windowType = typeof(SceneView);
                    break;
                case "game":
                    windowType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                    break;
                case "console":
                    windowType = typeof(Editor).Assembly.GetType("UnityEditor.ConsoleWindow");
                    break;
                case "hierarchy":
                    windowType = typeof(Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                    break;
                case "project":
                    windowType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
                    break;
                default:
                    panelName = panel;
                    return new Rect(0, 0, 0, 0);
            }

            if (windowType == null)
                return new Rect(0, 0, 0, 0);

            var windows = UnityEngine.Resources.FindObjectsOfTypeAll(windowType);
            if (windows.Length == 0)
                return new Rect(0, 0, 0, 0);

            var editorWindow = windows[0] as EditorWindow;
            if (editorWindow == null)
                return new Rect(0, 0, 0, 0);

            return editorWindow.position;
        }

        //! Captures the Game View from a camera and returns as base64 image
        [McpTool("Capture game view screenshot from camera")]
        public static string ScreenshotGame(
            [McpParam("Width in pixels")] int width = 1024,
            [McpParam("Height in pixels")] int height = 768,
            [McpParam("Camera name (empty = Main Camera)")] string camera_name = "",
            [McpParam("Image format: png or jpg")] string format = "png",
            [McpParam("JPG quality 1-100")] int quality = 85)
        {
            Camera camera;
            string cameraLabel;

            if (string.IsNullOrEmpty(camera_name))
            {
                camera = Camera.main;
                cameraLabel = camera != null ? camera.name : "Main Camera";
            }
            else
            {
                var go = ToolHelpers.FindGameObject(camera_name);
                if (go == null)
                    return ToolHelpers.Error($"GameObject '{camera_name}' not found");
                camera = go.GetComponent<Camera>();
                if (camera == null)
                    return ToolHelpers.Error($"No Camera component on '{camera_name}'");
                cameraLabel = camera_name;
            }

            if (camera == null)
                return ToolHelpers.Error("No camera found. Tag a camera as MainCamera or specify camera_name.");

            return CaptureCamera(camera, cameraLabel, width, height, format, quality, "game");
        }

        //! Captures the Scene View camera and returns as base64 image
        [McpTool("Capture scene view screenshot")]
        public static string ScreenshotScene(
            [McpParam("Width in pixels")] int width = 1024,
            [McpParam("Height in pixels")] int height = 768,
            [McpParam("Image format: png or jpg")] string format = "png",
            [McpParam("JPG quality 1-100")] int quality = 85)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return ToolHelpers.Error("No active Scene View found. Open a Scene View in the Editor.");

            var camera = sceneView.camera;
            if (camera == null)
                return ToolHelpers.Error("Scene View camera is not available.");

            return CaptureCamera(camera, "SceneView", width, height, format, quality, "scene");
        }

        private static string CaptureCamera(Camera camera, string cameraLabel,
            int width, int height, string format, int quality, string prefix)
        {
            width = Mathf.Clamp(width, 64, 7680);
            height = Mathf.Clamp(height, 64, 4320);
            quality = Mathf.Clamp(quality, 1, 100);
            bool isJpg = format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                         format.Equals("jpeg", StringComparison.OrdinalIgnoreCase);

            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;

            RenderTexture rt = null;
            Texture2D tex = null;

            try
            {
                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] imageBytes = isJpg ? tex.EncodeToJPG(quality) : tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(imageBytes);

                string mimeType = isJpg ? "image/jpeg" : "image/png";
                string ext = isJpg ? "jpg" : "png";

                // Save to file
                string savedPath = SaveScreenshot(imageBytes, prefix, ext);

                return new JObject
                {
                    ["status"] = "ok",
                    ["_image"] = base64,
                    ["_mimeType"] = mimeType,
                    ["width"] = width,
                    ["height"] = height,
                    ["camera"] = cameraLabel,
                    ["format"] = ext,
                    ["savedTo"] = savedPath
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return ToolHelpers.Error($"Screenshot failed: {ex.Message}");
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static string SaveScreenshot(byte[] imageBytes, string prefix, string ext)
        {
            try
            {
                if (!Directory.Exists(ScreenshotDir))
                    Directory.CreateDirectory(ScreenshotDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"{prefix}_{timestamp}.{ext}";
                string fullPath = Path.Combine(ScreenshotDir, filename);
                File.WriteAllBytes(fullPath, imageBytes);
                return fullPath;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to save screenshot: {ex.Message}");
                return null;
            }
        }
    }
}
