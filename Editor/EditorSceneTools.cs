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
        //! Saves the current scene
        [McpTool("Save current scene")]
        public static string EditorSaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene);

            if (saved)
            {
                return new JObject
                {
                    ["status"] = "ok",
                    ["message"] = "Scene saved",
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
    }
}
