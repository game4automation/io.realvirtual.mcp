// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using realvirtual.MCP;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Validates scene_find locates objects and scene_get_components lists their components
    public class TestMcpSceneFindAndComponents : FeatureTestBase
    {
        protected override string TestName => "MCP scene_find locates objects and scene_get_components lists components";

        private McpToolRegistry registry;

        protected override void SetupTest()
        {
            MinTestTime = 1f;

            // Erstelle ein benanntes Test-Object mit Komponenten
            var go = CreateGameObject("McpSceneTestObject");
            go.AddComponent<BoxCollider>();
            go.AddComponent<Rigidbody>();

            registry = new McpToolRegistry();
            registry.DiscoverTools();
        }

        protected override string ValidateResults()
        {
            // Test scene_find
            var findResult = registry.CallTool("scene_find",
                new Dictionary<string, object> { ["searchTerm"] = "McpSceneTest" });

            if (string.IsNullOrEmpty(findResult))
                return "scene_find returned empty";

            JObject findParsed;
            try { findParsed = JObject.Parse(findResult); }
            catch (System.Exception e) { return $"scene_find JSON parse error: {e.Message}"; }

            if (findParsed["objects"] == null)
                return "scene_find missing 'objects' key";

            var objects = findParsed["objects"] as JArray;
            if (objects == null || objects.Count == 0)
                return "scene_find found no objects";

            bool found = false;
            foreach (var obj in objects)
            {
                if (obj["name"]?.ToString() == "McpSceneTestObject")
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return "scene_find did not find 'McpSceneTestObject'";

            // Test scene_get_components
            var compResult = registry.CallTool("scene_get_components",
                new Dictionary<string, object> { ["name"] = "McpSceneTestObject" });

            if (string.IsNullOrEmpty(compResult))
                return "scene_get_components returned empty";

            JObject compParsed;
            try { compParsed = JObject.Parse(compResult); }
            catch (System.Exception e) { return $"scene_get_components JSON parse error: {e.Message}"; }

            if (compParsed["error"] != null)
                return $"scene_get_components error: {compParsed["error"]}";

            var components = compParsed["components"] as JArray;
            if (components == null)
                return "scene_get_components missing 'components'";

            bool hasBoxCollider = false;
            bool hasRigidbody = false;
            foreach (var comp in components)
            {
                var name = comp.ToString();
                if (name == "BoxCollider") hasBoxCollider = true;
                if (name == "Rigidbody") hasRigidbody = true;
            }

            if (!hasBoxCollider)
                return "scene_get_components missing BoxCollider";
            if (!hasRigidbody)
                return "scene_get_components missing Rigidbody";

            return "";
        }
    }
}
