// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace realvirtual.MCP.Serialization
{
    //! Serializes and applies the simple public properties of built-in (non-MonoBehaviour)
    //! components such as RectTransform, Canvas, CanvasScaler, Image, Camera or Light.
    //!
    //! MonoBehaviours are handled field-based by ComponentSerializer; Unity's native components
    //! expose their state through properties instead, so this class reflects public instance
    //! properties of simple types (primitives, string, enum, Vector2/3/4, Quaternion, Color,
    //! Color32, Rect). Object references, materials, meshes and arrays are deliberately left out.
    public static class NativeComponentSerializer
    {
        private static readonly HashSet<string> SkippedProperties = new HashSet<string>
        {
            "name", "tag", "hideFlags", "useGUILayout", "runInEditMode", "destroyCancellationToken"
        };

        private static readonly Dictionary<Type, PropertyInfo[]> _cache = new Dictionary<Type, PropertyInfo[]>();

        //! True for property types this serializer can represent as JSON
        public static bool IsSimpleType(Type t)
        {
            return t.IsPrimitive || t == typeof(string) || t.IsEnum ||
                   t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
                   t == typeof(Quaternion) || t == typeof(Color) || t == typeof(Color32) || t == typeof(Rect);
        }

        //! Serializes all readable simple properties of a component. Read-only property names are
        //! listed under "_readOnly" so an agent knows what component_set can not change.
        public static JObject Serialize(Component component)
        {
            var result = new JObject();
            if (component == null)
                return result;

            var readOnly = new JArray();
            foreach (var prop in GetProperties(component.GetType()))
            {
                try
                {
                    var value = prop.GetValue(component);
                    var token = ToToken(value);
                    if (token != null)
                        result[prop.Name] = token;
                    if (!prop.CanWrite || prop.GetSetMethod() == null)
                        readOnly.Add(prop.Name);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"NativeSerializer: Failed to read '{prop.Name}' on {component.GetType().Name}: {ex.Message}");
                }
            }

            if (readOnly.Count > 0)
                result["_readOnly"] = readOnly;
            return result;
        }

        //! Applies JSON values to writable simple properties. Unknown or read-only keys and
        //! conversion failures are reported as errors; written names go to appliedProperties.
        public static List<string> Apply(Component component, JObject data, List<string> appliedProperties = null)
        {
            var errors = new List<string>();
            if (component == null) { errors.Add("Component is null"); return errors; }
            if (data == null || !data.HasValues) { errors.Add("Data is null or empty"); return errors; }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(component, $"MCP Set {component.GetType().Name}");
#endif
            var props = GetProperties(component.GetType());
            var lookup = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in props)
                lookup[p.Name] = p;

            bool any = false;
            foreach (var kv in data)
            {
                if (kv.Key.StartsWith("_"))
                    continue;
                if (!lookup.TryGetValue(kv.Key, out var prop))
                {
                    errors.Add($"Unknown property '{kv.Key}' on {component.GetType().Name}");
                    continue;
                }
                if (!prop.CanWrite || prop.GetSetMethod() == null)
                {
                    errors.Add($"Property '{prop.Name}' on {component.GetType().Name} is read-only");
                    continue;
                }
                try
                {
                    var value = FromToken(kv.Value, prop.PropertyType);
                    prop.SetValue(component, value);
                    appliedProperties?.Add(prop.Name);
                    any = true;
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to set '{prop.Name}': {ex.Message}");
                }
            }

#if UNITY_EDITOR
            if (any && !Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(component);
#endif
            return errors;
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(type, out var cached))
                    return cached;

                var list = new List<PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0)
                        continue;
                    if (SkippedProperties.Contains(p.Name))
                        continue;
                    if (!IsSimpleType(p.PropertyType))
                        continue;
                    if (p.IsDefined(typeof(ObsoleteAttribute), true))
                        continue;
                    list.Add(p);
                }
                var arr = list.ToArray();
                _cache[type] = arr;
                return arr;
            }
        }

        private static JToken ToToken(object value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case bool b: return b;
                case Enum e: return e.ToString();
                case Vector2 v: return new JObject { ["x"] = v.x, ["y"] = v.y };
                case Vector3 v: return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
                case Vector4 v: return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z, ["w"] = v.w };
                case Quaternion q: return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
                case Color c: return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case Color32 c: return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case Rect r: return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? (JToken)null : f;
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? (JToken)null : d;
                default:
                    return value.GetType().IsPrimitive ? JToken.FromObject(value) : null;
            }
        }

        private static float F(JToken t, string key, string altKey, int index, float fallback)
        {
            if (t is JObject o)
            {
                var v = o[key] ?? (altKey != null ? o[altKey] : null);
                return v != null ? v.Value<float>() : fallback;
            }
            if (t is JArray a && index < a.Count)
                return a[index].Value<float>();
            return fallback;
        }

        private static object FromToken(JToken token, Type type)
        {
            if (type.IsEnum)
            {
                if (token.Type == JTokenType.Integer)
                    return Enum.ToObject(type, token.Value<int>());
                return Enum.Parse(type, token.Value<string>(), true);
            }
            if (type == typeof(Vector2)) return new Vector2(F(token, "x", null, 0, 0), F(token, "y", null, 1, 0));
            if (type == typeof(Vector3)) return new Vector3(F(token, "x", null, 0, 0), F(token, "y", null, 1, 0), F(token, "z", null, 2, 0));
            if (type == typeof(Vector4)) return new Vector4(F(token, "x", null, 0, 0), F(token, "y", null, 1, 0), F(token, "z", null, 2, 0), F(token, "w", null, 3, 0));
            if (type == typeof(Quaternion)) return new Quaternion(F(token, "x", null, 0, 0), F(token, "y", null, 1, 0), F(token, "z", null, 2, 0), F(token, "w", null, 3, 1));
            if (type == typeof(Color)) return new Color(F(token, "r", null, 0, 0), F(token, "g", null, 1, 0), F(token, "b", null, 2, 0), F(token, "a", null, 3, 1));
            if (type == typeof(Color32)) return new Color32((byte)F(token, "r", null, 0, 0), (byte)F(token, "g", null, 1, 0), (byte)F(token, "b", null, 2, 0), (byte)F(token, "a", null, 3, 255));
            if (type == typeof(Rect)) return new Rect(F(token, "x", null, 0, 0), F(token, "y", null, 1, 0), F(token, "width", "w", 2, 0), F(token, "height", "h", 3, 0));
            return token.ToObject(type);
        }
    }
}
