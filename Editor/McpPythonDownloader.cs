// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Manages the MCP Python server deployment in StreamingAssets.
    //!
    //! The Python server (including embedded Python runtime) is cloned from the
    //! GitHub repository into Assets/StreamingAssets/realvirtual-MCP/.
    //! This class provides git clone/pull operations as the primary deployment method.
    [InitializeOnLoad]
    static class McpPythonDownloader
    {
        private const string REPO_URL = "https://github.com/game4automation/realvirtual-MCP.git";
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

        //! Checks whether the target directory is a git repository.
        internal static bool IsGitRepo()
        {
            var gitDir = Path.Combine(GetPythonServerPath(), ".git");
            return Directory.Exists(gitDir);
        }

        static McpPythonDownloader()
        {
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
                "This requires git to be installed.\n" +
                "Target: " + GetPythonServerPath(),
                "Download Now", "Later"))
            {
                SessionState.SetBool(SESSION_KEY_DISMISSED, true);
                return;
            }

            DownloadPythonServer();
        }

        //! Runs a git command and returns the exit code. Stdout/stderr are captured.
        private static int RunGit(string arguments, string workingDir, out string output, out string error)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(psi))
                {
                    output = process.StandardOutput.ReadToEnd();
                    error = process.StandardError.ReadToEnd();
                    process.WaitForExit(300_000); // 5 min timeout
                    return process.ExitCode;
                }
            }
            catch (Exception e)
            {
                output = "";
                error = e.Message;
                return -1;
            }
        }

        //! Checks whether git is available on the system.
        internal static bool IsGitAvailable()
        {
            var code = RunGit("--version", Application.dataPath, out _, out _);
            return code == 0;
        }

        //! Clones the Python server repository into StreamingAssets.
        internal static void DownloadPythonServer()
        {
            if (!IsGitAvailable())
            {
                EditorUtility.DisplayDialog("MCP Setup Error",
                    "git is not installed or not in PATH.\n\n" +
                    "Please install git from https://git-scm.com and try again.\n\n" +
                    "Alternative: manually clone the repository:\n" +
                    $"git clone {REPO_URL}\n" +
                    $"into {GetPythonServerPath()}",
                    "OK");
                return;
            }

            var target = GetPythonServerPath();
            var streamingAssets = Path.Combine(Application.dataPath, "StreamingAssets");

            EditorUtility.DisplayProgressBar("MCP Setup", "Cloning Python server repository...", 0.2f);
            try
            {
                // Ensure StreamingAssets directory exists
                if (!Directory.Exists(streamingAssets))
                    Directory.CreateDirectory(streamingAssets);

                // Remove old installation if present but not a git repo
                if (Directory.Exists(target) && !IsGitRepo())
                {
                    Directory.Delete(target, true);
                }

                if (Directory.Exists(target) && IsGitRepo())
                {
                    // Already a git repo - pull latest
                    EditorUtility.DisplayProgressBar("MCP Setup", "Pulling latest changes...", 0.4f);
                    var code = RunGit("pull origin master", target, out var pullOut, out var pullErr);
                    if (code != 0)
                    {
                        McpLog.Error($"git pull failed: {pullErr}");
                        EditorUtility.DisplayDialog("MCP Setup Error",
                            $"git pull failed:\n\n{pullErr}",
                            "OK");
                        return;
                    }
                    McpLog.Info($"Python server updated: {pullOut.Trim()}");
                }
                else
                {
                    // Fresh clone
                    var code = RunGit($"clone {REPO_URL} {TARGET_FOLDER}", streamingAssets,
                        out var cloneOut, out var cloneErr);
                    if (code != 0)
                    {
                        McpLog.Error($"git clone failed: {cloneErr}");
                        EditorUtility.DisplayDialog("MCP Setup Error",
                            $"Failed to clone Python server.\n\n{cloneErr}\n\n" +
                            "Manual alternative:\n" +
                            $"git clone {REPO_URL}\n" +
                            $"into {target}",
                            "OK");
                        return;
                    }
                    McpLog.Info($"Python server cloned to {target}");
                }

                EditorUtility.DisplayProgressBar("MCP Setup", "Refreshing assets...", 0.9f);

                // Write version marker
                var versionFile = Path.Combine(target, ".mcp-version");
                File.WriteAllText(versionFile, McpVersion.Version);

                // Refresh so Unity picks up the new files
                AssetDatabase.Refresh();

                McpLog.Info("MCP Python server ready");
            }
            catch (Exception e)
            {
                McpLog.Error($"Setup failed: {e.Message}");
                EditorUtility.DisplayDialog("MCP Setup Error",
                    $"Failed to set up Python server.\n\n{e.Message}\n\n" +
                    "Manual alternative:\n" +
                    $"git clone {REPO_URL}\n" +
                    $"into {target}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        //! Updates the Python server by pulling latest from the repository.
        internal static void UpdatePythonServer()
        {
            var target = GetPythonServerPath();

            if (!Directory.Exists(target) || !IsGitRepo())
            {
                // Not a git repo - do a fresh clone
                DownloadPythonServer();
                return;
            }

            if (!IsGitAvailable())
            {
                EditorUtility.DisplayDialog("MCP Setup Error",
                    "git is not installed or not in PATH.\n\n" +
                    "Please install git from https://git-scm.com and try again.",
                    "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("MCP Setup", "Pulling latest changes...", 0.3f);
            try
            {
                var code = RunGit("pull origin master", target, out var output, out var error);
                if (code != 0)
                {
                    McpLog.Error($"git pull failed: {error}");
                    EditorUtility.DisplayDialog("MCP Setup Error",
                        $"git pull failed:\n\n{error}",
                        "OK");
                    return;
                }

                // Write version marker
                var versionFile = Path.Combine(target, ".mcp-version");
                File.WriteAllText(versionFile, McpVersion.Version);

                AssetDatabase.Refresh();
                McpLog.Info($"Python server updated: {output.Trim()}");
            }
            catch (Exception e)
            {
                McpLog.Error($"Update failed: {e.Message}");
                EditorUtility.DisplayDialog("MCP Setup Error",
                    $"Failed to update Python server.\n\n{e.Message}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
