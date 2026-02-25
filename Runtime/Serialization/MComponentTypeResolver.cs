// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace realvirtual.MCP.Serialization
{
    //! Resolves type names to Type objects across assemblies with namespace priority.
    //! Supports simple names (e.g., "Drive") and fully-qualified names (e.g., "realvirtual.Drive").
    //! Prioritizes realvirtual and game4automation namespaces over UnityEngine.
    public static class McpTypeResolver
    {
        private static readonly object _lockObject = new object();
        private static Dictionary<string, Type> _typeIndex;

#if UNITY_EDITOR
        //! Clears cache on script recompilation to avoid stale Type objects after domain reload
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            lock (_lockObject)
            {
                _typeIndex = null;
            }
        }
#endif

        //! Resolves a type name to a Type object.
        //! Returns null if the type cannot be found.
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Ensure type index is built
            EnsureTypeIndexBuilt();

            lock (_lockObject)
            {
                // Try exact match first (handles fully-qualified names)
                if (_typeIndex.TryGetValue(typeName, out Type type))
                    return type;

                // Try with namespace prefixes in priority order
                string[] namespacePrefixes = new[]
                {
                    "realvirtual.",
                    "game4automation.",
                    "UnityEngine."
                };

                foreach (var prefix in namespacePrefixes)
                {
                    string qualifiedName = prefix + typeName;
                    if (_typeIndex.TryGetValue(qualifiedName, out type))
                        return type;
                }

                return null;
            }
        }

        //! Clears the type index cache (useful for testing)
        public static void ClearCache()
        {
            lock (_lockObject)
            {
                _typeIndex = null;
            }
        }

        //! Gets count of cached types (for debugging)
        public static int CachedTypeCount
        {
            get
            {
                EnsureTypeIndexBuilt();
                lock (_lockObject)
                {
                    return _typeIndex?.Count ?? 0;
                }
            }
        }

        private static void EnsureTypeIndexBuilt()
        {
            lock (_lockObject)
            {
                if (_typeIndex == null)
                {
                    BuildTypeIndex();
                }
            }
        }

        private static void BuildTypeIndex()
        {
            _typeIndex = new Dictionary<string, Type>();

            // Scan all loaded assemblies for types
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        // Index by full name
                        if (!string.IsNullOrEmpty(type.FullName))
                        {
                            // Store with priority: realvirtual/game4automation types override Unity types
                            if (!_typeIndex.ContainsKey(type.FullName) ||
                                IsPriorityNamespace(type.Namespace))
                            {
                                _typeIndex[type.FullName] = type;
                            }
                        }

                        // Also index by simple name for convenience
                        if (!string.IsNullOrEmpty(type.Name))
                        {
                            // Only store if no existing entry or this is higher priority
                            if (!_typeIndex.ContainsKey(type.Name) ||
                                IsPriorityNamespace(type.Namespace))
                            {
                                _typeIndex[type.Name] = type;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be scanned (e.g., dynamic assemblies)
                }
            }
        }

        private static bool IsPriorityNamespace(string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
                return false;

            return namespaceName.StartsWith("realvirtual") ||
                   namespaceName.StartsWith("game4automation");
        }
    }
}
