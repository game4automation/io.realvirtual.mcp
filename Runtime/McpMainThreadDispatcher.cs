// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace realvirtual.MCP
{
    //! Dispatches actions to Unity's main thread from background threads.
    //!
    //! Unity API calls must happen on the main thread. This component queues
    //! actions from WebSocket handlers (which run on background threads) and
    //! executes them in Update() on the main thread.
    //!
    //! Automatically created and managed by McpBridge component.
    public class McpMainThreadDispatcher : MonoBehaviour
    {
        private static McpMainThreadDispatcher _instance;
        private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
        private int _mainThreadId;
        private static long _lastPumpTick;

        //! Gets the singleton instance. Returns null if not yet created.
        //! The instance is created on the main thread by EnsureInstance().
        //! Background threads should check for null before using.
        public static McpMainThreadDispatcher Instance => _instance;

        //! Timestamp of the last ProcessPending() call, for stall detection by background threads.
        //! Uses Stopwatch ticks. Zero means pump has never run.
        public static long LastPumpTick => Interlocked.Read(ref _lastPumpTick);

        //! Returns seconds since the main thread pump last ran. Returns -1 if pump has never run.
        public static double SecondsSinceLastPump
        {
            get
            {
                var tick = Interlocked.Read(ref _lastPumpTick);
                if (tick == 0) return -1;
                return (double)(System.Diagnostics.Stopwatch.GetTimestamp() - tick)
                       / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        //! Creates the singleton instance on the main thread. Must be called from main thread only.
        //! Called by McpEditorBridge.OnEditorUpdate to ensure the dispatcher exists before
        //! any WebSocket background threads try to use it.
        public static McpMainThreadDispatcher EnsureInstance()
        {
            if (_instance == null)
            {
                var go = new GameObject("McpMainThreadDispatcher");
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<McpMainThreadDispatcher>();
                if (Application.isPlaying)
                    DontDestroyOnLoad(go);
            }
            return _instance;
        }

        //! Returns true if called from Unity's main thread
        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                if (Application.isPlaying)
                    DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        void Update()
        {
            // Process all queued actions on main thread
            while (_actionQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Dispatcher: Error executing action: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        //! Enqueues an action to be executed on the main thread.
        //! @param action Action to execute
        public void Enqueue(Action action)
        {
            if (action == null)
                return;

            if (IsMainThread)
            {
                // Already on main thread, execute immediately
                action();
            }
            else
            {
                _actionQueue.Enqueue(action);
            }
        }

        //! Enqueues a function to be executed on the main thread and returns the result.
        //! @param func Function to execute that returns a value
        //! @return Task that completes when the function has been executed
        public Task<T> EnqueueWithResult<T>(Func<T> func)
        {
            if (func == null)
                return Task.FromResult(default(T));

            if (IsMainThread)
            {
                // Already on main thread, execute immediately
                return Task.FromResult(func());
            }

            var tcs = new TaskCompletionSource<T>();

            _actionQueue.Enqueue(() =>
            {
                try
                {
                    var result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        //! Processes all pending actions in the queue.
        //! Called by McpEditorBridge via EditorApplication.update in editor mode.
        public void ProcessPending()
        {
            Interlocked.Exchange(ref _lastPumpTick, System.Diagnostics.Stopwatch.GetTimestamp());
            while (_actionQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Dispatcher: Error executing action: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        //! Clears all pending actions from the queue
        public void ClearQueue()
        {
            while (_actionQueue.TryDequeue(out _)) { }
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
