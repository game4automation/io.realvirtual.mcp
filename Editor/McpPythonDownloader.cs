// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Manages the MCP Python server deployment in StreamingAssets.
    //!
    //! The Python server (including embedded Python runtime) is shipped in
    //! Assets/StreamingAssets/realvirtual-MCP/ so it is available both in the editor
    //! and in runtime builds. This class provides download/update from GitHub Releases
    //! as a fallback if the server files are missing.
    [InitializeOnLoad]
    static class McpPythonDownloader
    {
        private const string RELEASE_ZIP_URL =
            "https://github.com/realvirtual/unity-mcp-python/releases/latest/download/unity-mcp-python.zip";
        private const string TARGET_FOLDER = "realvirtual-MCP";
        private const string SESSION_KEY_DISMISSED = "McpPythonDownloader_dismissed";

        //! Returns the MCP Python server root directory in StreamingAssets.
        internal static string GetPythonServerPath()
        {
            return Path.Combine(Application.dataPath, "StreamingAssets", TARGET_FOLDER);
        }

        //! Returns the full path to the Python executable.
        internal static string GetPythonExePath()
        {
            return Path.Combine(GetPythonServerPath(), "python", "python.exe");
        }

        //! Checks whether the Python server is deployed and valid (not an LFS pointer).
        internal static bool IsDeployed()
        {
            var pythonExe = GetPythonExePath();
            if (!File.Exists(pythonExe))
                return false;

            // Embedded Python uses a small launcher exe (~105KB) that loads python3XX.dll.
            // python3.dll is a tiny stub (~70KB); the real runtime is python312.dll (~6.9MB).
            // Check that at least one DLL in the directory exceeds 1MB (the real runtime).
            var pythonDir = Path.GetDirectoryName(pythonExe);
            foreach (var dll in Directory.GetFiles(pythonDir, "python3*.dll"))
            {
                if (new FileInfo(dll).Length > 1_000_000)
                    return true;
            }
            return false;
        }

        static McpPythonDownloader()
        {
            // Use EditorApplication.update instead of delayCall (more reliable when Unity is in background)
            EditorApplication.update += CheckOnce;
        }

        private static bool _checked;

        static void CheckOnce()
        {
            if (_checked) return;
            _checked = true;
            EditorApplication.update -= CheckOnce;

            if (IsDeployed()) return;

            // Don't nag on every domain reload - only once per editor session
            if (SessionState.GetBool(SESSION_KEY_DISMISSED, false)) return;

            if (!EditorUtility.DisplayDialog(
                "MCP Python Server",
                "The MCP Python Server needs to be downloaded (~70 MB).\n\n" +
                "This is required for AI agent communication.\n" +
                "Target: " + GetPythonServerPath(),
                "Download Now", "Later"))
            {
                SessionState.SetBool(SESSION_KEY_DISMISSED, true);
                return;
            }

            DownloadPythonServer();
        }

        //! Downloads and extracts the Python server ZIP from GitHub Releases into StreamingAssets.
        internal static void DownloadPythonServer()
        {
            var target = GetPythonServerPath();
            var zipPath = Path.Combine(Path.GetTempPath(), "unity-mcp-python.zip");

            EditorUtility.DisplayProgressBar("MCP Setup", "Downloading Python server...", 0.1f);
            try
            {
                // 1. Download ZIP
                using (var client = new System.Net.WebClient())
                {
                    client.DownloadFile(RELEASE_ZIP_URL, zipPath);
                }

                EditorUtility.DisplayProgressBar("MCP Setup", "Extracting...", 0.6f);

                // 2. Remove old installation if present
                if (Directory.Exists(target))
                    Directory.Delete(target, true);

                // 3. Extract ZIP
                Directory.CreateDirectory(target);
                ZipFile.ExtractToDirectory(zipPath, target);

                // 4. Clean up ZIP
                File.Delete(zipPath);

                // 5. Write version marker
                var versionFile = Path.Combine(target, ".mcp-version");
                File.WriteAllText(versionFile, "1.0.0");

                // 6. Refresh so Unity picks up the new files
                AssetDatabase.Refresh();

                McpLog.Info($"Python server installed to {target}");
            }
            catch (Exception e)
            {
                McpLog.Error($"Download failed: {e.Message}");
                EditorUtility.DisplayDialog("MCP Setup Error",
                    $"Failed to download Python server.\n\n{e.Message}\n\n" +
                    "Manual alternative:\n" +
                    $"Download from {RELEASE_ZIP_URL}\n" +
                    $"Extract to {target}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        //! Re-downloads the Python server (for updates). Deletes existing installation first.
        internal static void UpdatePythonServer()
        {
            var target = GetPythonServerPath();
            if (Directory.Exists(target))
            {
                try
                {
                    Directory.Delete(target, true);
                }
                catch (Exception e)
                {
                    McpLog.Error($"Failed to remove old installation: {e.Message}");
                    return;
                }
            }

            DownloadPythonServer();
        }
    }
}
