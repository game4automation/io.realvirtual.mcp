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

        //! Sets the sibling index of a GameObject to control its order among siblings
        [McpTool("Set GameObject sibling index (hierarchy order among siblings)")]
        public static string TransformSetSiblingIndex(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Sibling index (0 = first child). Use -1 for last.")] int index)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(go.transform, "MCP: Set Sibling Index");
#endif

            if (index < 0)
                go.transform.SetAsLastSibling();
            else
                go.transform.SetSiblingIndex(index);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["siblingIndex"] = go.transform.GetSiblingIndex(),
                ["siblingCount"] = go.transform.parent != null ? go.transform.parent.childCount : go.transform.root.childCount
            });
        }

        //! Measures the distance between two GameObjects in world space
        [McpTool("Measure distance between two GameObjects. Returns distance in meters and millimeters, plus the direction vector.")]
        public static string TransformMeasureDistance(
            [McpParam("First GameObject name or path")] string objectA,
            [McpParam("Second GameObject name or path")] string objectB)
        {
            var goFrom = ToolHelpers.FindGameObject(objectA);
            if (goFrom == null)
                return ToolHelpers.Error($"GameObject '{objectA}' not found");

            var goTo = ToolHelpers.FindGameObject(objectB);
            if (goTo == null)
                return ToolHelpers.Error($"GameObject '{objectB}' not found");

            var posFrom = goFrom.transform.position;
            var posTo = goTo.transform.position;
            var delta = posTo - posFrom;
            var distance = delta.magnitude;

            // XZ distance (horizontal plane, ignoring Y)
            var deltaXZ = new Vector3(delta.x, 0, delta.z);
            var distanceXZ = deltaXZ.magnitude;

            return ToolHelpers.Ok(new JObject
            {
                ["from"] = new JObject
                {
                    ["name"] = goFrom.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(goFrom),
                    ["position"] = ToolHelpers.Vec3ToJson(posFrom)
                },
                ["to"] = new JObject
                {
                    ["name"] = goTo.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(goTo),
                    ["position"] = ToolHelpers.Vec3ToJson(posTo)
                },
                ["distance_m"] = System.Math.Round(distance, 4),
                ["distance_mm"] = System.Math.Round(distance * 1000, 1),
                ["distanceXZ_m"] = System.Math.Round(distanceXZ, 4),
                ["distanceXZ_mm"] = System.Math.Round(distanceXZ * 1000, 1),
                ["delta"] = ToolHelpers.Vec3ToJson(delta),
                ["heightDifference_m"] = System.Math.Round(delta.y, 4)
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

        /// Returns the encapsulated world-space AABB of all Renderers on a GameObject.
        private static Bounds? GetWorldBounds(GameObject go, bool includeChildren)
        {
            var renderers = includeChildren
                ? go.GetComponentsInChildren<Renderer>()
                : go.GetComponents<Renderer>();
            if (renderers.Length == 0) return null;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        //! Gets the world-space axis-aligned bounding box of a GameObject based on its Renderers
        [McpTool("Get GameObject bounding box from renderers. Returns world-space AABB center, size, min, max in meters and millimeters.")]
        public static string TransformGetBounds(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Include child renderers (default: true)")] bool includeChildren = true)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var bounds = GetWorldBounds(go, includeChildren);
            if (bounds == null)
                return ToolHelpers.Error($"No Renderer components found on '{name}'" +
                    (includeChildren ? " or its children" : ""));

            var b = bounds.Value;
            return ToolHelpers.Ok(new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["center_m"] = ToolHelpers.Vec3ToJson(b.center),
                ["size_m"] = ToolHelpers.Vec3ToJson(b.size),
                ["min_m"] = ToolHelpers.Vec3ToJson(b.min),
                ["max_m"] = ToolHelpers.Vec3ToJson(b.max),
                ["center_mm"] = ToolHelpers.Vec3ToJson(b.center * 1000),
                ["size_mm"] = ToolHelpers.Vec3ToJson(b.size * 1000),
                ["min_mm"] = ToolHelpers.Vec3ToJson(b.min * 1000),
                ["max_mm"] = ToolHelpers.Vec3ToJson(b.max * 1000)
            });
        }

        //! Measures the closest distance between the bounding box surfaces of two GameObjects
        [McpTool("Measure surface-to-surface distance between two GameObjects using bounding boxes. Returns gap or overlap per axis.")]
        public static string TransformMeasureSurfaceDistance(
            [McpParam("First GameObject name or path")] string objectA,
            [McpParam("Second GameObject name or path")] string objectB)
        {
            var goA = ToolHelpers.FindGameObject(objectA);
            if (goA == null)
                return ToolHelpers.Error($"GameObject '{objectA}' not found");

            var goB = ToolHelpers.FindGameObject(objectB);
            if (goB == null)
                return ToolHelpers.Error($"GameObject '{objectB}' not found");

            var boundsA = GetWorldBounds(goA, true);
            if (boundsA == null)
                return ToolHelpers.Error($"No Renderer components found on '{objectA}' or its children");

            var boundsB = GetWorldBounds(goB, true);
            if (boundsB == null)
                return ToolHelpers.Error($"No Renderer components found on '{objectB}' or its children");

            var a = boundsA.Value;
            var b = boundsB.Value;

            // Per-axis gap: positive = gap, negative = overlap
            float gapX = Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x);
            float gapY = Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y);
            float gapZ = Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z);

            // Clamp negative gaps to 0 for surface distance calculation
            float clampedX = Mathf.Max(0, gapX);
            float clampedY = Mathf.Max(0, gapY);
            float clampedZ = Mathf.Max(0, gapZ);

            float surfaceDistance = Mathf.Sqrt(clampedX * clampedX + clampedY * clampedY + clampedZ * clampedZ);
            bool overlapping = gapX < 0 && gapY < 0 && gapZ < 0;
            bool touching = surfaceDistance < 0.001f && !overlapping;

            var result = new JObject
            {
                ["objectA"] = new JObject
                {
                    ["name"] = goA.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(goA),
                    ["bounds_min"] = ToolHelpers.Vec3ToJson(a.min),
                    ["bounds_max"] = ToolHelpers.Vec3ToJson(a.max)
                },
                ["objectB"] = new JObject
                {
                    ["name"] = goB.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(goB),
                    ["bounds_min"] = ToolHelpers.Vec3ToJson(b.min),
                    ["bounds_max"] = ToolHelpers.Vec3ToJson(b.max)
                },
                ["surfaceDistance_m"] = System.Math.Round(surfaceDistance, 4),
                ["surfaceDistance_mm"] = System.Math.Round(surfaceDistance * 1000, 1),
                ["gapX_m"] = System.Math.Round(gapX, 4),
                ["gapY_m"] = System.Math.Round(gapY, 4),
                ["gapZ_m"] = System.Math.Round(gapZ, 4),
                ["overlapping"] = overlapping,
                ["touching"] = touching
            };

            if (overlapping)
            {
                // Penetration depth = minimum overlap magnitude across all axes
                float overlapX = Mathf.Min(a.max.x - b.min.x, b.max.x - a.min.x);
                float overlapY = Mathf.Min(a.max.y - b.min.y, b.max.y - a.min.y);
                float overlapZ = Mathf.Min(a.max.z - b.min.z, b.max.z - a.min.z);
                float penetration = Mathf.Min(overlapX, Mathf.Min(overlapY, overlapZ));
                result["penetration_m"] = System.Math.Round(penetration, 4);
                result["penetration_mm"] = System.Math.Round(penetration * 1000, 1);
            }

            return ToolHelpers.Ok(result);
        }
    }
}
