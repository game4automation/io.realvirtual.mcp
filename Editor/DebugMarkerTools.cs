// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for adding transient visual markers (cross/axes/sphere/arrow + optional label) at a
    //! world position or on a GameObject, for visually verifying scene state via screenshots/video.
    //!
    //! Marker SHAPES are rendered TWICE, on purpose, to cover both capture paths an agent might use:
    //! 1. As Handles/Gizmos, drawn each frame via SceneView.duringSceneGui - visible when interactively
    //!    inspecting the Scene View, and in screenshot_editor(panel:"scene") / screenshot_scene(includeGizmos:true)
    //!    (both do a real screen-pixel capture of the Scene View window, which includes Handles output).
    //! 2. As real, temporary GameObjects (HideFlags.HideAndDontSave, LineRenderer/MeshRenderer based) -
    //!    visible in any camera.Render()-based capture, i.e. screenshot_game, screenshot_scene (default,
    //!    RenderTexture path) and video_record_* (Game View Recorder capture). Editor Gizmos/Handles are
    //!    NOT part of a camera's normal render and never appear in those captures, which is why path 1
    //!    alone would be invisible in video/RenderTexture screenshots.
    //!
    //! The optional text LABEL is Scene-View-only (Handles.Label, path 1 above). A real-GameObject
    //! (TextMesh) label for the Game View / video path was prototyped but dropped: a legacy TextMesh's
    //! default font material does not reliably render under URP's camera.Render() path in this project
    //! (no error, it just silently fails to appear), and a hand-rolled URP-Unlit-transparent replacement
    //! material did not fix it either within reasonable effort. Use the marker shape (visible in both
    //! paths) plus the id/type printed in the debug_marker_add response to identify markers when working
    //! from Game View / video captures; use SceneView captures when the text label itself matters.
    //!
    //! Markers are NEVER written to the scene file: HideFlags.HideAndDontSave keeps them out of
    //! serialization and out of the Hierarchy window. Entering/exiting Play Mode triggers a domain
    //! reload that resets this class's static bookkeeping (the _markers dictionary), but the marker
    //! GameObjects themselves - being native HideAndDontSave objects - can survive that reload as
    //! untracked orphans. debug_marker_add sweeps and destroys any such orphans the first time it runs
    //! in a fresh domain, so they never pile up silently; still, always call debug_marker_clear
    //! explicitly when done rather than relying on a Play Mode transition to clean up for you.
    public static class DebugMarkerTools
    {
        private class MarkerEntry
        {
            public string Id;
            public Vector3 WorldPos;
            public string Type;
            public string Label;
            public Color Color;
            public float Size;
            public GameObject VisualRoot;
        }

        private static readonly Dictionary<string, MarkerEntry> _markers = new Dictionary<string, MarkerEntry>();
        private static int _autoIdCounter;
        private static bool _hooked;
        // Note: a distinct Material instance is created per colored shape (via CreateColoredMaterial)
        // instead of one shared material, because LineRenderer.startColor/endColor requires the shader
        // to explicitly support per-vertex color (most URP shaders do not by default) - setting
        // Material.color directly is the robust, shader-agnostic way to get the requested marker color.

        //! Adds a transient visual marker at a world position, or on a GameObject (optionally offset
        //! in that object's local space).
        [McpTool("Add a transient visual marker (cross/axes/sphere/arrow) at a position or on a GameObject", "debug_marker_add")]
        public static string DebugMarkerAdd(
            [McpParam("Optional marker id (auto-generated if empty). Adding with an existing id replaces it.")] string id = "",
            [McpParam("GameObject name/path to anchor the marker to (alternative to x/y/z)")] string path = "",
            [McpParam("World X position (used when path is empty)")] float x = float.NaN,
            [McpParam("World Y position (used when path is empty)")] float y = float.NaN,
            [McpParam("World Z position (used when path is empty)")] float z = float.NaN,
            [McpParam("Local offset X, applied relative to path's transform")] float localOffsetX = 0f,
            [McpParam("Local offset Y, applied relative to path's transform")] float localOffsetY = 0f,
            [McpParam("Local offset Z, applied relative to path's transform")] float localOffsetZ = 0f,
            [McpParam("Marker shape: cross, axes, sphere or arrow (default cross)")] string type = "cross",
            [McpParam("Optional text label - only visible via the SceneView/Handles capture path (screenshot_scene includeGizmos:true or screenshot_editor), not in Game View/video")] string label = "",
            [McpParam("Marker color as hex (e.g. '#FF0000' or 'FF0000'), default red")] string color = "#FF0000",
            [McpParam("Marker size in meters (default 0.05)")] float size = 0.05f)
        {
            type = string.IsNullOrEmpty(type) ? "cross" : type.ToLowerInvariant();
            if (type != "cross" && type != "axes" && type != "sphere" && type != "arrow")
                return ToolHelpers.Error("type must be one of: cross, axes, sphere, arrow");

            if (size <= 0) size = 0.05f;

            Vector3 worldPos;
            if (!string.IsNullOrEmpty(path))
            {
                var go = ToolHelpers.FindGameObject(path);
                if (go == null)
                    return ToolHelpers.Error($"GameObject '{path}' not found");
                worldPos = go.transform.TransformPoint(new Vector3(localOffsetX, localOffsetY, localOffsetZ));
            }
            else if (!float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z))
            {
                worldPos = new Vector3(x, y, z);
            }
            else
            {
                return ToolHelpers.Error("Either path, or all of x/y/z, must be provided");
            }

            if (!ColorUtility.TryParseHtmlString(color.StartsWith("#") ? color : "#" + color, out var col))
                col = Color.red;

            if (string.IsNullOrEmpty(id))
                id = $"marker_{++_autoIdCounter}";

            // Replace an existing marker with the same id
            if (_markers.TryGetValue(id, out var existing) && existing.VisualRoot != null)
                UnityEngine.Object.DestroyImmediate(existing.VisualRoot);

            var entry = new MarkerEntry
            {
                Id = id,
                WorldPos = worldPos,
                Type = type,
                Label = label,
                Color = col,
                Size = size
            };
            try
            {
                entry.VisualRoot = BuildVisual(entry);
            }
            catch (Exception ex)
            {
                return ToolHelpers.Error($"Failed to build marker visual: {ex.Message}");
            }
            _markers[id] = entry;

            EnsureHooked();

            return ToolHelpers.Ok(new JObject
            {
                ["id"] = id,
                ["position"] = ToolHelpers.Vec3ToJson(worldPos),
                ["type"] = type,
                ["size"] = size,
                ["color"] = "#" + ColorUtility.ToHtmlStringRGB(col),
                ["markerCount"] = _markers.Count,
                ["message"] = "Marker shape rendered as SceneView Handles AND as a real HideAndDontSave GameObject " +
                               "(both visible in Game View / video / RenderTexture screenshots). The text label " +
                               "(if any) is SceneView/Handles-only - use screenshot_scene(includeGizmos:true) or " +
                               "screenshot_editor(panel:'scene') to see it. Marker is not part of the scene; " +
                               "does not reliably survive a Play Mode transition."
            });
        }

        //! Clears a single marker by id, or all markers when id is empty.
        [McpTool("Clear one or all transient visual markers", "debug_marker_clear")]
        public static string DebugMarkerClear(
            [McpParam("Marker id to clear (empty = clear all)")] string id = "")
        {
            int removed = 0;

            if (string.IsNullOrEmpty(id))
            {
                foreach (var m in _markers.Values)
                {
                    if (m.VisualRoot != null)
                        UnityEngine.Object.DestroyImmediate(m.VisualRoot);
                    removed++;
                }
                _markers.Clear();
            }
            else if (_markers.TryGetValue(id, out var entry))
            {
                if (entry.VisualRoot != null)
                    UnityEngine.Object.DestroyImmediate(entry.VisualRoot);
                _markers.Remove(id);
                removed = 1;
            }
            else
            {
                return ToolHelpers.Error($"No marker with id '{id}'");
            }

            if (_markers.Count == 0)
                Unhook();

            SceneView.RepaintAll();

            return ToolHelpers.Ok(new JObject
            {
                ["removed"] = removed,
                ["remainingCount"] = _markers.Count
            });
        }

        // ------------------------------------------------------------------
        // Real GameObject visual (Game View / video / RenderTexture path)
        // ------------------------------------------------------------------

        private static Shader _cachedShader;

        //! Creates a new unlit material instance tinted with the given color. A dedicated instance is
        //! used per shape (rather than one shared material) so each axis/line/sphere gets its exact
        //! requested color regardless of whether the shader honors LineRenderer vertex colors.
        private static Material CreateColoredMaterial(Color color)
        {
            if (_cachedShader == null)
                _cachedShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");

            var mat = new Material(_cachedShader) { hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
            return mat;
        }

        private static GameObject BuildVisual(MarkerEntry entry)
        {
            var root = new GameObject($"[MCP Marker] {entry.Id}");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = entry.WorldPos;

            switch (entry.Type)
            {
                case "sphere":
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(root.transform, false);
                    sphere.transform.localScale = Vector3.one * entry.Size * 2f;
                    sphere.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(entry.Color);
                    break;

                case "axes":
                    AddLine(root, Vector3.zero, Vector3.right * entry.Size * 4f, Color.red);
                    AddLine(root, Vector3.zero, Vector3.up * entry.Size * 4f, Color.green);
                    AddLine(root, Vector3.zero, Vector3.forward * entry.Size * 4f, Color.blue);
                    break;

                case "arrow":
                    var tip = Vector3.up * entry.Size * 4f;
                    AddLine(root, Vector3.zero, tip, entry.Color);
                    AddLine(root, tip, tip + (Vector3.down + Vector3.right) * entry.Size, entry.Color);
                    AddLine(root, tip, tip + (Vector3.down + Vector3.left) * entry.Size, entry.Color);
                    break;

                case "cross":
                default:
                    AddLine(root, Vector3.left * entry.Size, Vector3.right * entry.Size, entry.Color);
                    AddLine(root, Vector3.down * entry.Size, Vector3.up * entry.Size, entry.Color);
                    AddLine(root, Vector3.back * entry.Size, Vector3.forward * entry.Size, entry.Color);
                    break;
            }

            // No real-GameObject label here by design - see class doc comment. The Handles.Label in
            // OnSceneGui below is the only label rendering path.

            return root;
        }

        private static void AddLine(GameObject root, Vector3 localA, Vector3 localB, Color color)
        {
            var go = new GameObject("Line");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(root.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, localA);
            lr.SetPosition(1, localB);
            lr.startWidth = lr.endWidth = 0.003f;
            lr.material = CreateColoredMaterial(color);
            lr.startColor = lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        // ------------------------------------------------------------------
        // Handles/Gizmo visual (interactive Scene View path)
        // ------------------------------------------------------------------

        private static void EnsureHooked()
        {
            if (_hooked) return;
            CleanupOrphans();
            SceneView.duringSceneGui += OnSceneGui;
            _hooked = true;
        }

        //! Destroys any leftover "[MCP Marker] ..." root GameObjects that are not tracked in
        //! _markers. Observed in practice: entering/exiting Play Mode triggers a domain reload that
        //! resets the static _markers dictionary, but the HideAndDontSave GameObjects it was tracking
        //! are native engine objects and survive the reload - orphaning them. This sweep runs once per
        //! fresh domain (guarded by _hooked being false), right before the first marker of that domain
        //! is created, so orphans from a previous domain never accumulate silently.
        private static void CleanupOrphans()
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go != null && go.transform.parent == null &&
                    go.hideFlags == HideFlags.HideAndDontSave &&
                    go.name.StartsWith("[MCP Marker] "))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            SceneView.duringSceneGui -= OnSceneGui;
            _hooked = false;
        }

        private static void OnSceneGui(SceneView sv)
        {
            foreach (var m in _markers.Values)
            {
                Handles.color = m.Color;
                var p = m.WorldPos;
                var s = m.Size;

                switch (m.Type)
                {
                    case "sphere":
                        Handles.SphereHandleCap(0, p, Quaternion.identity, s * 2f, EventType.Repaint);
                        break;
                    case "axes":
                        Handles.color = Color.red; Handles.DrawLine(p, p + Vector3.right * s * 4f);
                        Handles.color = Color.green; Handles.DrawLine(p, p + Vector3.up * s * 4f);
                        Handles.color = Color.blue; Handles.DrawLine(p, p + Vector3.forward * s * 4f);
                        break;
                    case "arrow":
                        Handles.ArrowHandleCap(0, p, Quaternion.LookRotation(Vector3.up), s * 4f, EventType.Repaint);
                        break;
                    case "cross":
                    default:
                        Handles.DrawLine(p + Vector3.left * s, p + Vector3.right * s);
                        Handles.DrawLine(p + Vector3.down * s, p + Vector3.up * s);
                        Handles.DrawLine(p + Vector3.back * s, p + Vector3.forward * s);
                        break;
                }

                if (!string.IsNullOrEmpty(m.Label))
                {
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = m.Color } };
                    Handles.Label(p + Vector3.up * s * 5f, m.Label, style);
                }
            }
        }
    }
}
