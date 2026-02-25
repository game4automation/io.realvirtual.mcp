// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! Shared helper methods for all MCP tools.
    //!
    //! Provides common utilities for finding GameObjects, building JSON responses,
    //! and converting Unity types to JSON. Used by all tool classes to avoid duplication.
    public static class ToolHelpers
    {
        //! Finds a GameObject by name or hierarchy path (e.g. "Robot/Rotobpath/PickPos").
        //! Path matching takes priority over name matching to correctly resolve objects
        //! that share the same name but exist in different hierarchy positions.
        public static GameObject FindGameObject(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return null;

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
