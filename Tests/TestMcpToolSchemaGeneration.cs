// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using realvirtual.MCP;
using Newtonsoft.Json.Linq;

namespace realvirtual.MCP.Tests
{
    //! Validates that GetToolSchemas() generates correct JSON with tools array and schema_version
    public class TestMcpToolSchemaGeneration : FeatureTestBase
    {
        protected override string TestName => "McpToolRegistry generates valid JSON tool schemas";

        private string schemasJson;
        private int toolCount;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();
            toolCount = registry.ToolCount;
            schemasJson = registry.GetToolSchemas();
        }

        protected override string ValidateResults()
        {
            if (string.IsNullOrEmpty(schemasJson))
                return "GetToolSchemas() returned empty";

            JObject parsed;
            try
            {
                parsed = JObject.Parse(schemasJson);
            }
            catch (System.Exception e)
            {
                return $"Schema JSON parse error: {e.Message}";
            }

            if (parsed["tools"] == null)
                return "Schema missing 'tools' key";

            var tools = parsed["tools"] as JArray;
            if (tools == null || tools.Count == 0)
                return "Schema 'tools' is empty or not an array";

            if (tools.Count != toolCount)
                return $"Schema tool count ({tools.Count}) != registry count ({toolCount})";

            if (parsed["schema_version"]?.ToString() != "1.0.0")
                return $"Schema version mismatch: {parsed["schema_version"]}";

            // Pruefe dass jedes Tool name, description und inputSchema hat
            var firstTool = tools[0] as JObject;
            if (firstTool == null)
                return "First tool entry is not a JObject";

            if (firstTool["name"] == null)
                return "Tool missing 'name'";
            if (firstTool["description"] == null)
                return "Tool missing 'description'";
            if (firstTool["inputSchema"] == null)
                return "Tool missing 'inputSchema'";

            // Pruefe dass inputSchema ein korrektes JSON Schema ist
            var schema = firstTool["inputSchema"] as JObject;
            if (schema == null)
                return "inputSchema is not a JObject";
            if (schema["type"]?.ToString() != "object")
                return $"inputSchema type should be 'object', got '{schema["type"]}'";
            if (schema["properties"] == null)
                return "inputSchema missing 'properties'";

            return "";
        }
    }
}
