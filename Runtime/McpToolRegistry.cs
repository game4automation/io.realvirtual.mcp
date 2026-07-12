// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace realvirtual.MCP
{
    //! Tool entry containing metadata and method info for an MCP tool
    public class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public MethodInfo Method { get; set; }
        public string InputSchema { get; set; }
    }

    //! Registry that discovers and manages all MCP tools defined in the application.
    //!
    //! Scans all assemblies at startup to find methods marked with [McpTool] attribute,
    //! generates JSON schemas for their parameters, and provides fast lookup for tool execution.
    //!
    //! Tools are discovered once at startup and cached for performance.
    public class McpToolRegistry
    {
        private readonly Dictionary<string, ToolEntry> _tools = new Dictionary<string, ToolEntry>();
        private bool _initialized = false;
        private string _instructions;

        //! Gets the number of registered tools
        public int ToolCount => _tools.Count;

        //! Gets all registered tool names
        public IEnumerable<string> ToolNames => _tools.Keys;

        //! Discovers all MCP tools in loaded assemblies.
        //!
        //! Scans all non-system assemblies for public static methods marked with [McpTool].
        //! Builds JSON schemas from method parameters and caches them for fast lookup.
        public void DiscoverTools()
        {
            if (_initialized)
                return;

            _tools.Clear();

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !IsSystemAssembly(a));

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        ScanAssembly(assembly);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Registry: Error scanning assembly {assembly.FullName}: {ex.Message}");
                    }
                }

                _initialized = true;
                McpLog.Debug($"Registry: Discovered {_tools.Count} MCP tools");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Registry: Error during tool discovery: {ex.Message}\n{ex.StackTrace}");
            }
        }

        //! Checks if assembly is a system assembly that should be skipped
        private bool IsSystemAssembly(Assembly assembly)
        {
            var name = assembly.FullName;
            return name.StartsWith("System") ||
                   name.StartsWith("mscorlib") ||
                   name.StartsWith("netstandard") ||
                   name.StartsWith("Unity.") ||
                   name.StartsWith("UnityEngine") ||
                   name.StartsWith("UnityEditor");
        }

        private const string TOOL_ATTR_FULLNAME = "realvirtual.MCP.McpToolAttribute";
        private const string PARAM_ATTR_FULLNAME = "realvirtual.MCP.McpParamAttribute";

        //! Scans a single assembly for MCP tools.
        //!
        //! Uses string-based attribute matching (GetType().FullName) instead of
        //! GetCustomAttribute&lt;T&gt;() to support cross-assembly discovery.
        //! This is critical when McpToolAttribute exists in both io.realvirtual.mcp
        //! (primary) and realvirtual.base (stub) - they are different Type objects
        //! but share the same FullName.
        private void ScanAssembly(Assembly assembly)
        {
            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

                foreach (var method in methods)
                {
                    // String-based matching: works across assembly boundaries
                    var attr = method.GetCustomAttributes(false)
                        .FirstOrDefault(a => a.GetType().FullName == TOOL_ATTR_FULLNAME);
                    if (attr == null)
                        continue;

                    // Validate method signature
                    if (method.ReturnType != typeof(string))
                    {
                        McpLog.Warn($"Registry: Skipping {type.Name}.{method.Name}: Return type must be string");
                        continue;
                    }

                    // Read properties via reflection (cross-assembly safe)
                    var description = attr.GetType().GetProperty("Description")?.GetValue(attr) as string;
                    var name = attr.GetType().GetProperty("Name")?.GetValue(attr) as string;

                    RegisterTool(method, description, name);
                }
            }
        }

        //! Registers a discovered tool
        private void RegisterTool(MethodInfo method, string description, string nameOverride)
        {
            var toolName = nameOverride ?? PascalToSnakeCase(method.Name);
            var schema = GenerateInputSchema(method);

            var entry = new ToolEntry
            {
                Name = toolName,
                Description = description ?? "",
                Method = method,
                InputSchema = schema
            };

            if (_tools.ContainsKey(toolName))
            {
                McpLog.Warn($"Registry: Duplicate tool name '{toolName}' - using {method.DeclaringType.Name}.{method.Name}");
            }

            _tools[toolName] = entry;
        }

        //! Converts PascalCase to snake_case
        private string PascalToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder();
            sb.Append(char.ToLower(input[0]));

            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]))
                {
                    sb.Append('_');
                    sb.Append(char.ToLower(input[i]));
                }
                else
                {
                    sb.Append(input[i]);
                }
            }

            return sb.ToString();
        }

        //! Generates JSON schema for method parameters
        private string GenerateInputSchema(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return "{\"type\":\"object\",\"properties\":{}}";
            }

            var sb = new StringBuilder();
            sb.Append("{\"type\":\"object\",\"properties\":{");

            var required = new List<string>();
            bool first = true;

            foreach (var param in parameters)
            {
                if (!first)
                    sb.Append(",");
                first = false;

                var paramName = param.Name;
                // String-based matching for McpParamAttribute (cross-assembly safe)
                var paramAttr = param.GetCustomAttributes(false)
                    .FirstOrDefault(a => a.GetType().FullName == PARAM_ATTR_FULLNAME);
                var description = paramAttr?.GetType().GetProperty("Description")?.GetValue(paramAttr) as string ?? "";

                sb.Append($"\"{paramName}\":{{");
                sb.Append($"\"type\":\"{GetJsonType(param.ParameterType)}\"");

                if (!string.IsNullOrEmpty(description))
                {
                    sb.Append($",\"description\":\"{EscapeJson(description)}\"");
                }

                sb.Append("}");

                // Required if no default value
                if (!param.HasDefaultValue)
                {
                    required.Add(paramName);
                }
            }

            sb.Append("}");

            if (required.Count > 0)
            {
                sb.Append(",\"required\":[");
                sb.Append(string.Join(",", required.Select(r => $"\"{r}\"")));
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        //! Maps C# type to JSON schema type
        private string GetJsonType(Type type)
        {
            if (type == typeof(string))
                return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "boolean";

            // Default to string for unknown types
            return "string";
        }

        //! Escapes JSON string values
        private string EscapeJson(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        //! Sets the MCP instructions content (from discovered *.mcp.md files).
        //! Included in the __discover__ response so AI clients receive usage conventions.
        public void SetInstructions(string instructions)
        {
            _instructions = instructions;
        }

        //! Gets JSON array of all tool schemas, optionally including instructions
        public string GetToolSchemas()
        {
            var sb = new StringBuilder();
            sb.Append("{\"tools\":[");

            bool first = true;
            foreach (var entry in _tools.Values)
            {
                if (!first)
                    sb.Append(",");
                first = false;

                sb.Append("{");
                sb.Append($"\"name\":\"{entry.Name}\",");
                sb.Append($"\"description\":\"{EscapeJson(entry.Description)}\",");
                sb.Append($"\"inputSchema\":{entry.InputSchema}");
                sb.Append("}");
            }

            sb.Append("]");

            if (!string.IsNullOrEmpty(_instructions))
            {
                sb.Append($",\"instructions\":\"{EscapeJson(_instructions)}\"");
            }

            sb.Append($",\"schema_version\":\"{McpVersion.Version}\"}}");
            return sb.ToString();
        }

        //! Calls a tool by name with provided arguments
        //! @param name Tool name
        //! @param arguments Dictionary of argument name to value
        //! @return Tool result as JSON string
        public string CallTool(string name, Dictionary<string, object> arguments)
        {
            if (!_tools.TryGetValue(name, out var entry))
            {
                return $"{{\"error\":\"Tool '{name}' not found\"}}";
            }

            try
            {
                var parameters = entry.Method.GetParameters();
                var args = new object[parameters.Length];
                // Keys already bound to an earlier parameter - so a shared alias (e.g. "path",
                // an alias of both assetPath and name) is not consumed twice.
                var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    if (arguments != null && TryResolveArgument(arguments, param.Name, consumedKeys, out var value, out var usedAlias))
                    {
                        try
                        {
                            args[i] = ConvertValue(value, param.ParameterType);
                        }
                        catch (Exception ex)
                        {
                            return $"{{\"error\":\"Invalid argument '{param.Name}': {EscapeJson(ex.Message)}\"}}";
                        }
                    }
                    else if (param.HasDefaultValue)
                    {
                        args[i] = param.DefaultValue;
                    }
                    else
                    {
                        var aliasHint = AliasHint(param.Name);
                        return $"{{\"error\":\"Missing required argument '{param.Name}'{EscapeJson(aliasHint)}\"}}";
                    }
                }

                var result = entry.Method.Invoke(null, args);
                return result?.ToString() ?? "{\"result\":null}";
            }
            catch (Exception ex)
            {
                McpLog.Error($"Registry: Error calling tool '{name}': {ex.Message}\n{ex.StackTrace}");
                return $"{{\"error\":\"Tool execution failed: {EscapeJson(ex.Message)}\"}}";
            }
        }

        //! Accepted alias keys per canonical parameter name.
        //! Lets agents use common synonyms (path/target for name, component for componentType,
        //! query for searchTerm, ...) instead of failing with "missing required argument".
        //! Matching is case-insensitive; the canonical parameter always wins if present.
        private static readonly Dictionary<string, string[]> _paramAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = new[] { "target", "path", "objectName", "gameObject", "object", "go" },
            ["componentType"] = new[] { "component", "type", "componentName" },
            ["searchTerm"] = new[] { "query", "search", "term", "q", "name" },
            ["assetPath"] = new[] { "path", "prefabPath", "asset", "assetpath" },
            ["methodName"] = new[] { "method" },
            ["properties"] = new[] { "props", "values", "properties" },
        };

        //! Resolves an argument for a parameter: exact key, then case-insensitive key,
        //! then any registered alias key. Keys already bound to an earlier parameter
        //! (tracked in <paramref name="consumedKeys"/>) are skipped. Returns true if found.
        private bool TryResolveArgument(Dictionary<string, object> arguments, string paramName,
            HashSet<string> consumedKeys, out object value, out string usedAlias)
        {
            usedAlias = null;
            value = null;

            // 1. Exact match
            if (!consumedKeys.Contains(paramName) && arguments.TryGetValue(paramName, out value))
            {
                consumedKeys.Add(paramName);
                return true;
            }

            // 2. Case-insensitive match
            foreach (var kv in arguments)
            {
                if (consumedKeys.Contains(kv.Key))
                    continue;
                if (string.Equals(kv.Key, paramName, StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    usedAlias = kv.Key;
                    consumedKeys.Add(kv.Key);
                    return true;
                }
            }

            // 3. Registered aliases (case-insensitive)
            if (_paramAliases.TryGetValue(paramName, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    foreach (var kv in arguments)
                    {
                        if (consumedKeys.Contains(kv.Key))
                            continue;
                        if (string.Equals(kv.Key, alias, StringComparison.OrdinalIgnoreCase))
                        {
                            value = kv.Value;
                            usedAlias = kv.Key;
                            consumedKeys.Add(kv.Key);
                            McpLog.Debug($"Registry: accepted alias '{kv.Key}' for parameter '{paramName}'");
                            return true;
                        }
                    }
                }
            }

            value = null;
            return false;
        }

        //! Builds a "(you can also pass it as ...)" hint for a missing parameter.
        private string AliasHint(string paramName)
        {
            if (_paramAliases.TryGetValue(paramName, out var aliases) && aliases.Length > 0)
                return $" (this GameObject/value identifier also accepts: {string.Join(", ", aliases)})";
            return "";
        }

        //! Converts argument value to target parameter type
        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            var valueType = value.GetType();

            // Direct assignment if types match
            if (targetType.IsAssignableFrom(valueType))
                return value;

            // String conversion
            if (targetType == typeof(string))
                return value.ToString();

            // Numeric conversions
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                throw new InvalidCastException($"Cannot convert {valueType.Name} to {targetType.Name}");
            }
        }

        //! Gets a tool entry by name
        public ToolEntry GetTool(string name)
        {
            _tools.TryGetValue(name, out var entry);
            return entry;
        }

        //! Gets all tool entries as dictionary
        //! @return Dictionary of tool name to ToolEntry
        public Dictionary<string, ToolEntry> GetAllToolEntries()
        {
            return new Dictionary<string, ToolEntry>(_tools);
        }
    }
}
