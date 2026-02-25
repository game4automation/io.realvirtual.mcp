// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for manipulating materials and renderers.
    //!
    //! Provides commands to set and get material colors and toggle renderer visibility.
    //! Uses sharedMaterial in edit mode to prevent material instance leaks.
    public static class MaterialTools
    {
        //! Sets the color on a material
        [McpTool("Set material color")]
        public static string MaterialSetColor(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Red (0-1)")] float r,
            [McpParam("Green (0-1)")] float g,
            [McpParam("Blue (0-1)")] float b,
            [McpParam("Alpha (0-1, default 1)")] float a = 1f,
            [McpParam("Color property name (default '_Color')")] string property = "_Color")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return ToolHelpers.Error($"No Renderer on '{name}'");

            // Use sharedMaterial in edit mode to prevent material instance leaks
            var mat = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
            if (mat == null)
                return ToolHelpers.Error($"No Material assigned on '{name}'");

            if (!mat.HasProperty(property))
                return ToolHelpers.Error($"Material on '{name}' has no property '{property}'");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(mat, "MCP: Set Color");
#endif

            mat.SetColor(property, new Color(r, g, b, a));

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["property"] = property,
                ["color"] = new JObject { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a }
            });
        }

        //! Gets the color from a material
        [McpTool("Get material color")]
        public static string MaterialGetColor(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Color property name (default '_Color')")] string property = "_Color")
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return ToolHelpers.Error($"No Renderer on '{name}'");

            // Always read from sharedMaterial to avoid creating instance
            var mat = renderer.sharedMaterial;
            if (mat == null)
                return ToolHelpers.Error($"No Material assigned on '{name}'");

            if (!mat.HasProperty(property))
                return ToolHelpers.Error($"Material on '{name}' has no property '{property}'");

            var color = mat.GetColor(property);

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["property"] = property,
                ["color"] = new JObject { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a }
            });
        }

        //! Sets renderer enabled/disabled
        [McpTool("Set renderer enabled/disabled")]
        public static string RendererSetEnabled(
            [McpParam("GameObject name or path")] string name,
            [McpParam("Enabled state")] bool enabled)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return ToolHelpers.Error($"No Renderer on '{name}'");

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(renderer, "MCP: Set Renderer Enabled");
#endif

            renderer.enabled = enabled;

            return ToolHelpers.Ok(new JObject
            {
                ["name"] = name,
                ["enabled"] = renderer.enabled
            });
        }
    }
}
