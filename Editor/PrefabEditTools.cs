// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for opening and editing prefabs in Unity's Prefab Stage.
    //!
    //! When a prefab is opened, the existing scene tools (scene_hierarchy, component_get/set,
    //! editor_select, editor_focus) work on the prefab contents automatically.
    public static class PrefabEditTools
    {
        //! Opens a prefab asset in Unity's Prefab Stage for editing
        [McpTool("Open prefab for editing", "prefab_open")]
        public static string PrefabOpen(
            [McpParam("Prefab asset path (e.g. Packages/io.realvirtual.starter/Tests/MyPrefab.prefab)")] string path)
        {
            if (string.IsNullOrEmpty(path))
                return ToolHelpers.Error("Prefab path cannot be empty");

            if (!path.EndsWith(".prefab"))
                path += ".prefab";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return ToolHelpers.Error($"Prefab not found at '{path}'");

            // Open prefab in Prefab Stage
            AssetDatabase.OpenAsset(prefab);

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return ToolHelpers.Error("Failed to open prefab stage");

            var root = stage.prefabContentsRoot;
            var children = root.GetComponentsInChildren<Transform>(true);

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Opened prefab '{root.name}' for editing",
                ["path"] = path,
                ["rootName"] = root.name,
                ["childCount"] = children.Length - 1,
                ["hint"] = "Use scene_hierarchy, component_get/set, editor_select to work with prefab contents. Call prefab_save to save, prefab_close to exit."
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets the current prefab stage info (if a prefab is open for editing)
        [McpTool("Get current prefab stage info", "prefab_stage")]
        public static string PrefabStage()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new JObject
                {
                    ["status"] = "ok",
                    ["inPrefabStage"] = false,
                    ["message"] = "Not in prefab editing mode"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var root = stage.prefabContentsRoot;
            var children = root.GetComponentsInChildren<Transform>(true);

            return new JObject
            {
                ["status"] = "ok",
                ["inPrefabStage"] = true,
                ["prefabPath"] = stage.assetPath,
                ["rootName"] = root.name,
                ["childCount"] = children.Length - 1
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Saves the currently open prefab and stays in prefab editing mode
        [McpTool("Save current prefab", "prefab_save")]
        public static string PrefabSave()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return ToolHelpers.Error("Not in prefab editing mode");

            // Mark dirty and save
            EditorUtility.SetDirty(stage.prefabContentsRoot);
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Saved prefab '{stage.prefabContentsRoot.name}'",
                ["path"] = stage.assetPath
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Closes the prefab stage and returns to the previous scene
        [McpTool("Close prefab editor", "prefab_close")]
        public static string PrefabClose(
            [McpParam("Save before closing")] bool save = true)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return ToolHelpers.Error("Not in prefab editing mode");

            var prefabPath = stage.assetPath;
            var prefabName = stage.prefabContentsRoot.name;

            if (save)
            {
                EditorUtility.SetDirty(stage.prefabContentsRoot);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            }

            StageUtility.GoToMainStage();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Closed prefab '{prefabName}'",
                ["path"] = prefabPath,
                ["saved"] = save
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
