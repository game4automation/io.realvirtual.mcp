// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using System.Linq;

namespace realvirtual.MCP.Tests
{
    //! Validates that McpToolRegistry discovers the core generic MCP tools
    //! that ship with the io.realvirtual.mcp package (no realvirtual.base dependency).
    //! These tools must always be present regardless of whether Starter is installed.
    public class TestMcpStandaloneToolDiscovery : FeatureTestBase
    {
        protected override string TestName => "MCP standalone tools are discovered without Starter package";

        private int toolCount;
        private List<string> toolNames;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();
            toolCount = registry.ToolCount;
            toolNames = registry.ToolNames.ToList();

            LogTest($"Discovered {toolCount} tools: {string.Join(", ", toolNames)}");
        }

        protected override string ValidateResults()
        {
            if (toolCount == 0)
                return "No MCP tools discovered at all";

            // These generic tools live in io.realvirtual.mcp and must ALWAYS be present
            var genericTools = new[]
            {
                "scene_hierarchy", "scene_find", "scene_get_transform",
                "scene_get_components", "scene_get_info",
                "sim_play", "sim_stop", "sim_pause", "sim_status",
                "game_object_create", "game_object_destroy",
                "component_get", "component_set", "component_get_all",
                "transform_set_position", "transform_set_rotation"
            };

            foreach (var expected in genericTools)
            {
                if (!toolNames.Contains(expected))
                    return $"Generic tool '{expected}' not found. These must exist in standalone MCP package. Available: {string.Join(", ", toolNames)}";
            }

            if (toolCount < 15)
                return $"Expected at least 15 generic tools, found {toolCount}";

            return "";
        }
    }
}
