// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for manipulating GameObject transforms.
    //!
    //! Provides commands to set position, rotation, scale, reparent, translate, and orient GameObjects.
    //! Rigidbody-aware: uses physics-safe methods when Rigidbody is present in play mode.
    public static class TransformTools
    {
        //! Sets the position of a GameObject
        [McpTool("Set GameObject position")]
        public static string TransformSetPosition(
            [McpParam("GameObject name or path")] string name,
            [McpParam("X position")] float x,
            [McpParam("Y position")] float y,
            [McpParam("Z position")] float z,
            [McpParam("Coordinate space: local or world (default: local)")] string space = "local")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var pos = new Vector3(x, y, z);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Set Position");
#endif

            // Rigidbody-aware: use MovePosition if Rigidbody is present and non-kinematic in play mode
            if (Application.isPlaying && go.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
            {
                rb.MovePosition(space == "world" ? pos : go.transform.TransformPoint(pos));
            }
            else
            {
                if (space == "world")
                    go.transform.position = pos;
                else
                    go.transform.localPosition = pos;
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["localPosition"] = ToolHelpers.Vec3ToJson(go.transform.localPosition),
                ["worldPosition"] = ToolHelpers.Vec3ToJson(go.transform.position)
            });
        }

        //! Sets the rotation of a GameObject (euler angles)
        [McpTool("Set GameObject rotation")]
        public static string TransformSetRotation(
            [McpParam("GameObject name or path")] string name,
            [McpParam("X rotation (degrees)")] float x,
            [McpParam("Y rotation (degrees)")] float y,
            [McpParam("Z rotation (degrees)")] float z,
            [McpParam("Coordinate space: local or world (default: local)")] string space = "local")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var rot = Quaternion.Euler(x, y, z);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Set Rotation");
#endif

            // Rigidbody-aware: use MoveRotation if Rigidbody is present and non-kinematic in play mode
            if (Application.isPlaying && go.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
            {
                rb.MoveRotation(space == "world" ? rot : go.transform.parent != null
                    ? go.transform.parent.rotation * rot
                    : rot);
            }
            else
            {
                if (space == "world")
                    go.transform.rotation = rot;
                else
                    go.transform.localRotation = rot;
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["localRotation"] = ToolHelpers.Vec3ToJson(go.transform.localRotation.eulerAngles),
                ["worldRotation"] = ToolHelpers.Vec3ToJson(go.transform.rotation.eulerAngles)
            });
        }

        //! Sets the local scale of a GameObject
        [McpTool("Set GameObject scale")]
        public static string TransformSetScale(
            [McpParam("GameObject name or path")] string name,
            [McpParam("X scale")] float x,
            [McpParam("Y scale")] float y,
            [McpParam("Z scale")] float z)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Set Scale");
#endif

            go.transform.localScale = new Vector3(x, y, z);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["scale"] = ToolHelpers.Vec3ToJson(go.transform.localScale)
            });
        }

        //! Sets the parent of a GameObject (empty string = unparent to root)
        [McpTool("Set GameObject parent")]
        public static string TransformSetParent(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Parent name or path (empty = root)")] string parent)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            if (string.IsNullOrEmpty(parent))
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.SetTransformParent(go.transform, null, "MCP: Unparent");
                else
                    go.transform.SetParent(null);
#else
                go.transform.SetParent(null);
#endif
            }
            else
            {
                var parentGo = ToolHelpers.FindGameObject(parent);
                if (parentGo == null)
                    return ToolHelpers.Error($"Parent '{parent}' not found");

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.SetTransformParent(go.transform, parentGo.transform, "MCP: Set Parent");
                else
                    go.transform.SetParent(parentGo.transform);
#else
                go.transform.SetParent(parentGo.transform);
#endif
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["parent"] = go.transform.parent != null ? go.transform.parent.name : "(root)"
            });
        }

        //! Translates a GameObject by an offset
        [McpTool("Translate a GameObject")]
        public static string TransformTranslate(
            [McpParam("GameObject name or path")] string name,
            [McpParam("X offset")] float x,
            [McpParam("Y offset")] float y,
            [McpParam("Z offset")] float z,
            [McpParam("Coordinate space: local or world (default: local)")] string space = "local")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Translate");
#endif

            go.transform.Translate(new Vector3(x, y, z), space == "world" ? Space.World : Space.Self);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["localPosition"] = ToolHelpers.Vec3ToJson(go.transform.localPosition),
                ["worldPosition"] = ToolHelpers.Vec3ToJson(go.transform.position)
            });
        }

        //! Makes a GameObject look at another GameObject
        [McpTool("Make GameObject look at target")]
        public static string TransformLookAt(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Target GameObject name or path")] string targetName)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var target = ToolHelpers.FindGameObject(targetName);
            if (target == null)
                return ToolHelpers.Error($"Target '{targetName}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Look At");
#endif

            go.transform.LookAt(target.transform);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["rotation"] = ToolHelpers.Vec3ToJson(go.transform.rotation.eulerAngles),
                ["target"] = targetName
            });
        }
    }
}
