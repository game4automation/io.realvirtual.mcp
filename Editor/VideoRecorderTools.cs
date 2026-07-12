// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for recording a Game-View MP4 via the Unity Recorder package (com.unity.recorder).
    //!
    //! Lets an AI agent capture short video clips of the running simulation for visual verification
    //! (e.g. checking a drive path, a gripper cycle, or a LogicStep sequence over time) - something a
    //! single screenshot cannot show.
    //!
    //! GOTCHAS (do not "fix" these without re-reading them first):
    //! - The Recorder's `<Take>` wildcard in OutputFile throws "Illegal characters in path" on Windows.
    //!   OutputFile is therefore always a fixed, pre-timestamped name - never a wildcard pattern.
    //! - Starting the recorder from an EditorApplication.playModeStateChanged callback does NOT survive
    //!   the domain reload that happens when Play Mode is entered. video_record_start therefore only
    //!   starts a recording while already in Play Mode; it never auto-enters Play Mode itself. Call
    //!   sim_play first.
    //! - This is the MCP-tool counterpart of the prototype in
    //!   Assets/realvirtual-Tools/Editor/RecordVideoTool.cs (kept as-is; its Recorder API usage was
    //!   the reference for this implementation).
    public static class VideoRecorderTools
    {
        private static RecorderController _controller;
        private static string _outputPath;
        private static DateTime _startTimeUtc;
        private static float _maxDurationSec;
        private static int _width;
        private static int _height;
        private static int _fps;

        //! Starts recording the Game View to an MP4 file. Must be called while Play Mode is already
        //! running (call sim_play first) - starting play mode automatically is not supported because
        //! the Recorder cannot survive the domain reload on Play entry.
        [McpTool("Start Game View MP4 recording (call sim_play first)", "video_record_start")]
        public static string VideoRecordStart(
            [McpParam("Video width in pixels (default 1280)")] int width = 1280,
            [McpParam("Video height in pixels (default 720)")] int height = 720,
            [McpParam("Frames per second (default 30)")] int fps = 30,
            [McpParam("Safety-net max recording length in seconds; recording auto-stops after this even if video_record_stop is never called (default 60)")] float maxDurationSec = 60f)
        {
            if (!EditorApplication.isPlaying)
                return ToolHelpers.Error("Play Mode is not running. Recording only works from an already-running Play Mode session " +
                    "(the Recorder cannot be started across the domain reload triggered by entering Play Mode). Call sim_play first, then video_record_start.");

            if (_controller != null && _controller.IsRecording())
                return ToolHelpers.Error("A recording is already in progress. Call video_record_stop first.");

            if (width <= 0 || height <= 0 || fps <= 0 || maxDurationSec <= 0)
                return ToolHelpers.Error("width, height, fps and maxDurationSec must all be positive");

            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".log", "recordings");
                Directory.CreateDirectory(dir);

                var sceneName = SceneManager.GetActiveScene().name;
                if (string.IsNullOrEmpty(sceneName)) sceneName = "scene";
                var timestamp = DateTime.Now.ToString("HHmmss");
                var baseName = $"rec_{sceneName}_{timestamp}";
                var outputFileNoExt = Path.Combine(dir, baseName); // no wildcards - see class doc gotcha

                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "MCP Video Recorder";
                movie.Enabled = true;
                movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                movie.VideoBitRateMode = VideoBitrateMode.High;
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
                movie.OutputFile = outputFileNoExt;
                settings.AddRecorderSettings(movie);
                // TimeInterval doubles as the maxDurationSec safety net: if video_record_stop is never
                // called, the Recorder itself stops recording at this time.
                settings.SetRecordModeToTimeInterval(0f, maxDurationSec);
                settings.FrameRate = fps;
                settings.CapFrameRate = true;

                _controller = new RecorderController(settings);
                _controller.PrepareRecording();
                _controller.StartRecording();

                _outputPath = outputFileNoExt + ".mp4"; // MovieRecorderSettings appends the format extension itself
                _startTimeUtc = DateTime.UtcNow;
                _maxDurationSec = maxDurationSec;
                _width = width;
                _height = height;
                _fps = fps;

                McpLog.Info($"video_record_start: recording -> {_outputPath} ({width}x{height}@{fps}, safety-net {maxDurationSec}s)");

                return ToolHelpers.Ok(new JObject
                {
                    ["recording"] = true,
                    ["path"] = _outputPath,
                    ["width"] = width,
                    ["height"] = height,
                    ["fps"] = fps,
                    ["maxDurationSec"] = maxDurationSec,
                    ["message"] = "Recording started. Call video_record_stop to finish early, or it auto-stops after maxDurationSec."
                });
            }
            catch (Exception ex)
            {
                _controller = null;
                return ToolHelpers.Error($"Failed to start recording: {ex.Message}");
            }
        }

        //! Stops the current recording (if the Recorder's own safety-net time interval hasn't already
        //! finished it) and returns the absolute path and duration of the produced MP4.
        [McpTool("Stop Game View MP4 recording", "video_record_stop")]
        public static string VideoRecordStop()
        {
            if (_controller == null)
                return ToolHelpers.Error("No recording has been started");

            bool wasRecording = _controller.IsRecording();
            if (wasRecording)
                _controller.StopRecording();

            var elapsed = DateTime.UtcNow - _startTimeUtc;
            var path = _outputPath;
            var fileExists = !string.IsNullOrEmpty(path) && File.Exists(path);
            long sizeBytes = fileExists ? new FileInfo(path).Length : 0;

            _controller = null;
            _outputPath = null;

            McpLog.Info($"video_record_stop: {(wasRecording ? "stopped" : "already finished (safety-net)")} -> {path} ({elapsed.TotalSeconds:F1}s, {sizeBytes} bytes)");

            return ToolHelpers.Ok(new JObject
            {
                ["recording"] = false,
                ["path"] = path,
                ["fileExists"] = fileExists,
                ["sizeBytes"] = sizeBytes,
                ["durationSec"] = Math.Round(elapsed.TotalSeconds, 2),
                ["stoppedBySafetyNet"] = !wasRecording
            });
        }

        //! Reports whether a recording is currently in progress, and its elapsed time / target path.
        [McpTool("Get Game View MP4 recording status", "video_record_status")]
        public static string VideoRecordStatus()
        {
            bool recording = _controller != null && _controller.IsRecording();

            var result = new JObject
            {
                ["recording"] = recording
            };

            if (recording)
            {
                var elapsed = DateTime.UtcNow - _startTimeUtc;
                result["path"] = _outputPath;
                result["elapsedSec"] = Math.Round(elapsed.TotalSeconds, 2);
                result["maxDurationSec"] = _maxDurationSec;
                result["width"] = _width;
                result["height"] = _height;
                result["fps"] = _fps;
            }

            return ToolHelpers.Ok(result);
        }
    }
}
