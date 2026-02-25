// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace realvirtual.MCP.Tools
{
    //! Shared helper methods for all MCP tools.
    //!
    //! Provides common utilities for finding GameObjects, building JSON responses,
    //! and converting Unity types to JSON. Used by all tool classes to avoid duplication.
    //! Prefab-stage-aware: when a prefab is open for editing, searches operate on
    //! the prefab contents instead of the main scene.
    public static class ToolHelpers
    {
        //! Returns true if Unity is currently in prefab editing mode.
        public static bool IsInPrefabStage()
        {
#if UNITY_EDITOR
            return PrefabStageUtility.GetCurrentPrefabStage() != null;
#else
            return false;
#endif
        }

        //! Returns the prefab stage root GameObject, or null if not in prefab stage.
        public static GameObject GetPrefabStageRoot()
        {
#if UNITY_EDITOR
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null ? stage.prefabContentsRoot : null;
#else
            return null;
#endif
        }

        //! Returns all GameObjects in the current context (prefab stage or scene).
        //! When in prefab stage, returns only objects within the prefab hierarchy.
        public static GameObject[] GetAllGameObjectsInContext()
        {
#if UNITY_EDITOR
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var root = stage.prefabContentsRoot;
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var result = new GameObject[transforms.Length];
                for (int i = 0; i < transforms.Length; i++)
                    result[i] = transforms[i].gameObject;
                return result;
            }
#endif
            return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        //! Finds a GameObject by name or hierarchy path (e.g. "Robot/Rotobpath/PickPos").
        //! Path matching takes priority over name matching to correctly resolve objects
        //! that share the same name but exist in different hierarchy positions.
        //! Prefab-stage-aware: when a prefab is open, searches within prefab contents.
        public static GameObject FindGameObject(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return null;

#if UNITY_EDITOR
            // When in prefab stage, search only within prefab contents
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                return FindInPrefabStage(stage, nameOrPath);
#endif

            // Fast path: GameObject.Find supports "/" path notation and only finds active objects
            var go = GameObject.Find(nameOrPath);
            if (go != null)
                return go;

            // Fallback: search ALL objects including inactive (Unity 6 API)
            // Check full hierarchy path FIRST, then fall back to name-only matching.
            // This ensures disambiguation when multiple objects share the same name.
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            GameObject nameMatch = null;
            foreach (var obj in allObjects)
            {
                if (GetGameObjectPath(obj) == nameOrPath)
                    return obj;
                if (nameMatch == null && obj.name == nameOrPath)
                    nameMatch = obj;
            }

            return nameMatch;
        }

#if UNITY_EDITOR
        //! Searches for a GameObject within the prefab stage by name or path.
        //! In prefab stage, paths are relative to the prefab root (e.g. "Root/Child/SubChild").
        private static GameObject FindInPrefabStage(PrefabStage stage, string nameOrPath)
        {
            var root = stage.prefabContentsRoot;

            // Direct match on root name
            if (root.name == nameOrPath)
                return root;

            // Search all children (including inactive)
            var transforms = root.GetComponentsInChildren<Transform>(true);
            GameObject nameMatch = null;
            foreach (var t in transforms)
            {
                var obj = t.gameObject;
                if (GetGameObjectPath(obj) == nameOrPath)
                    return obj;
                if (nameMatch == null && obj.name == nameOrPath)
                    nameMatch = obj;
            }

            return nameMatch;
        }
#endif

        //! Gets the full hierarchy path of a GameObject
        public static string GetGameObjectPath(GameObject obj)
        {
            var path = obj.name;
            var parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        //! Converts a Vector3 to a JObject with x, y, z properties
        public static JObject Vec3ToJson(Vector3 v)
        {
            return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        }

        //! Converts a Quaternion to a JObject with euler angle x, y, z properties
        public static JObject QuatToJson(Quaternion q)
        {
            var euler = q.eulerAngles;
            return new JObject { ["x"] = euler.x, ["y"] = euler.y, ["z"] = euler.z };
        }

        //! Creates an error JSON response string
        public static string Error(string message)
        {
            return new JObject
            {
                ["status"] = "error",
                ["error"] = message
            }.ToString(Formatting.None);
        }

        //! Creates a success JSON response string with data
        public static string Ok(JObject data)
        {
            data["status"] = "ok";
            return data.ToString(Formatting.None);
        }

        //! Creates a success JSON response string with a message
        public static string Ok(string message)
        {
            return new JObject
            {
                ["status"] = "ok",
                ["message"] = message
            }.ToString(Formatting.None);
        }
    }
}
