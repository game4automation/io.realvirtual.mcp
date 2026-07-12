// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for creating player builds from the Unity Editor.
    //!
    //! Provides a command to build a single scene for a chosen platform (Windows by default)
    //! using Unity's BuildPipeline. Intended for verifying build capabilities and quick
    //! smoke-test builds. Editor-only (excluded from builds by assembly definition).
    public static class EditorBuildTools
    {
        //! Builds a player from a single scene for the requested platform.
        //!
        //! Wraps BuildPipeline.BuildPlayer. If scenePath is empty the currently active
        //! (saved) scene is used. If the requested build target differs from the active
        //! target, Unity switches the active target first (requires the platform module
        //! to be installed). Cannot run while the editor is in play mode.
        [McpTool("Build a player from a single scene for a platform (e.g. Windows)", "editor_build")]
        public static string EditorBuild(
            [McpParam("Scene asset path to build (e.g. 'Assets/Scenes/MyScene.unity'). If empty, the active saved scene is used.")] string scenePath = "",
            [McpParam("Build target: StandaloneWindows64 (default), StandaloneWindows, StandaloneOSX, StandaloneLinux64, Android, WebGL. Aliases 'Windows'/'Win64' map to StandaloneWindows64.")] string buildTarget = "StandaloneWindows64",
            [McpParam("Optional output path. If empty, builds to '<project>/Build/MCPTest/<target>/<scene>(.exe)'.")] string outputPath = "",
            [McpParam("If true, produces a development build (debugging enabled).")] bool developmentBuild = false)
        {
            if (EditorApplication.isPlaying)
                return ToolHelpers.Error("Cannot build while in play mode. Stop the simulation first (sim_stop).");

            // Resolve build target
            if (!TryParseBuildTarget(buildTarget, out var target))
                return ToolHelpers.Error($"Unknown build target '{buildTarget}'. Use e.g. StandaloneWindows64, StandaloneOSX, StandaloneLinux64, Android, WebGL.");

            var group = BuildPipeline.GetBuildTargetGroup(target);

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                return ToolHelpers.Error($"Build target '{target}' is not supported (platform module not installed in this Unity Editor).");

            // Resolve scene
            if (string.IsNullOrEmpty(scenePath))
            {
                var active = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path))
                    return ToolHelpers.Error("Active scene is not saved. Provide scenePath or save the scene first (editor_save_scene).");
                scenePath = active.path;
            }

            if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                scenePath += ".unity";

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return ToolHelpers.Error($"Scene not found at path: {scenePath}");

            // Resolve output location
            if (string.IsNullOrEmpty(outputPath))
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var buildName = Path.GetFileNameWithoutExtension(scenePath);
                var ext = GetExecutableExtension(target);
                outputPath = Path.Combine(projectRoot, "Build", "MCPTest", target.ToString(),
                    string.IsNullOrEmpty(ext) ? buildName : buildName + ext);
            }

            var outputDir = GetExecutableExtension(target).Length == 0
                ? outputPath                              // folder-based targets (WebGL, iOS)
                : Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Switch active build target if needed
            bool switched = false;
            if (target != EditorUserBuildSettings.activeBuildTarget)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                    return ToolHelpers.Error($"Failed to switch active build target to '{target}'.");
                switched = true;
            }

            // Build
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = developmentBuild ? BuildOptions.Development : BuildOptions.None
            };

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception ex)
            {
                return ToolHelpers.Error($"Build threw an exception: {ex.Message}");
            }

            var summary = report.summary;
            var data = new JObject
            {
                ["result"] = summary.result.ToString(),
                ["scene"] = scenePath,
                ["buildTarget"] = target.ToString(),
                ["switchedActiveTarget"] = switched,
                ["outputPath"] = summary.outputPath,
                ["totalErrors"] = summary.totalErrors,
                ["totalWarnings"] = summary.totalWarnings,
                ["totalSizeBytes"] = (long)summary.totalSize,
                ["buildTimeSeconds"] = Math.Round(summary.totalTime.TotalSeconds, 2),
                ["developmentBuild"] = developmentBuild
            };

            if (summary.result == BuildResult.Succeeded)
                return ToolHelpers.Ok(data);

            data["status"] = "error";
            data["error"] = $"Build {summary.result} with {summary.totalErrors} error(s).";
            return data.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Parses a build target string (case-insensitive) including a few friendly aliases.
        private static bool TryParseBuildTarget(string value, out BuildTarget target)
        {
            target = BuildTarget.StandaloneWindows64;
            if (string.IsNullOrEmpty(value))
                return true;

            switch (value.Trim().ToLowerInvariant())
            {
                case "windows":
                case "win":
                case "win64":
                case "standalonewindows64":
                    target = BuildTarget.StandaloneWindows64;
                    return true;
                case "win32":
                case "standalonewindows":
                    target = BuildTarget.StandaloneWindows;
                    return true;
                case "osx":
                case "mac":
                case "macos":
                case "standaloneosx":
                    target = BuildTarget.StandaloneOSX;
                    return true;
                case "linux":
                case "linux64":
                case "standalonelinux64":
                    target = BuildTarget.StandaloneLinux64;
                    return true;
            }

            return Enum.TryParse(value, true, out target) && Enum.IsDefined(typeof(BuildTarget), target);
        }

        //! Returns the executable file extension for a target, or empty string for folder-based targets.
        private static string GetExecutableExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.StandaloneLinux64:
                    return ".x86_64";
                case BuildTarget.Android:
                    return ".apk";
                default:
                    return ""; // WebGL, iOS and others build into a folder
            }
        }
    }
}
