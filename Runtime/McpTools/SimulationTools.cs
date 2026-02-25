// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for controlling the Unity simulation playback state.
    //!
    //! Provides commands to start, pause, stop the simulation and query its status.
    //! These tools allow AI agents to control the simulation timeline.
    public static class SimulationTools
    {
        //! Starts the simulation (Unity Play mode)
        [McpTool("Start simulation")]
        public static string SimPlay()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.EditorApplication.isPlaying = true;
                return Result("playing", "Simulation started");
            }
            return Result("playing", "Simulation already running");
#else
            return Result("playing", "Simulation is running (build mode)");
#endif
        }

        //! Pauses the simulation
        [McpTool("Pause simulation")]
        public static string SimPause()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPaused = true;
            return Result("paused", "Simulation paused");
#else
            Time.timeScale = 0;
            return Result("paused", "Simulation paused (time scale set to 0)");
#endif
        }

        //! Stops the simulation (exits Play mode in Editor)
        [McpTool("Stop simulation")]
        public static string SimStop()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.EditorApplication.isPlaying = false;
                return Result("stopped", "Simulation stopped");
            }
            return Result("stopped", "Simulation not running");
#else
            Time.timeScale = 0;
            return Result("stopped", "Time scale set to 0 (cannot stop in build mode)");
#endif
        }

        //! Resumes the simulation from pause
        [McpTool("Resume simulation")]
        public static string SimResume()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPaused = false;
            return Result("playing", "Simulation resumed");
#else
            Time.timeScale = 1;
            return Result("playing", "Simulation resumed (time scale set to 1)");
#endif
        }

        //! Sets the simulation speed multiplier
        [McpTool("Set simulation speed")]
        public static string SimSetSpeed(
            [McpParam("Speed multiplier (1.0 = normal)")] float speed)
        {
            if (speed < 0)
                return new JObject { ["error"] = "Speed must be positive" }.ToString(Newtonsoft.Json.Formatting.None);

            Time.timeScale = speed;

            return new JObject
            {
                ["status"] = "ok",
                ["speed"] = speed,
                ["message"] = $"Time scale set to {speed}"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets the current simulation status
        [McpTool("Get simulation status")]
        public static string SimStatus()
        {
#if UNITY_EDITOR
            return new JObject
            {
                ["isPlaying"] = UnityEditor.EditorApplication.isPlaying,
                ["isPaused"] = UnityEditor.EditorApplication.isPaused,
                ["timeScale"] = Time.timeScale,
                ["time"] = Time.time,
                ["frameCount"] = Time.frameCount,
                ["mode"] = "editor"
            }.ToString(Newtonsoft.Json.Formatting.None);
#else
            return new JObject
            {
                ["isPlaying"] = true,
                ["isPaused"] = Time.timeScale == 0,
                ["timeScale"] = Time.timeScale,
                ["time"] = Time.time,
                ["frameCount"] = Time.frameCount,
                ["mode"] = "build"
            }.ToString(Newtonsoft.Json.Formatting.None);
#endif
        }

        //! Resets simulation time to zero
        [McpTool("Reset simulation")]
        public static string SimReset()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.isPlaying = true;
            return Result("reset", "Simulation restarted");
#else
            return new JObject { ["error"] = "Reset not available in build mode" }.ToString(Newtonsoft.Json.Formatting.None);
#endif
        }

        //! Helper to build a simple status/message result
        private static string Result(string status, string message)
        {
            return new JObject
            {
                ["status"] = status,
                ["message"] = message
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
