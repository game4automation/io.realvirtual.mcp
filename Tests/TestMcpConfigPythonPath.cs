// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.Reflection;

namespace realvirtual.MCP.Tests
{
    //! Validates that McpBridge uses the external Python server path (from %LOCALAPPDATA%)
    //! instead of an in-project path. This ensures Asset Store compatibility.
    public class TestMcpConfigPythonPath : FeatureTestBase
    {
        protected override string TestName => "MCP bridge Python path points to external deployment directory";

        private string bridgePythonPath;
        private string expectedBasePath;
        private bool pathUsesLocalAppData;
        private bool pathNotInsideProject;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            // Get the python server path from McpBridge via reflection (it's a private method)
            var bridgeType = typeof(McpBridge);
            var method = bridgeType.GetMethod("GetPythonServerPath",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method != null)
            {
                bridgePythonPath = method.Invoke(null, null) as string;
            }

            expectedBasePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            pathUsesLocalAppData = !string.IsNullOrEmpty(bridgePythonPath) &&
                                   bridgePythonPath.StartsWith(expectedBasePath);

            // Ensure path is NOT inside the Unity project
            var projectPath = UnityEngine.Application.dataPath;
            pathNotInsideProject = !string.IsNullOrEmpty(bridgePythonPath) &&
                                   !bridgePythonPath.StartsWith(projectPath);

            LogTest($"Bridge Python path: {bridgePythonPath}");
            LogTest($"LocalAppData base: {expectedBasePath}");
        }

        protected override string ValidateResults()
        {
            if (string.IsNullOrEmpty(bridgePythonPath))
                return "McpBridge.GetPythonServerPath() returned null or empty (method not found or returned null)";

            if (!pathUsesLocalAppData)
                return $"Python path does not start with LocalAppData ({expectedBasePath}): {bridgePythonPath}";

            if (!pathNotInsideProject)
                return $"Python path is inside the Unity project (should be external): {bridgePythonPath}";

            if (!bridgePythonPath.Contains("realvirtual-MCP"))
                return $"Python path does not contain 'realvirtual-MCP': {bridgePythonPath}";

            return "";
        }
    }
}
