// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for Unity Editor operations.
    //!
    //! Provides commands to read console logs, trigger script recompilation,
    //! and query editor state. These tools enable AI agents to interact
    //! with Unity Editor functionality.
    public static class EditorTools
    {
        private static Type _logEntriesType;
        private static Type _logEntryType;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _startMethod;
        private static MethodInfo _getEntryMethod;
        private static MethodInfo _endMethod;
        private static MethodInfo _clearMethod;
        private static FieldInfo _conditionField;
        private static FieldInfo _modeField;
        private static FieldInfo _callstackStartField;
        private static PropertyInfo _consoleFlagsProp;
        private static MethodInfo _getFilterMethod;
        private static MethodInfo _setFilterMethod;
        private static bool _reflectionInitialized;

        //! Reads recent Unity console log entries
        [McpTool("Read console log")]
        public static string EditorReadLog(
            [McpParam("Max entries to return")] int count = 50,
            [McpParam("Filter: all, error, warning, log")] string filter = "all")
        {
            InitReflection();

            if (_startMethod == null || _getEntryMethod == null || _endMethod == null)
                return new JObject { ["error"] = "Console log API not available" }.ToString(Newtonsoft.Json.Formatting.None);

            var entries = new JArray();

            // Temporarily enable all log level flags so GetCount/StartGettingEntries returns
            // the full unfiltered count. Unity's console API respects these flags.
            int savedFlags = -1;
            string savedFilter = null;
            try
            {
                if (_consoleFlagsProp != null)
                {
                    savedFlags = (int)_consoleFlagsProp.GetValue(null);
                    const int allLogFlags = (1 << 7) | (1 << 8) | (1 << 9);
                    _consoleFlagsProp.SetValue(null, savedFlags | allLogFlags);
                }

                if (_getFilterMethod != null)
                {
                    savedFilter = (string)_getFilterMethod.Invoke(null, null) ?? "";
                    if (!string.IsNullOrEmpty(savedFilter))
                        _setFilterMethod?.Invoke(null, new object[] { "" });
                }
            }
            catch { }

            int total = (int)_startMethod.Invoke(null, null);
            int start = Mathf.Max(0, total - count);

            try
            {
                for (int i = total - 1; i >= start; i--)
                {
                    try
                    {
                        // Create Unity's actual LogEntry class instance
                        var entry = Activator.CreateInstance(_logEntryType);
                        var parameters = new object[] { i, entry };
                        bool success = (bool)_getEntryMethod.Invoke(null, parameters);

                        if (!success)
                            continue;

                        // Read back the out parameter (may be updated by the method)
                        entry = parameters[1];

                        string condition = _conditionField?.GetValue(entry) as string ?? "";
                        int mode = _modeField != null ? (int)_modeField.GetValue(entry) : 0;

                        var type = GetLogType(mode);
                        if (!MatchesFilter(type, filter))
                            continue;

                        // Split message from stack trace using Unity 6 callstackTextStartUTF16 field,
                        // falling back to string search for older Unity versions
                        string message = condition;
                        string stackTrace = null;
                        int stackStart = -1;

                        if (_callstackStartField != null)
                        {
                            int csStart = (int)_callstackStartField.GetValue(entry);
                            if (csStart > 0 && csStart < condition.Length)
                                stackStart = csStart;
                        }

                        if (stackStart < 0)
                        {
                            stackStart = condition.IndexOf("\nUnityEngine.", StringComparison.Ordinal);
                            if (stackStart < 0)
                                stackStart = condition.IndexOf("\nSystem.", StringComparison.Ordinal);
                            if (stackStart >= 0)
                                stackStart++; // skip the newline
                        }

                        if (stackStart >= 0)
                        {
                            message = condition.Substring(0, stackStart).TrimEnd('\n');
                            stackTrace = condition.Substring(stackStart);
                        }

                        var logEntry = new JObject
                        {
                            ["message"] = message,
                            ["type"] = type
                        };

                        if (stackTrace != null)
                            logEntry["stackTrace"] = stackTrace;

                        entries.Add(logEntry);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            finally
            {
                _endMethod.Invoke(null, null);

                // Restore original console flags and filter text
                try
                {
                    if (savedFlags >= 0 && _consoleFlagsProp != null)
                        _consoleFlagsProp.SetValue(null, savedFlags);
                    if (savedFilter != null && !string.IsNullOrEmpty(savedFilter))
                        _setFilterMethod?.Invoke(null, new object[] { savedFilter });
                }
                catch { }
            }

            return new JObject
            {
                ["entries"] = entries,
                ["count"] = entries.Count,
                ["total"] = total
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Writes a message to the Unity console log
        [McpTool("Write to console log")]
        public static string EditorWriteLog(
            [McpParam("Message to write")] string message = "MCP test message",
            [McpParam("Log type: log, warning, error")] string type = "log")
        {
            switch (type.ToLower())
            {
                case "warning":
                    McpLog.Warn(message);
                    break;
                case "error":
                    McpLog.Error(message);
                    break;
                default:
                    McpLog.Info(message);
                    break;
            }

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Logged: {message}",
                ["type"] = type
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Clears the Unity console
        [McpTool("Clear console log")]
        public static string EditorClearLog()
        {
            InitReflection();

            if (_clearMethod != null)
                _clearMethod.Invoke(null, null);

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = "Console cleared"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Triggers script recompilation
        [McpTool("Recompile scripts")]
        public static string EditorRecompile()
        {
            if (EditorApplication.isPlaying)
            {
                return new JObject
                {
                    ["error"] = "Cannot recompile while simulation is running. Stop the simulation first (sim_stop).",
                    ["isPlaying"] = true
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            CompilationPipeline.RequestScriptCompilation();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = "Script recompilation requested"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Refreshes the AssetDatabase (reimports changed assets)
        [McpTool("Refresh assets")]
        public static string EditorRefreshAssets()
        {
            AssetDatabase.Refresh();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = "AssetDatabase refresh triggered"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets the current Unity Editor status
        [McpTool("Get editor status")]
        public static string EditorGetStatus()
        {
            return new JObject
            {
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isPlaying"] = EditorApplication.isPlaying,
                ["isPaused"] = EditorApplication.isPaused,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["platform"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["unityVersion"] = Application.unityVersion
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Initializes reflection cache for LogEntries and LogEntry access.
        //! Uses Unity's actual internal LogEntry class instead of a custom struct.
        private static void InitReflection()
        {
            if (_reflectionInitialized) return;

            try
            {
                _logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                _logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");

                if (_logEntriesType != null)
                {
                    var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    _getCountMethod = _logEntriesType.GetMethod("GetCount", flags);
                    _startMethod = _logEntriesType.GetMethod("StartGettingEntries", flags);
                    _endMethod = _logEntriesType.GetMethod("EndGettingEntries", flags);
                    _clearMethod = _logEntriesType.GetMethod("Clear", flags);

                    // GetEntryInternal takes (int row, LogEntry outputEntry)
                    if (_logEntryType != null)
                        _getEntryMethod = _logEntriesType.GetMethod("GetEntryInternal", flags,
                            null, new[] { typeof(int), _logEntryType }, null);

                    // Console flags and filter for ensuring all log types are visible during reading
                    _consoleFlagsProp = _logEntriesType.GetProperty("consoleFlags", flags);
                    _getFilterMethod = _logEntriesType.GetMethod("GetFilteringText", flags);
                    _setFilterMethod = _logEntriesType.GetMethod("SetFilteringText", flags);
                }

                if (_logEntryType != null)
                {
                    var instFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _conditionField = _logEntryType.GetField("message", instFlags)
                                   ?? _logEntryType.GetField("condition", instFlags);
                    _modeField = _logEntryType.GetField("mode", instFlags);
                    // Unity 6 provides callstackTextStartUTF16 for precise stack trace splitting
                    _callstackStartField = _logEntryType.GetField("callstackTextStartUTF16", instFlags);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"LogEntries reflection init failed: {ex.Message}");
            }

            _reflectionInitialized = true;
        }

        //! Gets the log entry type string from LogEntry.mode (EntryType flags)
        private static string GetLogType(int mode)
        {
            // Error types (check first - higher priority)
            const int ERROR_MASK = (1 << 0)   // kError
                                 | (1 << 1)   // kAssert
                                 | (1 << 4)   // kFatal
                                 | (1 << 6)   // kAssetImportError
                                 | (1 << 8)   // kScriptingError
                                 | (1 << 11)  // kScriptCompileError
                                 | (1 << 17)  // kScriptingException
                                 | (1 << 21); // kScriptingAssertion
            if ((mode & ERROR_MASK) != 0) return "error";

            // Warning types
            const int WARNING_MASK = (1 << 7)   // kAssetImportWarning
                                   | (1 << 9)   // kScriptingWarning
                                   | (1 << 12); // kScriptCompileWarning
            if ((mode & WARNING_MASK) != 0) return "warning";

            return "log";
        }

        //! Checks if a log type matches the filter
        private static bool MatchesFilter(string type, string filter)
        {
            if (filter == null || filter == "all") return true;
            return type == filter.ToLower();
        }
    }
}
