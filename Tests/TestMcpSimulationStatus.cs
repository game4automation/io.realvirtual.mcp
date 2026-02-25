// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using realvirtual.MCP;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Validates sim_status returns valid simulation state and sim_set_speed changes time scale
    public class TestMcpSimulationStatus : FeatureTestBase
    {
        protected override string TestName => "MCP sim_status returns valid status and sim_set_speed changes time scale";

        private McpToolRegistry registry;
        private float originalTimeScale;

        protected override void SetupTest()
        {
            MinTestTime = 1f;

            originalTimeScale = Time.timeScale;

            registry = new McpToolRegistry();
            registry.DiscoverTools();
        }

        // Garantierte Wiederherstellung von Time.timeScale auch bei Test-Fehlschlag
        protected override void CleanupTest()
        {
            Time.timeScale = originalTimeScale;
            base.CleanupTest();
        }

        protected override string ValidateResults()
        {
            // Test sim_status
            var statusResult = registry.CallTool("sim_status", null);
            if (string.IsNullOrEmpty(statusResult))
                return "sim_status returned empty";

            JObject statusParsed;
            try { statusParsed = JObject.Parse(statusResult); }
            catch (System.Exception e) { return $"sim_status JSON parse error: {e.Message}"; }

            if (statusParsed["isPlaying"] == null)
                return "sim_status missing 'isPlaying'";
            if (statusParsed["timeScale"] == null)
                return "sim_status missing 'timeScale'";
            if (statusParsed["time"] == null)
                return "sim_status missing 'time'";

            // Test sim_set_speed
            var setResult = registry.CallTool("sim_set_speed",
                new Dictionary<string, object> { ["speed"] = 2.0f });

            JObject setParsed;
            try { setParsed = JObject.Parse(setResult); }
            catch (System.Exception e) { return $"sim_set_speed JSON parse error: {e.Message}"; }

            if (setParsed["error"] != null)
                return $"sim_set_speed error: {setParsed["error"]}";

            if (Time.timeScale < 1.9f || Time.timeScale > 2.1f)
                return $"Time.timeScale not updated. Expected ~2.0, got {Time.timeScale}";

            return "";
        }
    }
}
