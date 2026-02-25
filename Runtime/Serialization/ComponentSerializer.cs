// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Serialization
{
    //! Serializes MonoBehaviour components to JSON for MCP tools.
    //! Uses cached reflection data for optimal performance.
    public static class ComponentSerializer
    {
        // Shared JSON serializer settings for [Serializable] classes
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };

        //! Serializes all serializable fields of a MonoBehaviour to JObject
        public static JObject Serialize(MonoBehaviour component)
        {
            if (component == null)
                return new JObject { ["error"] = "Component is null" };

            var type = component.GetType();
            var reflData = ReflectionCache.GetReflectionData(type);

            var result = new JObject();

            // Serialize all fields
            foreach (var field in reflData.SerializableFields)
            {
                try
                {
                    var value = field.GetValue(component);
                    if (value == null)
                        continue;

                    var token = SerializeValue(value, field.Category, field.ElementType);
                    if (token != null)
                        result[field.Name] = token;
                }
                catch (Exception ex)
                {
                    // Log warning but continue with other fields - don't crash entire serialization
                    McpLog.Warn($"Serializer: Failed to serialize field '{field.Name}' on {type.Name}: {ex.Message}");
                }
            }

            return result;
        }

        //! Serializes a named component type on a GameObject
        public static JObject Serialize(GameObject go, string componentTypeName)
        {
            if (go == null)
                return new JObject { ["error"] = "GameObject is null" };

            var type = realvirtual.MCP.Serialization.McpTypeResolver.Resolve(componentTypeName);
            if (type == null)
                return new JObject { ["error"] = $"Type '{componentTypeName}' not found" };

            var comp = go.GetComponent(type) as MonoBehaviour;
            if (comp == null)
                return new JObject { ["error"] = $"Component '{componentTypeName}' not found on '{go.name}'" };

            return Serialize(comp);
        }

        //! Serializes only specific fields by name
        public static JObject SerializeFields(MonoBehaviour component, string[] fieldNames)
        {
            if (component == null)
                return new JObject { ["error"] = "Component is null" };

            if (fieldNames == null || fieldNames.Length == 0)
                return new JObject { ["error"] = "No field names specified" };

            var type = component.GetType();
            var reflData = ReflectionCache.GetReflectionData(type);

            var result = new JObject();
            var fieldNameSet = new HashSet<string>(fieldNames);

            // Serialize only requested fields
            foreach (var field in reflData.SerializableFields)
            {
                if (!fieldNameSet.Contains(field.Name))
                    continue;

                try
                {
                    var value = field.GetValue(component);
                    if (value == null)
                        continue;

                    var token = SerializeValue(value, field.Category, field.ElementType);
                    if (token != null)
                        result[field.Name] = token;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Serializer: Failed to serialize field '{field.Name}': {ex.Message}");
                }
            }

            return result;
        }

        //! Serializes ALL components on a GameObject, keyed by type name
        public static JObject SerializeAll(GameObject go, Func<Type, bool> typeFilter = null)
        {
            if (go == null)
                return new JObject { ["error"] = "GameObject is null" };

            var result = new JObject();
            var componentCounts = new Dictionary<string, int>();

            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;

                var type = component.GetType();

                // Apply optional type filter
                if (typeFilter != null && !typeFilter(type))
                    continue;

                try
                {
                    var componentData = Serialize(component);
                    if (componentData == null || !componentData.HasValues)
                        continue;

                    // Handle multiple components of same type
                    string key = type.Name;
                    if (componentCounts.ContainsKey(type.Name))
                    {
                        componentCounts[type.Name]++;
                        key = $"{type.Name}_{componentCounts[type.Name]}";
                    }
                    else
                    {
                        componentCounts[type.Name] = 0;
                    }

                    result[key] = componentData;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Serializer: Failed to serialize component {type.Name}: {ex.Message}");
                }
            }

            return result;
        }

        //! Serializes a value based on its category
        private static JToken SerializeValue(object value, FieldCategory category, Type elementType)
        {
            if (value == null)
                return null;

            switch (category)
            {
                case FieldCategory.Primitive:
                    return SerializePrimitive(value);

                case FieldCategory.UnityPrimitive:
                    return SerializeUnityPrimitive(value);

                case FieldCategory.GameObject:
                    return SerializeGameObject((GameObject)value);

                case FieldCategory.Component:
                    return SerializeComponent((Component)value);

                case FieldCategory.Material:
                    return SerializeMaterial((Material)value);

                case FieldCategory.ScriptableObject:
                    return SerializeScriptableObject((ScriptableObject)value);

                case FieldCategory.PrimitiveArray:
                case FieldCategory.UnityPrimitiveArray:
                    return SerializeArray(value, category);

                case FieldCategory.ObjectArray:
                case FieldCategory.ObjectList:
                    return SerializeObjectCollection((IEnumerable)value, elementType);

                case FieldCategory.Serializable:
                    return SerializeSerializable(value);

                default:
                    return null;
            }
        }

        private static JToken SerializePrimitive(object value)
        {
            if (value == null)
                return null;

            // Enums serialize as string
            if (value.GetType().IsEnum)
                return value.ToString();

            return JToken.FromObject(value);
        }

        private static JToken SerializeUnityPrimitive(object value)
        {
            // Explicit decomposition - never use JToken.FromObject on Unity types
            // (causes circular references with Vector3.normalized, Color.linear, etc.)
            switch (value)
            {
                case Vector2 v:
                    return new JObject { ["x"] = v.x, ["y"] = v.y };

                case Vector3 v:
                    return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

                case Vector4 v:
                    return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z, ["w"] = v.w };

                case Quaternion q:
                    return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };

                case Color c:
                    return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };

                case Color32 c:
                    return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };

                case Rect r:
                    return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };

                case Bounds b:
                    return new JObject
                    {
                        ["center"] = new JObject { ["x"] = b.center.x, ["y"] = b.center.y, ["z"] = b.center.z },
                        ["size"] = new JObject { ["x"] = b.size.x, ["y"] = b.size.y, ["z"] = b.size.z }
                    };

                default:
                    return null;
            }
        }

        private static JToken SerializeGameObject(GameObject go)
        {
            if (go == null)
                return null;

            // Serialize as hierarchy path (read-only for MCP use case)
            return GetGameObjectPath(go);
        }

        private static JToken SerializeComponent(Component component)
        {
            if (component == null)
                return null;

            // Serialize as path + type + index (for disambiguation when multiple of same type exist)
            var go = component.gameObject;
            var type = component.GetType();
            var result = new JObject
            {
                ["path"] = GetGameObjectPath(go),
                ["type"] = type.Name
            };

            // Add index if there are multiple components of the same type
            var components = go.GetComponents(type);
            if (components.Length > 1)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == component)
                    {
                        result["index"] = i;
                        break;
                    }
                }
            }

            return result;
        }

        private static JToken SerializeMaterial(Material mat)
        {
            if (mat == null)
                return null;

            // Serialize as name only (read-only for MCP use case)
            return mat.name;
        }

        private static JToken SerializeScriptableObject(ScriptableObject so)
        {
            if (so == null)
                return null;

            // Serialize as name only (read-only for MCP use case)
            return so.name;
        }

        private static JToken SerializeArray(object arrayValue, FieldCategory category)
        {
            if (arrayValue == null)
                return null;

            var arr = new JArray();
            var enumerable = (IEnumerable)arrayValue;

            foreach (var element in enumerable)
            {
                if (element == null)
                {
                    arr.Add(null);
                    continue;
                }

                JToken elementToken = null;

                if (category == FieldCategory.PrimitiveArray)
                {
                    elementToken = SerializePrimitive(element);
                }
                else if (category == FieldCategory.UnityPrimitiveArray)
                {
                    elementToken = SerializeUnityPrimitive(element);
                }

                arr.Add(elementToken);
            }

            return arr;
        }

        private static JToken SerializeObjectCollection(IEnumerable collection, Type elementType)
        {
            if (collection == null)
                return null;

            var arr = new JArray();

            foreach (var element in collection)
            {
                if (element == null)
                {
                    arr.Add(null);
                    continue;
                }

                // Reference types - serialize as path/name
                if (element is GameObject go)
                {
                    arr.Add(GetGameObjectPath(go));
                }
                else if (element is Component comp)
                {
                    arr.Add(new JObject
                    {
                        ["path"] = GetGameObjectPath(comp.gameObject),
                        ["type"] = comp.GetType().Name
                    });
                }
                else if (element is Material mat)
                {
                    arr.Add(mat.name);
                }
                else
                {
                    arr.Add(null); // Unknown reference type
                }
            }

            return arr;
        }

        private static JToken SerializeSerializable(object value)
        {
            if (value == null)
                return null;

            try
            {
                var serializer = JsonSerializer.Create(_jsonSettings);
                return JObject.FromObject(value, serializer);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Serializer: Failed to serialize [Serializable] object: {ex.Message}");
                return null;
            }
        }

        //! Gets the full hierarchy path of a GameObject
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return null;

            var path = obj.name;
            var parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
