// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace realvirtual.MCP.Serialization
{
    //! Thrown when a serialization budget (depth, time, output size) is exceeded.
    //! Deliberately NOT swallowed by the error handler so the abort propagates.
    internal sealed class McpSerializationAbortException : Exception
    {
        public McpSerializationAbortException(string message) : base(message) { }
    }

    //! Hardened Newtonsoft JSON serialization for all MCP serializations of arbitrary objects.
    //!
    //! Newtonsoft's default contract resolver serializes C# PROPERTY getters via reflection.
    //! On Unity types this is fatal: Matrix4x4.rotation asserts 'ValidTRS()', and properties
    //! like Matrix4x4.inverse/transpose return new structs, causing unbounded recursion in
    //! JsonSerializerInternalWriter (observed: 4+ min main thread freeze, 1.9 GB Editor.log).
    //!
    //! This class enforces:
    //! - Fields only (public + [SerializeField]) - never property getters
    //! - Explicit converters for Unity structs (Matrix4x4, Vector*, Quaternion, Color, ...)
    //! - UnityEngine.Object references as short form {name, type, hierarchyPath} - never deep
    //! - Depth, time and output-size budgets via GuardedJTokenWriter
    public static class McpSafeJson
    {
        //! Maximum nesting depth for serialized object graphs
        public const int MaxDepth = 5;

        //! Time budget in milliseconds for a single serialization
        public const int MaxSerializationMs = 2000;

        //! Maximum number of JSON tokens written before aborting (approximates ~1 MB output)
        public const int MaxTokens = 50000;

        //! Maximum output payload size in characters (~1 MB)
        public const int MaxOutputChars = 1000000;

        //! Serializes an arbitrary object with hardened settings and budgets.
        //! Returns the token on success; on abort/failure returns null and sets error
        //! to an honest, user-actionable message.
        public static JToken SerializeGuarded(object value, out string error)
        {
            error = null;
            if (value == null)
                return null;

            try
            {
                var serializer = CreateSerializer();
                using (var writer = new GuardedJTokenWriter(MaxDepth, MaxSerializationMs, MaxTokens))
                {
                    serializer.Serialize(writer, value);
                    return writer.Token;
                }
            }
            catch (Exception ex)
            {
                var abort = FindAbort(ex);
                error = abort != null
                    ? $"serialization aborted: {abort.Message} - use component_get with specific fields"
                    : $"serialization failed: {ex.Message}";
                return null;
            }
        }

        //! Creates a JsonSerializer hardened for Unity object graphs.
        //! Central settings for all MCP serializations - do not use default settings anywhere.
        public static JsonSerializer CreateSerializer()
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = SafeFieldsContractResolver.Instance,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = MaxDepth,
                Converters = new List<JsonConverter>
                {
                    new UnityStructConverter(),
                    new UnityObjectRefConverter()
                },
                Error = (sender, args) =>
                {
                    // Skip faulty members instead of throwing/spamming,
                    // but never swallow budget aborts - those must propagate
                    if (!(ContainsAbort(args.ErrorContext.Error)))
                        args.ErrorContext.Handled = true;
                }
            };
            return JsonSerializer.Create(settings);
        }

        private static bool ContainsAbort(Exception ex)
        {
            return FindAbort(ex) != null;
        }

        private static McpSerializationAbortException FindAbort(Exception ex)
        {
            while (ex != null)
            {
                if (ex is McpSerializationAbortException abort)
                    return abort;
                ex = ex.InnerException;
            }
            return null;
        }

        //! Gets the full hierarchy path of a GameObject
        internal static string GetGameObjectPath(GameObject obj)
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

    //! Contract resolver that serializes only fields (public + [SerializeField] private),
    //! matching Unity Inspector rules. Never invokes C# property getters - property
    //! getters on Unity types (Matrix4x4.rotation, Transform.localToWorldMatrix, ...)
    //! assert, allocate or recurse and must never run during reflection serialization.
    internal sealed class SafeFieldsContractResolver : DefaultContractResolver
    {
        public static readonly SafeFieldsContractResolver Instance = new SafeFieldsContractResolver();

        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            var members = new List<MemberInfo>();
            var seen = new HashSet<string>();

            for (var type = objectType; type != null && type != typeof(object); type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (field.IsDefined(typeof(NonSerializedAttribute), false))
                        continue;
                    if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                        continue;
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))
                        continue;
                    if (!seen.Add(field.Name))
                        continue;

                    members.Add(field);
                }
            }

            return members;
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            property.Readable = true;
            property.Writable = member is FieldInfo;
            return property;
        }
    }

    //! Explicit converter for Unity struct types. Writes only raw component values -
    //! never property getters (Matrix4x4.rotation asserts 'ValidTRS()' on non-TRS matrices,
    //! Matrix4x4.inverse/transpose return new structs and cause unbounded recursion).
    internal sealed class UnityStructConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector2) || objectType == typeof(Vector3) ||
                   objectType == typeof(Vector4) || objectType == typeof(Quaternion) ||
                   objectType == typeof(Color) || objectType == typeof(Color32) ||
                   objectType == typeof(Rect) || objectType == typeof(Bounds) ||
                   objectType == typeof(Matrix4x4) ||
                   objectType == typeof(Vector2Int) || objectType == typeof(Vector3Int) ||
                   objectType == typeof(RectInt) || objectType == typeof(BoundsInt);
        }

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException("UnityStructConverter is write-only");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case Vector2 v:
                    WriteObject(writer, ("x", v.x), ("y", v.y));
                    break;
                case Vector3 v:
                    WriteObject(writer, ("x", v.x), ("y", v.y), ("z", v.z));
                    break;
                case Vector4 v:
                    WriteObject(writer, ("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w));
                    break;
                case Quaternion q:
                    WriteObject(writer, ("x", q.x), ("y", q.y), ("z", q.z), ("w", q.w));
                    break;
                case Color c:
                    WriteObject(writer, ("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));
                    break;
                case Color32 c:
                    WriteObject(writer, ("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));
                    break;
                case Rect r:
                    WriteObject(writer, ("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
                    break;
                case Bounds b:
                    writer.WriteStartObject();
                    writer.WritePropertyName("center");
                    WriteObject(writer, ("x", b.center.x), ("y", b.center.y), ("z", b.center.z));
                    writer.WritePropertyName("size");
                    WriteObject(writer, ("x", b.size.x), ("y", b.size.y), ("z", b.size.z));
                    writer.WriteEndObject();
                    break;
                case Matrix4x4 m:
                    writer.WriteStartObject();
                    for (int row = 0; row < 4; row++)
                    {
                        for (int col = 0; col < 4; col++)
                        {
                            writer.WritePropertyName($"m{row}{col}");
                            writer.WriteValue(m[row, col]);
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case Vector2Int v:
                    WriteObject(writer, ("x", (float)v.x), ("y", (float)v.y));
                    break;
                case Vector3Int v:
                    WriteObject(writer, ("x", (float)v.x), ("y", (float)v.y), ("z", (float)v.z));
                    break;
                case RectInt r:
                    WriteObject(writer, ("x", (float)r.x), ("y", (float)r.y), ("width", (float)r.width), ("height", (float)r.height));
                    break;
                case BoundsInt b:
                    writer.WriteStartObject();
                    writer.WritePropertyName("position");
                    WriteObject(writer, ("x", (float)b.position.x), ("y", (float)b.position.y), ("z", (float)b.position.z));
                    writer.WritePropertyName("size");
                    WriteObject(writer, ("x", (float)b.size.x), ("y", (float)b.size.y), ("z", (float)b.size.z));
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteNull();
                    break;
            }
        }

        private static void WriteObject(JsonWriter writer, params (string name, float value)[] values)
        {
            writer.WriteStartObject();
            foreach (var (name, val) in values)
            {
                writer.WritePropertyName(name);
                writer.WriteValue(val);
            }
            writer.WriteEndObject();
        }
    }

    //! Converter for UnityEngine.Object references. Never serializes them deeply -
    //! always writes the short form {name, type, hierarchyPath}. Deep serialization of
    //! Transform/GameObject/Component graphs via reflection is what froze the editor.
    internal sealed class UnityObjectRefConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(objectType);
        }

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException("UnityObjectRefConverter is write-only");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var obj = value as UnityEngine.Object;
            if (obj == null) // Unity fake-null covers destroyed objects
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(obj.name);
            writer.WritePropertyName("type");
            writer.WriteValue(obj.GetType().Name);

            string path = null;
            if (obj is GameObject go)
                path = McpSafeJson.GetGameObjectPath(go);
            else if (obj is Component comp)
                path = McpSafeJson.GetGameObjectPath(comp.gameObject);

            if (path != null)
            {
                writer.WritePropertyName("hierarchyPath");
                writer.WriteValue(path);
            }

            writer.WriteEndObject();
        }
    }

    //! JTokenWriter with depth, time and output-size budgets.
    //! Throws McpSerializationAbortException when a budget is exceeded so that
    //! runaway serializations abort in milliseconds instead of freezing the editor.
    internal sealed class GuardedJTokenWriter : JTokenWriter
    {
        private readonly int _maxDepth;
        private readonly int _maxTokens;
        private readonly long _deadlineTimestamp;
        private int _tokenCount;

        public GuardedJTokenWriter(int maxDepth, int maxMilliseconds, int maxTokens)
        {
            _maxDepth = maxDepth;
            _maxTokens = maxTokens;
            _deadlineTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() +
                                 maxMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000;
        }

        private void CheckBudget(bool entersContainer)
        {
            if (entersContainer && Top >= _maxDepth)
                throw new McpSerializationAbortException($"too deep (max depth {_maxDepth})");

            if (++_tokenCount > _maxTokens)
                throw new McpSerializationAbortException($"too large (> {_maxTokens} tokens)");

            // Check the clock every 256 tokens to keep the hot path cheap
            if ((_tokenCount & 0xFF) == 0 &&
                System.Diagnostics.Stopwatch.GetTimestamp() > _deadlineTimestamp)
                throw new McpSerializationAbortException("time budget exceeded");
        }

        public override void WriteStartObject() { CheckBudget(true); base.WriteStartObject(); }
        public override void WriteStartArray() { CheckBudget(true); base.WriteStartArray(); }
        public override void WritePropertyName(string name) { CheckBudget(false); base.WritePropertyName(name); }
        public override void WriteNull() { CheckBudget(false); base.WriteNull(); }
        public override void WriteValue(string value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(bool value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(int value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(uint value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(long value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(ulong value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(float value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(double value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(decimal value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(DateTime value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(byte[] value) { CheckBudget(false); base.WriteValue(value); }
        public override void WriteValue(object value) { CheckBudget(false); base.WriteValue(value); }
    }
}
