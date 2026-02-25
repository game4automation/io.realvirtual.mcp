#if REALVIRTUAL_MCP
// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using System.Collections.Generic;
using realvirtual.MCP;
using Newtonsoft.Json.Linq;

namespace realvirtual.MCP.Tests
{
    //! Validates error handling for unknown tools, missing arguments and invalid types
    public class TestMcpToolCallErrorHandling : FeatureTestBase
    {
        protected override string TestName => "McpToolRegistry handles errors for invalid tool calls";

        private string unknownToolResult;
        private string missingArgResult;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();

            // Case 1: Unbekanntes Tool
            unknownToolResult = registry.CallTool("nonexistent_tool_xyz", null);

            // Case 2: Fehlender required Parameter (drive_get braucht 'name')
            missingArgResult = registry.CallTool("drive_get", new Dictionary<string, object>());
        }

        protected override string ValidateResults()
        {
            // Case 1: Unbekanntes Tool sollte Error-JSON liefern
            if (string.IsNullOrEmpty(unknownToolResult))
                return "Unknown tool call returned empty";

            JObject unknownParsed;
            try { unknownParsed = JObject.Parse(unknownToolResult); }
            catch { return $"Unknown tool result not valid JSON: {unknownToolResult}"; }

            if (unknownParsed["error"] == null)
                return "Unknown tool result missing 'error' key";
            if (!unknownParsed["error"].ToString().Contains("not found"))
                return $"Error message should mention 'not found': {unknownParsed["error"]}";

            // Case 2: Fehlender Arg sollte Error-JSON liefern
            if (string.IsNullOrEmpty(missingArgResult))
                return "Missing arg call returned empty";

            JObject missingParsed;
            try { missingParsed = JObject.Parse(missingArgResult); }
            catch { return $"Missing arg result not valid JSON: {missingArgResult}"; }

            if (missingParsed["error"] == null)
                return "Missing arg result missing 'error' key";

            return "";
        }
    }
}

#endif
