#if REALVIRTUAL_MCP
// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using System.Collections.Generic;
using System.Linq;
using realvirtual.MCP;
using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Validates that McpToolRegistry discovers all [McpTool] marked methods
    public class TestMcpToolDiscovery : FeatureTestBase
    {
        protected override string TestName => "McpToolRegistry discovers all expected MCP tools";

        private int toolCount;
        private List<string> toolNames;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();
            toolCount = registry.ToolCount;
            toolNames = registry.ToolNames.ToList();

            LogTest($"Discovered {toolCount} MCP tools: {string.Join(", ", toolNames)}");
        }

        protected override string ValidateResults()
        {
            if (toolCount == 0)
                return "No MCP tools discovered";

            // Mindestens die bekannten Core-Tools muessen vorhanden sein
            var expectedTools = new[]
            {
                "drive_list", "drive_get", "drive_to",
                "scene_hierarchy", "scene_find",
                "sensor_list", "sensor_get",
                "signal_list", "signal_get",
                "sim_status"
            };

            foreach (var expected in expectedTools)
            {
                if (!toolNames.Contains(expected))
                    return $"Expected tool '{expected}' not found. Available: {string.Join(", ", toolNames)}";
            }

            if (toolCount < 20)
                return $"Expected at least 20 tools, found {toolCount}";

            return "";
        }
    }
}

#endif
