// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for creating, deleting, and managing GameObjects.
    //!
    //! Provides commands to create, destroy, duplicate, rename, activate/deactivate GameObjects,
    //! and query detailed information about them. These tools enable AI agents to build and modify scene content.
    public static class GameObjectTools
    {
        //! Reveals a GameObject in the Hierarchy by expanding its parent chain and pinging it.
        private static void RevealInHierarchy(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && go != null)
            {
                UnityEditor.Selection.activeGameObject = go;
                UnityEditor.EditorGUIUtility.PingObject(go);
            }
#endif
        }

        //! Creates a new empty GameObject
        [McpTool("Create a new empty GameObject")]
        public static string GameObjectCreate(
            [McpParam("Name for the new GameObject")] string name,
            [McpParam("Parent name or path (optional)")] string parent = "")
        {
            var go = new GameObject(name);

            if (!string.IsNullOrEmpty(parent))
            {
                var parentGo = ToolHelpers.FindGameObject(parent);
                if (parentGo == null)
                {
                    Object.DestroyImmediate(go);
                    return ToolHelpers.Error($"Parent '{parent}' not found");
                }
                go.transform.SetParent(parentGo.transform);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "MCP: Create GameObject");
#endif

            RevealInHierarchy(go);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["instanceId"] = go.GetInstanceID()
            });
        }

        //! Creates a primitive GameObject (Cube, Sphere, Plane, Cylinder, Capsule)
        [McpTool("Create a primitive GameObject")]
        public static string GameObjectCreatePrimitive(
            [McpParam("Primitive type: Cube, Sphere, Plane, Cylinder, Capsule")] string type,
            [McpParam("Name for the primitive (optional)")] string name = "",
            [McpParam("Parent name or path (optional)")] string parent = "")
        {
            PrimitiveType primitiveType;
            switch (type.ToLower())
            {
                case "cube": primitiveType = PrimitiveType.Cube; break;
                case "sphere": primitiveType = PrimitiveType.Sphere; break;
                case "plane": primitiveType = PrimitiveType.Plane; break;
                case "cylinder": primitiveType = PrimitiveType.Cylinder; break;
                case "capsule": primitiveType = PrimitiveType.Capsule; break;
                default: return ToolHelpers.Error($"Unknown primitive type '{type}'. Use: Cube, Sphere, Plane, Cylinder, Capsule");
            }

            var go = GameObject.CreatePrimitive(primitiveType);

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            if (!string.IsNullOrEmpty(parent))
            {
                var parentGo = ToolHelpers.FindGameObject(parent);
                if (parentGo == null)
                {
                    Object.DestroyImmediate(go);
                    return ToolHelpers.Error($"Parent '{parent}' not found");
                }
                go.transform.SetParent(parentGo.transform);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "MCP: Create Primitive");
#endif

            RevealInHierarchy(go);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["instanceId"] = go.GetInstanceID(),
                ["type"] = type
            });
        }

        //! Destroys a GameObject
        [McpTool("Destroy a GameObject")]
        public static string GameObjectDestroy(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var path = ToolHelpers.GetGameObjectPath(go);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.DestroyObjectImmediate(go);
            else
                Object.Destroy(go);
#else
            Object.Destroy(go);
#endif

            return ToolHelpers.Ok(new JObject
            {
                ["destroyed"] = path
            });
        }

        //! Duplicates a GameObject
        [McpTool("Duplicate a GameObject")]
        public static string GameObjectDuplicate(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Name for the duplicate (optional)")] string newName = "")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var duplicate = Object.Instantiate(go, go.transform.parent);
            if (!string.IsNullOrEmpty(newName))
                duplicate.name = newName;
            else
                duplicate.name = go.name; // Remove "(Clone)" suffix

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RegisterCreatedObjectUndo(duplicate, "MCP: Duplicate GameObject");
#endif

            RevealInHierarchy(duplicate);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = duplicate.name,
                ["path"] = ToolHelpers.GetGameObjectPath(duplicate),
                ["instanceId"] = duplicate.GetInstanceID()
            });
        }

        //! Renames a GameObject
        [McpTool("Rename a GameObject")]
        public static string GameObjectRename(
            [McpParam("GameObject name or path")] string name,
            [McpParam("New name")] string newName)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            if (string.IsNullOrEmpty(newName))
                return ToolHelpers.Error("New name cannot be empty");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go, "MCP: Rename GameObject");
#endif

            go.name = newName;

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            });
        }

        //! Sets a GameObject active or inactive
        [McpTool("Set GameObject active/inactive")]
        public static string GameObjectSetActive(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Active state")] bool active)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go, "MCP: Set Active");
#endif

            go.SetActive(active);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["active"] = go.activeSelf
            });
        }

        //! Gets detailed information about a GameObject
        [McpTool("Get GameObject info")]
        public static string GameObjectGetInfo(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var components = go.GetComponents<Component>();
            var compArr = new JArray();
            foreach (var comp in components)
            {
                if (comp != null)
                    compArr.Add(comp.GetType().Name);
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["active"] = go.activeSelf,
                ["activeInHierarchy"] = go.activeInHierarchy,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["tag"] = go.tag,
                ["childCount"] = go.transform.childCount,
                ["components"] = compArr
            });
        }
        //! Sets the layer of a GameObject
        [McpTool("Set GameObject layer by name")]
        public static string GameObjectSetLayer(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Layer name (e.g. 'Default', 'UI', 'Ignore Raycast')")] string layer)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            int layerIndex = LayerMask.NameToLayer(layer);
            if (layerIndex < 0)
                return ToolHelpers.Error($"Layer '{layer}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go, "MCP: Set Layer");
#endif

            go.layer = layerIndex;

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["layer"] = layer,
                ["layerIndex"] = layerIndex
            });
        }

        //! Sets the tag of a GameObject
        [McpTool("Set GameObject tag")]
        public static string GameObjectSetTag(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Tag name (e.g. 'Player', 'MainCamera', 'Untagged')")] string tag)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            try
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.RecordObject(go, "MCP: Set Tag");
#endif
                go.tag = tag;
            }
            catch (UnityException)
            {
                return ToolHelpers.Error($"Tag '{tag}' is not defined. Add it in Tags & Layers settings.");
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["tag"] = go.tag
            });
        }
    }
}
