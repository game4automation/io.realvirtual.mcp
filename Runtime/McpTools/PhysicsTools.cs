// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for adding and removing physics components.
    //!
    //! Provides commands to add/remove Rigidbody and Colliders.
    //! Includes guards for duplicate Rigidbody and missing MeshFilter for MeshColliders.
    public static class PhysicsTools
    {
        //! Adds a Rigidbody to a GameObject
        [McpTool("Add Rigidbody to GameObject")]
        public static string PhysicsAddRigidbody(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Mass (default 1)")] float mass = 1f,
            [McpParam("Use gravity (default true)")] bool useGravity = true,
            [McpParam("Is kinematic (default false)")] bool isKinematic = false)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            if (go.GetComponent<Rigidbody>() != null)
                return ToolHelpers.Error($"'{name}' already has a Rigidbody");

            Rigidbody rb;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                rb = UnityEditor.Undo.AddComponent<Rigidbody>(go);
            else
                rb = go.AddComponent<Rigidbody>();
#else
            rb = go.AddComponent<Rigidbody>();
#endif

            rb.mass = mass;
            rb.useGravity = useGravity;
            rb.isKinematic = isKinematic;

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["mass"] = rb.mass,
                ["useGravity"] = rb.useGravity,
                ["isKinematic"] = rb.isKinematic
            });
        }

        //! Adds a Collider to a GameObject
        [McpTool("Add Collider to GameObject")]
        public static string PhysicsAddCollider(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Collider type: Box, Sphere, Capsule, Mesh")] string type,
            [McpParam("Is trigger (default false)")] bool isTrigger = false)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            Collider collider;
            switch (type.ToLower())
            {
                case "box":
#if UNITY_EDITOR
                    collider = !Application.isPlaying
                        ? UnityEditor.Undo.AddComponent<BoxCollider>(go)
                        : go.AddComponent<BoxCollider>();
#else
                    collider = go.AddComponent<BoxCollider>();
#endif
                    break;

                case "sphere":
#if UNITY_EDITOR
                    collider = !Application.isPlaying
                        ? UnityEditor.Undo.AddComponent<SphereCollider>(go)
                        : go.AddComponent<SphereCollider>();
#else
                    collider = go.AddComponent<SphereCollider>();
#endif
                    break;

                case "capsule":
#if UNITY_EDITOR
                    collider = !Application.isPlaying
                        ? UnityEditor.Undo.AddComponent<CapsuleCollider>(go)
                        : go.AddComponent<CapsuleCollider>();
#else
                    collider = go.AddComponent<CapsuleCollider>();
#endif
                    break;

                case "mesh":
                    if (go.GetComponent<MeshFilter>() == null)
                        return ToolHelpers.Error($"'{name}' has no MeshFilter - required for MeshCollider");
#if UNITY_EDITOR
                    collider = !Application.isPlaying
                        ? UnityEditor.Undo.AddComponent<MeshCollider>(go)
                        : go.AddComponent<MeshCollider>();
#else
                    collider = go.AddComponent<MeshCollider>();
#endif
                    break;

                default:
                    return ToolHelpers.Error($"Unknown collider type '{type}'. Use: Box, Sphere, Capsule, Mesh");
            }

            collider.isTrigger = isTrigger;

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["colliderType"] = collider.GetType().Name,
                ["isTrigger"] = collider.isTrigger
            });
        }

        //! Removes the Rigidbody from a GameObject
        [McpTool("Remove Rigidbody from GameObject")]
        public static string PhysicsRemoveRigidbody(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return ToolHelpers.Error($"'{name}' has no Rigidbody");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.DestroyObjectImmediate(rb);
            else
                Object.Destroy(rb);
#else
            Object.Destroy(rb);
#endif

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["removed"] = "Rigidbody"
            });
        }

        //! Removes all Colliders from a GameObject
        [McpTool("Remove all Colliders from GameObject")]
        public static string PhysicsRemoveColliders(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var colliders = go.GetComponents<Collider>();
            if (colliders.Length == 0)
                return ToolHelpers.Error($"'{name}' has no Colliders");

            int count = colliders.Length;
            foreach (var col in colliders)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.DestroyObjectImmediate(col);
                else
                    Object.Destroy(col);
#else
                Object.Destroy(col);
#endif
            }

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["removedCount"] = count
            });
        }
    }
}
