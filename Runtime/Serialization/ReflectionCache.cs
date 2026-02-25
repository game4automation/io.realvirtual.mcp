// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace realvirtual.MCP.Serialization
{
    //! Defines categories for field serialization to enable optimized handling
    public enum FieldCategory
    {
        Primitive,            // int, float, bool, string, enum
        UnityPrimitive,       // Vector2, Vector3, Vector4, Quaternion, Color, Color32, Rect, Bounds
        GameObject,           // GameObject references (serialized as path string)
        Component,            // Component/MonoBehaviour references (serialized as path + type)
        Material,             // Material references (name only)
        ScriptableObject,     // ScriptableObject references (name only)
        PrimitiveArray,       // float[], int[], string[], enum[]
        UnityPrimitiveArray,  // Vector3[], Color[], etc.
        ObjectArray,          // Component[], GameObject[] etc.
        ObjectList,           // List<Component>, List<GameObject> etc.
        Serializable,         // [Serializable] classes/structs
        Unsupported           // UnityEvent, delegates, AnimationCurve, Gradient, etc. - skipped
    }

    //! Cached information about a serializable field
    public class CachedFieldInfo
    {
        public FieldInfo Field { get; }
        public string Name => Field.Name;
        public Type FieldType => Field.FieldType;
        public FieldCategory Category { get; }
        public Type ElementType { get; } // For arrays/lists
        public bool IsPublic { get; }

        public CachedFieldInfo(FieldInfo field, FieldCategory category, Type elementType = null)
        {
            Field = field;
            Category = category;
            ElementType = elementType;
            IsPublic = field.IsPublic;
        }

        public object GetValue(object obj) => Field.GetValue(obj);
        public void SetValue(object obj, object value) => Field.SetValue(obj, value);
    }

    //! Complete reflection data for a component type, cached for reuse
    public class ComponentReflectionData
    {
        public Type ComponentType { get; }
        public string FullName => ComponentType.FullName;
        public string Name => ComponentType.Name;
        public bool IsRealvirtualComponent { get; }

        public CachedFieldInfo[] SerializableFields { get; }

        // Pre-categorized fields for fast access during serialization
        public CachedFieldInfo[] MaterialFields { get; }
        public CachedFieldInfo[] GameObjectFields { get; }
        public CachedFieldInfo[] ComponentFields { get; }
        public CachedFieldInfo[] ScriptableObjectFields { get; }
        public CachedFieldInfo[] PrimitiveFields { get; }
        public CachedFieldInfo[] UnityPrimitiveFields { get; }
        public CachedFieldInfo[] ArrayFields { get; }

        public ComponentReflectionData(Type type, CachedFieldInfo[] fields)
        {
            ComponentType = type;
            IsRealvirtualComponent = type.Namespace?.StartsWith("realvirtual") == true ||
                                    type.Namespace?.StartsWith("game4automation") == true;
            SerializableFields = fields;

            // Pre-categorize fields for optimized access
            var materialFields = new List<CachedFieldInfo>();
            var gameObjectFields = new List<CachedFieldInfo>();
            var componentFields = new List<CachedFieldInfo>();
            var scriptableObjectFields = new List<CachedFieldInfo>();
            var primitiveFields = new List<CachedFieldInfo>();
            var unityPrimitiveFields = new List<CachedFieldInfo>();
            var arrayFields = new List<CachedFieldInfo>();

            foreach (var field in fields)
            {
                switch (field.Category)
                {
                    case FieldCategory.Material:
                        materialFields.Add(field);
                        break;
                    case FieldCategory.GameObject:
                        gameObjectFields.Add(field);
                        break;
                    case FieldCategory.Component:
                        componentFields.Add(field);
                        break;
                    case FieldCategory.ScriptableObject:
                        scriptableObjectFields.Add(field);
                        break;
                    case FieldCategory.Primitive:
                        primitiveFields.Add(field);
                        break;
                    case FieldCategory.UnityPrimitive:
                        unityPrimitiveFields.Add(field);
                        break;
                    case FieldCategory.PrimitiveArray:
                    case FieldCategory.UnityPrimitiveArray:
                    case FieldCategory.ObjectArray:
                    case FieldCategory.ObjectList:
                        arrayFields.Add(field);
                        break;
                }
            }

            MaterialFields = materialFields.ToArray();
            GameObjectFields = gameObjectFields.ToArray();
            ComponentFields = componentFields.ToArray();
            ScriptableObjectFields = scriptableObjectFields.ToArray();
            PrimitiveFields = primitiveFields.ToArray();
            UnityPrimitiveFields = unityPrimitiveFields.ToArray();
            ArrayFields = arrayFields.ToArray();
        }
    }

    //! Thread-safe cache for component reflection data.
    //! Computes and stores reflection information once per type for reuse across MCP operations.
    public static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, ComponentReflectionData> _cache = new ConcurrentDictionary<Type, ComponentReflectionData>();

        // Binding flags for field discovery
        private const BindingFlags FIELD_FLAGS =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.FlattenHierarchy;

#if UNITY_EDITOR
        //! Clears cache on script recompilation to avoid stale Type objects after domain reload
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            _cache.Clear();
        }
