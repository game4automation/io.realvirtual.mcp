// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace realvirtual.MCP.Tests
{
    //! Validates that McpToolRegistry's string-based attribute matching correctly discovers
    //! tools across assembly boundaries. This is the critical test for the cross-assembly
    //! reflection pattern: McpToolAttribute may exist in both io.realvirtual.mcp (primary)
    //! and realvirtual.base (stub), but string-based FullName matching finds both.
    public class TestMcpReflectionCrossAssembly : FeatureTestBase
    {
        protected override string TestName => "MCP string-based reflection discovers tools across assemblies";

        private int totalToolCount;
        private Dictionary<string, string> toolAssemblyMap;
        private bool foundToolsFromMcpAssembly;
        private bool foundToolsFromOtherAssembly;
        private int assemblyCount;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();
            totalToolCount = registry.ToolCount;

            // Build a map of tool name -> declaring assembly
            toolAssemblyMap = new Dictionary<string, string>();
            var allEntries = registry.GetAllToolEntries();

            foreach (var kvp in allEntries)
            {
                var assemblyName = kvp.Value.Method.DeclaringType.Assembly.GetName().Name;
                toolAssemblyMap[kvp.Key] = assemblyName;
            }

            // Check which assemblies contributed tools
            var distinctAssemblies = toolAssemblyMap.Values.Distinct().ToList();
            assemblyCount = distinctAssemblies.Count;

            foundToolsFromMcpAssembly = distinctAssemblies.Any(a =>
                a.Contains("mcp") || a == "io.realvirtual.mcp");

            foundToolsFromOtherAssembly = distinctAssemblies.Any(a =>
                !a.Contains("mcp") && a != "io.realvirtual.mcp" && a != "io.realvirtual.mcp.editor");

            LogTest($"Total tools: {totalToolCount} from {assemblyCount} assemblies");
            foreach (var asm in distinctAssemblies)
            {
                var count = toolAssemblyMap.Values.Count(v => v == asm);
                LogTest($"  {asm}: {count} tools");
            }
        }

        protected override string ValidateResults()
        {
            if (totalToolCount == 0)
                return "No tools discovered at all";

            if (!foundToolsFromMcpAssembly)
                return "No tools found from io.realvirtual.mcp assembly. Core MCP tools should be in this assembly.";

            // When Starter is installed (REALVIRTUAL_MCP defined), we expect tools from
            // realvirtual.base as well (DriveTools, SensorTools, SignalTools)
            if (!foundToolsFromOtherAssembly)
                return $"No tools found from assemblies OTHER than io.realvirtual.mcp. " +
                       $"String-based cross-assembly discovery may not be working. " +
                       $"Assemblies found: {string.Join(", ", toolAssemblyMap.Values.Distinct())}";

            if (assemblyCount < 2)
                return $"Tools only found from {assemblyCount} assembly. Expected tools from at least 2 assemblies (mcp + starter/editor).";

            // Verify specific cross-assembly tools exist
            // drive_list is in realvirtual.base (Starter package), not io.realvirtual.mcp
            if (toolAssemblyMap.TryGetValue("drive_list", out var driveListAsm))
            {
                if (driveListAsm == "io.realvirtual.mcp")
                    return "drive_list tool found in io.realvirtual.mcp - it should be in realvirtual.base (Starter package)";
            }

            // sim_play is in io.realvirtual.mcp (or editor), verify it exists
            if (!toolAssemblyMap.ContainsKey("sim_play"))
                return "Core tool 'sim_play' not found";

            return "";
        }
    }
}
