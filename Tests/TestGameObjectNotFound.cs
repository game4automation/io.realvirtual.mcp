// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using realvirtual.MCP.Tools;

namespace realvirtual.MCP.Tests
{
    //! Verifies tools return proper error JSON when GameObject doesn't exist
    public class TestGameObjectNotFound : FeatureTestBase
    {
        protected override string TestName => "MCP Error Response for Missing GameObject";

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
        }

        protected override string ValidateResults()
        {
            var result = ToolHelpers.FindGameObject("NonExistentObject_XYZ_12345");
            if (result != null)
                return "FindGameObject should return null for non-existent object";

            var errorJson = ToolHelpers.Error("test error message");
            var parsed = JObject.Parse(errorJson);
            if (parsed["error"] == null)
                return "Error JSON missing 'error' field";
            if (parsed["error"].ToString() != "test error message")
                return $"Error message wrong: {parsed["error"]}";

            var okJson = ToolHelpers.Ok("test ok message");
            parsed = JObject.Parse(okJson);
            if (parsed["status"] == null || parsed["status"].ToString() != "ok")
                return "Ok JSON missing or wrong 'status' field";

            return "";
        }
    }
}
