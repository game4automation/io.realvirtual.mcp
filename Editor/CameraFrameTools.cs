// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tool that frames a GameObject (or a raw world position) in the Scene View and/or the
    //! Main Camera, computing camera distance from the target's renderer bounds instead of requiring
    //! the agent to guess a position and rotation by hand.
    public static class CameraFrameTools
    {
        //! Computes a camera position that frames the target and applies it to the SceneView camera
        //! and/or the Main Camera.
        [McpTool("Frame a GameObject or position in the Scene/Game camera", "camera_frame")]
        public static string CameraFrame(
            [McpParam("GameObject name/path, or a 'x,y,z' world position (meters)")] string target,
            [McpParam("View direction X (default: isometric)")] float directionX = float.NaN,
            [McpParam("View direction Y (default: isometric)")] float directionY = float.NaN,
            [McpParam("View direction Z (default: isometric)")] float directionZ = float.NaN,
            [McpParam("Extra margin factor around the target bounds (default 1.5)")] float padding = 1.5f,
            [McpParam("Which camera(s) to move: scene, game, or both (default: both)")] string view = "both")
        {
            if (string.IsNullOrEmpty(target))
                return ToolHelpers.Error("target is required (GameObject name/path or 'x,y,z' position)");

            if (padding <= 0)
                padding = 1.5f;

            view = string.IsNullOrEmpty(view) ? "both" : view.ToLowerInvariant();
            if (view != "scene" && view != "game" && view != "both")
                return ToolHelpers.Error("view must be 'scene', 'game' or 'both'");

            // Resolve target: GameObject bounds first, then fall back to a raw "x,y,z" position.
            Vector3 center;
            float radius;
            string resolvedName = null;
            string resolvedPath = null;

            var go = ToolHelpers.FindGameObject(target);
            if (go != null)
            {
                var bounds = GetWorldBounds(go);
                if (bounds == null)
                {
                    // No renderers found (e.g. an empty parent) - frame just its transform position
                    // with a small default radius so the camera still lands at a sensible distance.
                    center = go.transform.position;
                    radius = 0.5f;
                }
                else
                {
                    center = bounds.Value.center;
                    radius = Mathf.Max(bounds.Value.extents.magnitude, 0.05f);
                }
                resolvedName = go.name;
                resolvedPath = ToolHelpers.GetGameObjectPath(go);
            }
            else if (TryParsePosition(target, out var pos))
            {
                center = pos;
                radius = 0.5f;
            }
            else
            {
                return ToolHelpers.Error($"target '{target}' is neither a known GameObject nor a valid 'x,y,z' position");
            }

            // Direction: explicit override (all three components) or default isometric.
            Vector3 direction;
            if (!float.IsNaN(directionX) && !float.IsNaN(directionY) && !float.IsNaN(directionZ))
            {
                direction = new Vector3(directionX, directionY, directionZ);
                if (direction.sqrMagnitude < 1e-6f)
                    direction = new Vector3(1f, 1f, 1f);
            }
            else
            {
                direction = new Vector3(1f, 1f, 1f);
            }
            direction.Normalize();

            const float assumedFovDeg = 50f;
            float distance = (radius * padding) / Mathf.Sin(assumedFovDeg * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, 0.1f);

            var camPos = center + direction * distance;
            var rotation = Quaternion.LookRotation((center - camPos).normalized, Vector3.up);

            bool sceneUpdated = false;
            bool gameUpdated = false;
            string mainCameraName = null;

            if (view == "scene" || view == "both")
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null)
                {
                    sv.pivot = center;
                    sv.rotation = rotation;
                    sv.size = radius * padding;
                    sv.orthographic = false;
                    sv.Repaint();
                    sceneUpdated = true;
                }
            }

            if (view == "game" || view == "both")
            {
                var cam = Camera.main;
                if (cam != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        Undo.RecordObject(cam.transform, "MCP: Frame Camera");
#endif
                    cam.transform.position = camPos;
                    cam.transform.rotation = rotation;
                    mainCameraName = cam.name;
                    gameUpdated = true;
                }
            }

            if (!sceneUpdated && !gameUpdated)
                return ToolHelpers.Error("No target camera available: SceneView is not open and/or no Main Camera exists in the scene.");

            var result = new JObject
            {
                ["target"] = resolvedName ?? target,
                ["center"] = ToolHelpers.Vec3ToJson(center),
                ["radius_m"] = radius,
                ["direction"] = ToolHelpers.Vec3ToJson(direction),
                ["distance_m"] = distance,
                ["cameraPosition"] = ToolHelpers.Vec3ToJson(camPos),
                ["padding"] = padding,
                ["sceneViewUpdated"] = sceneUpdated,
                ["mainCameraUpdated"] = gameUpdated
            };
            if (resolvedPath != null) result["path"] = resolvedPath;
            if (mainCameraName != null) result["mainCamera"] = mainCameraName;

            return ToolHelpers.Ok(result);
        }

        //! Parses a "x,y,z" (comma or whitespace separated) world position string.
        private static bool TryParsePosition(string text, out Vector3 pos)
        {
            pos = Vector3.zero;
            var parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
            {
                pos = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        //! Returns the encapsulated world-space AABB of all Renderers under a GameObject (including children).
        private static Bounds? GetWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
