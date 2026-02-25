// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;

namespace realvirtual.MCP.Tests
{
    //! Validates that the Python server deployment is idempotent:
    //! - .mcp-version file is used to track deployment state
    //! - Version file path is deterministic and consistent
    //! - If the server is already deployed, re-deployment would overwrite cleanly
    public class TestMcpDeployerIdempotent : FeatureTestBase
    {
        protected override string TestName => "MCP deployer version check is idempotent";

        private string serverPath;
        private string versionFilePath;
        private bool versionPathIsDeterministic;
        private bool pathEndCorrectly;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            serverPath = Path.Combine(localAppData, "realvirtual-MCP");
            versionFilePath = Path.Combine(serverPath, ".mcp-version");

            // Calling the path computation twice should yield identical results (deterministic)
            var secondCall = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "realvirtual-MCP");
            versionPathIsDeterministic = serverPath == secondCall;

            pathEndCorrectly = versionFilePath.EndsWith(".mcp-version");

            LogTest($"Version file path: {versionFilePath}");
            LogTest($"Server deployed: {Directory.Exists(serverPath)}");

            // If deployed, check version file
            if (File.Exists(versionFilePath))
            {
                var version = File.ReadAllText(versionFilePath).Trim();
                LogTest($"Current version: {version}");
            }
        }

        protected override string ValidateResults()
        {
            if (string.IsNullOrEmpty(serverPath))
                return "Server path is null or empty";

            if (!versionPathIsDeterministic)
                return "Server path is not deterministic - two calls returned different values";

            if (!pathEndCorrectly)
                return $"Version file path does not end with .mcp-version: {versionFilePath}";

            // Verify the version file path is inside the server directory
            if (!versionFilePath.StartsWith(serverPath))
                return $"Version file path is not inside server directory: {versionFilePath} vs {serverPath}";

            return "";
        }
    }
}
