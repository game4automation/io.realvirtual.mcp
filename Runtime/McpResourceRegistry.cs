// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Discovers and caches MCP usage guide files (*.mcp.md) from the project.
    //!
    //! Any file ending with .mcp.md in the Packages/ or Assets/ directories is
    //! treated as an MCP usage guide. These files contain conventions and rules
    //! that are delivered to AI clients via the MCP instructions mechanism.
    //!
    //! Packages can ship their own guides (e.g., IK.mcp.md next to IK code),
    //! and projects can add custom guides in Assets/.
    public class McpResourceRegistry
    {
        private string _combinedGuide;
        private List<string> _guidePaths = new List<string>();

        private const string GUIDE_EXTENSION = "*.mcp.md";

        //! Gets the combined content of all discovered guides
        public string GetCombinedGuide() => _combinedGuide ?? "";

        //! Gets the number of discovered guide files
        public int GuideCount => _guidePaths.Count;

        //! Gets the paths of all discovered guide files
        public IReadOnlyList<string> GuidePaths => _guidePaths;

        //! Discovers all *.mcp.md files in Packages/ and Assets/ directories.
        //! Reads and concatenates their content with source headers.
        public void DiscoverGuides()
        {
            _guidePaths.Clear();
            _combinedGuide = null;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var searchDirs = new[] { "Packages", "Assets" };
            var allFiles = new List<string>();

            foreach (var dir in searchDirs)
            {
                var fullPath = Path.Combine(projectRoot, dir);
                if (!Directory.Exists(fullPath))
                    continue;

                try
                {
                    var files = Directory.GetFiles(fullPath, GUIDE_EXTENSION, SearchOption.AllDirectories);
                    allFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"ResourceRegistry: Error scanning {dir}/: {ex.Message}");
                }
            }

            if (allFiles.Count == 0)
            {
                McpLog.Debug("ResourceRegistry: No .mcp.md files found");
                return;
            }

            // Sort for deterministic order: Packages first, then Assets, alphabetical within each
            allFiles.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var filePath in allFiles)
            {
                try
                {
                    var content = File.ReadAllText(filePath);
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    // Create relative path for the header
                    var relativePath = filePath.Substring(projectRoot.Length + 1)
                        .Replace('\\', '/');

                    _guidePaths.Add(relativePath);

                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.AppendLine($"--- {relativePath} ---");
                    sb.AppendLine(content.TrimEnd());
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"ResourceRegistry: Error reading {filePath}: {ex.Message}");
                }
            }

            _combinedGuide = sb.ToString();
            McpLog.Debug($"ResourceRegistry: Discovered {_guidePaths.Count} MCP guide(s)");
        }
    }
}
