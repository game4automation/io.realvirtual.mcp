// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Linq;
using realvirtual.MCP;
using Newtonsoft.Json.Linq;

namespace realvirtual.MCP.Tests
{
    //! Validates that tool input schemas include correct parameter types and required fields
    public class TestMcpToolInputSchemaWithParams : FeatureTestBase
    {
        protected override string TestName => "MCP tool input schemas include correct parameter types and required fields";

        private McpToolRegistry registry;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            registry = new McpToolRegistry();
            registry.DiscoverTools();
        }

        protected override string ValidateResults()
        {
            // drive_to hat 2 required Parameter: name (string), position (number)
            var driveTo = registry.GetTool("drive_to");
            if (driveTo == null)
                return "drive_to tool not found in registry";

            if (string.IsNullOrEmpty(driveTo.InputSchema))
                return "drive_to InputSchema is empty";

            JObject schema;
            try { schema = JObject.Parse(driveTo.InputSchema); }
            catch { return $"drive_to schema not valid JSON: {driveTo.InputSchema}"; }

            var props = schema["properties"] as JObject;
            if (props == null)
                return "drive_to schema missing 'properties'";

            // Pruefe 'name' Parameter (string)
            if (props["name"] == null)
                return "drive_to schema missing 'name' property";
            if (props["name"]["type"]?.ToString() != "string")
                return $"drive_to 'name' type should be string, got {props["name"]["type"]}";

            // Pruefe 'position' Parameter (number)
            if (props["position"] == null)
                return "drive_to schema missing 'position' property";
            if (props["position"]["type"]?.ToString() != "number")
                return $"drive_to 'position' type should be number, got {props["position"]["type"]}";

            // Pruefe required Array
            var required = schema["required"] as JArray;
            if (required == null)
                return "drive_to schema missing 'required' array";
            if (!required.Any(r => r.ToString() == "name"))
                return "'name' not in required list";
            if (!required.Any(r => r.ToString() == "position"))
                return "'position' not in required list";

            // drive_list hat keine Parameter
            var driveList = registry.GetTool("drive_list");
            if (driveList == null)
                return "drive_list tool not found";

            JObject listSchema;
            try { listSchema = JObject.Parse(driveList.InputSchema); }
            catch (System.Exception e) { return $"drive_list schema JSON parse error: {e.Message}"; }

            var listProps = listSchema["properties"] as JObject;
            if (listProps == null)
                return "drive_list schema missing 'properties'";
            if (listProps.Count != 0)
                return $"drive_list should have 0 properties, got {listProps.Count}";

            return "";
        }
    }
}
