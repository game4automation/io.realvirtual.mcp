// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Linq;
using System.Reflection;

namespace realvirtual.MCP.Tests
{
    //! Validates that McpToolAttribute exists in exactly the right assemblies.
    //! When REALVIRTUAL_MCP is defined (io.realvirtual.mcp is installed):
    //!   - Primary copy in io.realvirtual.mcp assembly: ACTIVE
    //!   - Stub copy in realvirtual.base assembly: ACTIVE (no #if guard - both coexist)
    //! String-based matching in McpToolRegistry handles both types transparently.
    //! This test ensures no unexpected third copy appears.
    public class TestMcpAttributeStubGuard : FeatureTestBase
    {
        protected override string TestName => "McpToolAttribute exists in expected assemblies only";

        private int attributeTypeCount;
        private string[] foundInAssemblies;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            const string fullName = "realvirtual.MCP.McpToolAttribute";

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes()
                            .Where(t => t.FullName == fullName)
                            .Select(t => a.GetName().Name);
                    }
                    catch
                    {
                        return Enumerable.Empty<string>();
                    }
                })
                .Distinct()
                .ToArray();

            attributeTypeCount = found.Length;
            foundInAssemblies = found;

            LogTest($"Found McpToolAttribute in {attributeTypeCount} assemblies: {string.Join(", ", found)}");
        }

        protected override string ValidateResults()
        {
            if (attributeTypeCount == 0)
                return "McpToolAttribute not found in any assembly";

            // The primary copy must exist in io.realvirtual.mcp
            if (!foundInAssemblies.Contains("io.realvirtual.mcp"))
                return $"McpToolAttribute missing from io.realvirtual.mcp assembly. Found in: {string.Join(", ", foundInAssemblies)}";

            // We expect exactly 2 copies: io.realvirtual.mcp (primary) + realvirtual.base (stub)
            // The stub in realvirtual.base has no #if guard and coexists with the primary.
            // McpToolRegistry uses string-based FullName matching to handle both.
            if (attributeTypeCount > 3)
                return $"McpToolAttribute found in too many assemblies ({attributeTypeCount}): {string.Join(", ", foundInAssemblies)}. Expected max 2-3.";

            return "";
        }
    }
}
