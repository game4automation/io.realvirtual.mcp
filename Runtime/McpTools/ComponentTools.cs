// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using realvirtual.MCP.Serialization;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for generic component property access.
    //!
    //! Provides commands to get and set properties on any MonoBehaviour component using JSON serialization.
    //! These tools enable AI agents to inspect and modify any component without needing hand-coded MCP tools per type.
    public static class ComponentTools
    {
        //! Selects a GameObject in the Hierarchy so the user can see changes in the Inspector.
        private static void SelectInHierarchy(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && go != null)
                UnityEditor.Selection.activeGameObject = go;
#endif
        }

        //! Gets component properties as JSON
        [McpTool("Get component properties")]
        public static string ComponentGet(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name,
            [McpParam("Component type (e.g. 'Drive')")] string componentType)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            try
            {
                var result = ComponentSerializer.Serialize(go, componentType);
                if (result["error"] != null)
                    return result.ToString(Formatting.None);

                return ToolHelpers.Ok(new JObject
                {
                    ["gameObject"] = go.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(go),
                    ["component"] = componentType,
                    ["properties"] = result
                });
            }
            catch (System.Exception ex)
            {
                return ToolHelpers.Error($"Serialization failed: {ex.Message}");
            }
        }

        //! Sets component properties from JSON
        [McpTool("Set component properties")]
        public static string ComponentSet(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name,
            [McpParam("Component type (e.g. 'Drive')")] string componentType,
            [McpParam("JSON with property values")] string properties)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            try
            {
                var data = JObject.Parse(properties);
                var errors = ComponentDeserializer.Deserialize(go, componentType, data);
                SelectInHierarchy(go);

                if (errors.Count > 0)
                    return ToolHelpers.Error(string.Join("; ", errors));

                return ToolHelpers.Ok(new JObject
                {
                    ["gameObject"] = go.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(go),
                    ["component"] = componentType
                });
            }
            catch (JsonReaderException ex)
            {
                return ToolHelpers.Error($"Invalid JSON: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                return ToolHelpers.Error($"Deserialization failed: {ex.Message}");
            }
        }

        //! Gets all components on GameObject
        [McpTool("Get all components on GameObject")]
        public static string ComponentGetAll(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            try
            {
                var result = ComponentSerializer.SerializeAll(go);
                return ToolHelpers.Ok(new JObject
                {
                    ["gameObject"] = go.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(go),
                    ["components"] = result
                });
            }
            catch (System.Exception ex)
            {
                return ToolHelpers.Error($"Serialization failed: {ex.Message}");
            }
        }

        //! Sets properties on multiple components
        [McpTool("Set properties on multiple components")]
        public static string ComponentSetAll(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name,
            [McpParam("JSON keyed by component type")] string properties)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            try
            {
                var data = JObject.Parse(properties);
                var errors = ComponentDeserializer.DeserializeAll(go, data);

                if (errors.Count > 0)
                    return ToolHelpers.Error(string.Join("; ", errors));

                return ToolHelpers.Ok(new JObject
                {
                    ["gameObject"] = name
                });
            }
            catch (JsonReaderException ex)
            {
                return ToolHelpers.Error($"Invalid JSON: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                return ToolHelpers.Error($"Deserialization failed: {ex.Message}");
            }
        }
        //! Adds a component to a GameObject by type name
        [McpTool("Add component to GameObject by type name")]
        public static string ComponentAdd(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name,
            [McpParam("Component type name (e.g. 'AudioSource', 'Light', 'Drive')")] string componentType)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var type = Serialization.McpTypeResolver.Resolve(componentType);
            if (type == null)
                return ToolHelpers.Error($"Type '{componentType}' not found");

            if (!typeof(Component).IsAssignableFrom(type))
                return ToolHelpers.Error($"'{componentType}' is not a Component type");

            // Check for duplicate (single-instance components like Rigidbody)
            if (go.GetComponent(type) != null && typeof(UnityEngine.Rigidbody).IsAssignableFrom(type))
                return ToolHelpers.Error($"'{componentType}' already exists on '{name}'");

            Component comp;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                comp = UnityEditor.Undo.AddComponent(go, type);
            else
#endif
                comp = go.AddComponent(type);

            if (comp == null)
                return ToolHelpers.Error($"Failed to add '{componentType}' to '{name}'");

            SelectInHierarchy(go);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["component"] = comp.GetType().Name,
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            });
        }

        //! Removes a component from a GameObject by type name
        [McpTool("Remove component from GameObject by type name")]
        public static string ComponentRemove(
            [McpParam("GameObject hierarchy path (e.g. 'Robot/Rotobpath/PickPos'). Use full path to disambiguate objects with the same name.")] string name,
            [McpParam("Component type name (e.g. 'AudioSource', 'Light')")] string componentType)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var type = Serialization.McpTypeResolver.Resolve(componentType);
            if (type == null)
                return ToolHelpers.Error($"Type '{componentType}' not found");

            var comp = go.GetComponent(type);
            if (comp == null)
                return ToolHelpers.Error($"No '{componentType}' found on '{name}'");

            // Prevent removing Transform
            if (comp is Transform)
                return ToolHelpers.Error("Cannot remove Transform component");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.DestroyObjectImmediate(comp);
            else
#endif
                Object.DestroyImmediate(comp);

            return ToolHelpers.Ok(new JObject
            {
                ["gameObject"] = name,
                ["removed"] = componentType
            });
        }
    }
}
