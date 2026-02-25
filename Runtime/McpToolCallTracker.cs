// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Threading;

namespace realvirtual.MCP
{
    //! Thread-safe static tracker for the current MCP tool call.
    //! Written from WebSocket background threads, read from Editor main thread for UI display.
    public static class McpToolCallTracker
    {
        //! Possible states of a tracked tool call
        public enum CallState
        {
            Idle,       //!< No tool call in progress or recently completed
            Executing,  //!< Tool call is currently executing on main thread
            Done,       //!< Tool call completed successfully
            Error       //!< Tool call completed with error
        }

        private static volatile string _currentToolName;
        private static volatile int _state; // CallState cast to int for Interlocked
        private static long _startedTick;   // Stopwatch tick when call started
        private static long _completedTick; // Stopwatch tick when call completed
        private static volatile bool _dirty; // Set on any state change, consumed by editor UI

        //! Name of the current or most recently executed tool
        public static string CurrentToolName => _currentToolName;

        //! Current state of the tracker
        public static CallState State => (CallState)Interlocked.CompareExchange(ref _state, 0, 0);

        //! Stopwatch tick when the last call completed (for auto-hide timing)
        public static long CompletedTick => Interlocked.Read(ref _completedTick);

        //! Returns true if state changed since last check, and clears the flag.
        //! Called by the editor update loop to detect activity for forced repaints.
        public static bool ConsumeIsDirty()
        {
            if (!_dirty) return false;
            _dirty = false;
            return true;
        }

        //! Elapsed seconds since the current call started. Returns 0 if idle.
        public static double ElapsedSeconds
        {
            get
            {
                var start = Interlocked.Read(ref _startedTick);
                if (start == 0) return 0;
                long end;
                var s = (CallState)Interlocked.CompareExchange(ref _state, 0, 0);
                if (s == CallState.Executing)
                    end = System.Diagnostics.Stopwatch.GetTimestamp();
                else
                    end = Interlocked.Read(ref _completedTick);
                if (end == 0) end = System.Diagnostics.Stopwatch.GetTimestamp();
                return (double)(end - start) / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        //! Called when a tool call begins executing. Thread-safe.
        public static void OnCallStarted(string toolName)
        {
            _currentToolName = toolName;
            Interlocked.Exchange(ref _startedTick,
                System.Diagnostics.Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _state, (int)CallState.Executing);
            _dirty = true;
        }

        //! Called when a tool call finishes. Thread-safe.
        public static void OnCallCompleted(bool success)
        {
            Interlocked.Exchange(ref _completedTick,
                System.Diagnostics.Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _state,
                (int)(success ? CallState.Done : CallState.Error));
            _dirty = true;
        }

        //! Resets state to Idle. Called by the overlay after the auto-hide timer expires.
        public static void Reset()
        {
            Interlocked.Exchange(ref _state, (int)CallState.Idle);
            Interlocked.Exchange(ref _startedTick, 0);
            _currentToolName = null;
        }
    }
}
