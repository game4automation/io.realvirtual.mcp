// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for prefab instantiation and search.
    //!
    //! In edit mode, uses AssetDatabase + PrefabUtility for proper prefab links and undo support.
    //! In play mode, uses Resources.Load for runtime instantiation.
    public static class PrefabTools
    {
        //! Instantiates a prefab
        [McpTool("Instantiate a prefab")]
        public static string PrefabInstantiate(
            [McpParam("Prefab asset path (edit: 'Assets/...prefab', play: Resources-relative path)")] string assetPath,
            [McpParam("Name for instance (optional)")] string name = "",
            [McpParam("Parent name or path (optional)")] string parent = "")
        {
            if (string.IsNullOrEmpty(assetPath))
                return ToolHelpers.Error("Asset path cannot be empty");

            GameObject instance = null;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    return ToolHelpers.Error($"Prefab not found at '{assetPath}'");
                instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
                UnityEditor.Undo.RegisterCreatedObjectUndo(instance, "MCP: Instantiate Prefab");
            }
            else
#endif
            {
                var prefab = UnityEngine.Resources.Load<GameObject>(assetPath);
                if (prefab == null)
                    return ToolHelpers.Error($"Prefab not found in Resources at '{assetPath}'");
                instance = Object.Instantiate(prefab);
            }

            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            if (!string.IsNullOrEmpty(parent))
            {
                var parentGo = ToolHelpers.FindGameObject(parent);
                if (parentGo == null)
                {
                    // Don't destroy the instance - it was already created, just warn
                    return ToolHelpers.Ok(new JObject
                    {
                        ["name"] = instance.name,
                        ["path"] = ToolHelpers.GetGameObjectPath(instance),
                        ["instanceId"] = instance.GetInstanceID(),
                        ["warning"] = $"Parent '{parent}' not found, instantiated at root"
                    });
                }
                instance.transform.SetParent(parentGo.transform);
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = instance.name,
                ["path"] = ToolHelpers.GetGameObjectPath(instance),
                ["instanceId"] = instance.GetInstanceID()
            });
        }

        //! Finds prefabs by search term
        [McpTool("Find prefabs by name")]
        public static string PrefabFind(
            [McpParam("Search term for prefab name")] string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return ToolHelpers.Error("Search term cannot be empty");

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:Prefab {searchTerm}");
            var arr = new JArray();

            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                arr.Add(new JObject
                {
                    ["path"] = path,
                    ["name"] = System.IO.Path.GetFileNameWithoutExtension(path)
                });
            }

            return ToolHelpers.Ok(new JObject
            {
                ["prefabs"] = arr,
                ["count"] = arr.Count,
                ["searchTerm"] = searchTerm
            });
#else
            return ToolHelpers.Error("Prefab search is only available in the Editor");
#endif
        }
    }
}
