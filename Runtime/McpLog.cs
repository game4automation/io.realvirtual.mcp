// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

namespace realvirtual.MCP
{
    //! Centralized logger for the MCP package. All log output is prefixed with [MCP].
    //! Debug messages are suppressed unless DebugEnabled is set to true.
    public static class McpLog
    {
        public static bool DebugEnabled;
        private const string PREFIX = "[MCP] ";

        public static void Info(string msg) => UnityEngine.Debug.Log(PREFIX + msg);
        public static void Warn(string msg) => UnityEngine.Debug.LogWarning(PREFIX + msg);
        public static void Error(string msg) => UnityEngine.Debug.LogError(PREFIX + msg);

        public static void Debug(string msg)
        {
            if (DebugEnabled) UnityEngine.Debug.Log(PREFIX + msg);
        }
    }
}
