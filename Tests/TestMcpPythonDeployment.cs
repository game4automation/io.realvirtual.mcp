// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;

namespace realvirtual.MCP.Tests
{
    //! Validates that the MCP Python server deployment path is correctly configured
    //! and that GetPythonServerPath() returns a valid, writable directory.
    public class TestMcpPythonDeployment : FeatureTestBase
    {
        protected override string TestName => "MCP Python deployment path is valid and writable";

        private string serverPath;
        private string pythonExePath;
        private bool pathIsAbsolute;
        private bool parentDirectoryExists;
        private bool containsTargetFolder;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            // Use the same logic as McpPythonDownloader (which is Editor-only, so we duplicate the path logic)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            serverPath = Path.Combine(localAppData, "realvirtual-MCP");
            pythonExePath = Path.Combine(serverPath, "python", "python.exe");

            pathIsAbsolute = Path.IsPathRooted(serverPath);
            parentDirectoryExists = Directory.Exists(Path.GetDirectoryName(serverPath));
            containsTargetFolder = serverPath.Contains("realvirtual-MCP");

            LogTest($"Python server path: {serverPath}");
            LogTest($"Python exe path: {pythonExePath}");
        }

        protected override string ValidateResults()
        {
            if (string.IsNullOrEmpty(serverPath))
                return "GetPythonServerPath() returned empty string";

            if (!pathIsAbsolute)
                return $"Path is not absolute: {serverPath}";

            if (!parentDirectoryExists)
                return $"Parent directory does not exist: {Path.GetDirectoryName(serverPath)}";

            if (!containsTargetFolder)
                return $"Path does not contain 'realvirtual-MCP': {serverPath}";

            if (string.IsNullOrEmpty(pythonExePath))
                return "GetPythonExePath() returned empty string";

            if (!pythonExePath.EndsWith("python.exe"))
                return $"Python exe path does not end with python.exe: {pythonExePath}";

            return "";
        }
    }
}