#endif

        //! Gets cached reflection data for a type, computing if not already cached
        public static ComponentReflectionData GetReflectionData(Type type)
        {
            return _cache.GetOrAdd(type, ComputeReflectionData);
        }

        //! Clears the cache (useful for testing)
        public static void ClearCache()
        {
            _cache.Clear();
        }

        //! Gets count of cached types (for debugging)
        public static int CachedTypeCount => _cache.Count;

        private static ComponentReflectionData ComputeReflectionData(Type type)
        {
            var fields = ComputeSerializableFields(type);
            return new ComponentReflectionData(type, fields);
        }

        private static CachedFieldInfo[] ComputeSerializableFields(Type type)
        {
            var result = new List<CachedFieldInfo>();

            foreach (var field in type.GetFields(FIELD_FLAGS))
            {
                // Skip compiler-generated fields
                if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                // Skip static and const fields
                if (field.IsStatic || field.IsLiteral)
                    continue;

                // Skip [NonSerialized] fields
                if (field.IsDefined(typeof(NonSerializedAttribute), false))
                    continue;

                // Check if field should be serialized (matching Unity Inspector rules)
                bool isSerializable = IsFieldSerializable(field);
                if (!isSerializable)
                    continue;

                // Determine category
                var category = CategorizeType(field.FieldType, out Type elementType);
                if (category == FieldCategory.Unsupported)
                    continue;

                result.Add(new CachedFieldInfo(field, category, elementType));
            }

            return result.ToArray();
        }

        private static bool IsFieldSerializable(FieldInfo field)
        {
            // Skip [HideInInspector] - hidden from Inspector = hidden from MCP
            if (field.IsDefined(typeof(HideInInspector), true))
                return false;

            // Public fields are serializable by default
            if (field.IsPublic)
                return true;

            // Private/protected fields need [SerializeField] attribute
            return field.IsDefined(typeof(SerializeField), true);
        }

        private static FieldCategory CategorizeType(Type type, out Type elementType)
        {
            elementType = null;

            // Primitives and strings
            if (type.IsPrimitive || type == typeof(string))
                return FieldCategory.Primitive;

            // Enums
            if (type.IsEnum)
                return FieldCategory.Primitive;

            // LayerMask is an int wrapper - treat as primitive
            if (type == typeof(LayerMask))
                return FieldCategory.Primitive;

            // AnimationCurve and Gradient have complex internal structure - unsupported
            if (type == typeof(AnimationCurve) || type == typeof(Gradient))
                return FieldCategory.Unsupported;

            // Unity primitive types
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Color32) ||
                type == typeof(Rect) || type == typeof(Bounds))
                return FieldCategory.UnityPrimitive;

            // Material
            if (type == typeof(Material))
                return FieldCategory.Material;

            // GameObject
            if (type == typeof(GameObject))
                return FieldCategory.GameObject;

            // ScriptableObject (check before Component since SO doesn't inherit from Component)
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return FieldCategory.ScriptableObject;

            // Component (includes MonoBehaviour)
            if (typeof(Component).IsAssignableFrom(type))
                return FieldCategory.Component;

            // Arrays
            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return CategorizeArrayType(elementType);
            }

            // Generic Lists
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return CategorizeListType(elementType);
            }

            // [Serializable] classes/structs
            if (type.IsClass || type.IsValueType)
            {
                if (type.IsDefined(typeof(SerializableAttribute), false))
                    return FieldCategory.Serializable;
            }

            // UnityEvents, delegates, and unknown types
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(type))
                return FieldCategory.Unsupported;

            if (typeof(Delegate).IsAssignableFrom(type))
                return FieldCategory.Unsupported;

            return FieldCategory.Unsupported;
        }

        private static FieldCategory CategorizeArrayType(Type elementType)
        {
            // Primitive arrays
            if (elementType.IsPrimitive || elementType == typeof(string) || elementType.IsEnum)
                return FieldCategory.PrimitiveArray;

            // Unity primitive arrays
            if (elementType == typeof(Vector2) || elementType == typeof(Vector3) || elementType == typeof(Vector4) ||
                elementType == typeof(Quaternion) || elementType == typeof(Color) || elementType == typeof(Color32) ||
                elementType == typeof(Rect) || elementType == typeof(Bounds))
                return FieldCategory.UnityPrimitiveArray;

            // Object arrays (GameObject, Component, Material, etc.)
            if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                return FieldCategory.ObjectArray;

            return FieldCategory.Unsupported;
        }

        private static FieldCategory CategorizeListType(Type elementType)
        {
            // List<primitive>
            if (elementType.IsPrimitive || elementType == typeof(string) || elementType.IsEnum)
                return FieldCategory.PrimitiveArray; // Treat same as arrays

            // List<Unity primitive>
            if (elementType == typeof(Vector2) || elementType == typeof(Vector3) || elementType == typeof(Vector4) ||
                elementType == typeof(Quaternion) || elementType == typeof(Color) || elementType == typeof(Color32))
                return FieldCategory.UnityPrimitiveArray;

            // List<UnityEngine.Object>
            if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                return FieldCategory.ObjectList;

            return FieldCategory.Unsupported;
        }
    }
}
