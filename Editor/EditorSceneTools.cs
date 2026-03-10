// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for Unity Editor scene operations.
    //!
    //! Provides commands to save scenes, undo operations, select and focus GameObjects
    //! in the Unity Editor. These tools are Editor-only (excluded from builds by assembly definition).
    public static class EditorSceneTools
    {
        //! Saves the current scene. If path is provided, saves as a new scene file (Save As).
        [McpTool("Save current scene")]
        public static string EditorSaveScene(
            [McpParam("Optional: asset path to save as (e.g. 'Assets/Scenes/MyScene.unity'). If empty, saves to current path.")] string path = "")
        {
            var scene = SceneManager.GetActiveScene();

            bool saved;
            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith(".unity"))
                    path += ".unity";

                // Ensure directory exists
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                saved = EditorSceneManager.SaveScene(scene, path);
            }
            else
            {
                saved = EditorSceneManager.SaveScene(scene);
            }

            if (saved)
            {
                scene = SceneManager.GetActiveScene();
                return new JObject
                {
                    ["status"] = "ok",
                    ["message"] = string.IsNullOrEmpty(path) ? "Scene saved" : $"Scene saved as '{path}'",
                    ["scenePath"] = scene.path
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            return new JObject
            {
                ["status"] = "error",
                ["error"] = "Failed to save scene"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Performs an undo operation
        [McpTool("Undo last operation")]
        public static string EditorUndo()
        {
            Undo.PerformUndo();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = "Undo performed"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Selects a GameObject in the Editor hierarchy
        [McpTool("Select GameObject in hierarchy")]
        public static string EditorSelect(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            Selection.activeGameObject = go;

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Selected '{go.name}'",
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Opens a scene by asset path
        [McpTool("Open scene by path", "editor_open_scene")]
        public static string EditorOpenScene(
            [McpParam("Scene asset path (e.g. Assets/Scenes/MyScene.unity)")] string path,
            [McpParam("Save current scene before opening")] bool save = true)
        {
            if (string.IsNullOrEmpty(path))
                return ToolHelpers.Error("Scene path cannot be empty");

            if (!path.EndsWith(".unity"))
                path += ".unity";

            if (!System.IO.File.Exists(path))
                return ToolHelpers.Error($"Scene not found at '{path}'");

            if (save)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Opened scene '{scene.name}'",
                ["scenePath"] = scene.path,
                ["rootCount"] = scene.GetRootGameObjects().Length
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Focuses the Scene view camera on a GameObject
        [McpTool("Focus camera on GameObject")]
        public static string EditorFocus(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            Selection.activeGameObject = go;
            SceneView.FrameLastActiveSceneView();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Focused on '{go.name}'",
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
        //! Executes a Unity Editor menu item by path
        [McpTool("Execute Unity Editor menu item by path", "editor_execute_menu")]
        public static string EditorExecuteMenu(
            [McpParam("Menu item path (e.g. 'Window/General/Console')")] string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
                return ToolHelpers.Error("Menu path cannot be empty");

            bool executed = EditorApplication.ExecuteMenuItem(menuPath);
            if (!executed)
                return ToolHelpers.Error($"Menu item '{menuPath}' not found or could not be executed");

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Executed menu: {menuPath}"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Invokes a static method by fully qualified class and method name
        [McpTool("Invoke a static method by class and method name", "editor_invoke_method")]
        public static string EditorInvokeMethod(
            [McpParam("Fully qualified class name (e.g. 'realvirtual.ProjectBuilder')")] string className,
            [McpParam("Static method name (e.g. 'RunTests')")] string methodName)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName))
                return ToolHelpers.Error("Class name and method name are required");

            // Find the type across all loaded assemblies
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(className);
                if (type != null)
                    break;
            }

            if (type == null)
                return ToolHelpers.Error($"Type '{className}' not found");

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                return ToolHelpers.Error($"Static method '{methodName}' not found on '{className}'");

            try
            {
                var result = method.Invoke(null, null);
                var response = new JObject
                {
                    ["status"] = "ok",
                    ["message"] = $"Invoked {className}.{methodName}()"
                };

                if (result != null)
                    response["result"] = result.ToString();

                return response.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return ToolHelpers.Error($"Method execution failed: {inner.Message}");
            }
        }

        //! Gets the current scene camera position, rotation, pivot and size
        [McpTool("Get scene camera view", "editor_get_camera")]
        public static string EditorGetCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                return ToolHelpers.Error("No active SceneView found");

            var pos = sv.camera.transform.position;
            var rot = sv.camera.transform.rotation.eulerAngles;
            var pivot = sv.pivot;
            var pivotRot = sv.rotation.eulerAngles;

            return new JObject
            {
                ["status"] = "ok",
                ["position"] = new JObject { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z },
                ["rotation"] = new JObject { ["x"] = rot.x, ["y"] = rot.y, ["z"] = rot.z },
                ["pivot"] = new JObject { ["x"] = pivot.x, ["y"] = pivot.y, ["z"] = pivot.z },
                ["pivotRotation"] = new JObject { ["x"] = pivotRot.x, ["y"] = pivotRot.y, ["z"] = pivotRot.z },
                ["size"] = sv.size,
                ["orthographic"] = sv.orthographic
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Sets the scene camera view by pivot, rotation and size
        [McpTool("Set scene camera view", "editor_set_camera")]
        public static string EditorSetCamera(
            [McpParam("Pivot X position")] float pivotX = float.NaN,
            [McpParam("Pivot Y position")] float pivotY = float.NaN,
            [McpParam("Pivot Z position")] float pivotZ = float.NaN,
            [McpParam("Rotation X (pitch in degrees)")] float rotationX = float.NaN,
            [McpParam("Rotation Y (yaw in degrees)")] float rotationY = float.NaN,
            [McpParam("Rotation Z (roll in degrees)")] float rotationZ = float.NaN,
            [McpParam("Camera zoom size")] float size = float.NaN,
            [McpParam("Orthographic view")] bool orthographic = false)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                return ToolHelpers.Error("No active SceneView found");

            if (!float.IsNaN(pivotX) || !float.IsNaN(pivotY) || !float.IsNaN(pivotZ))
            {
                var pivot = sv.pivot;
                if (!float.IsNaN(pivotX)) pivot.x = pivotX;
                if (!float.IsNaN(pivotY)) pivot.y = pivotY;
                if (!float.IsNaN(pivotZ)) pivot.z = pivotZ;
                sv.pivot = pivot;
            }

            if (!float.IsNaN(rotationX) || !float.IsNaN(rotationY) || !float.IsNaN(rotationZ))
            {
                var euler = sv.rotation.eulerAngles;
                if (!float.IsNaN(rotationX)) euler.x = rotationX;
                if (!float.IsNaN(rotationY)) euler.y = rotationY;
                if (!float.IsNaN(rotationZ)) euler.z = rotationZ;
                sv.rotation = Quaternion.Euler(euler);
            }

            if (!float.IsNaN(size))
                sv.size = size;

            sv.orthographic = orthographic;
            sv.Repaint();

            var newPivot = sv.pivot;
            var newRot = sv.rotation.eulerAngles;
            return new JObject
            {
                ["status"] = "ok",
                ["pivot"] = new JObject { ["x"] = newPivot.x, ["y"] = newPivot.y, ["z"] = newPivot.z },
                ["pivotRotation"] = new JObject { ["x"] = newRot.x, ["y"] = newRot.y, ["z"] = newRot.z },
                ["size"] = sv.size,
                ["orthographic"] = sv.orthographic
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
