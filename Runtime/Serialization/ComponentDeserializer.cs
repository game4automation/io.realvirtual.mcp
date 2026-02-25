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
    //! Deserializes JSON to MonoBehaviour components for MCP tools.
    //! Performs partial updates - only fields present in JSON are modified.
    public static class ComponentDeserializer
    {
        // Shared JSON serializer settings for [Serializable] classes
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };

        //! Applies JSON to a MonoBehaviour (partial update - only fields present in JSON).
        //! Registers Undo and marks the object as dirty so changes persist on scene save.
        //! Returns a list of errors for fields that could not be set (empty if all succeeded).
        public static List<string> Deserialize(MonoBehaviour component, JObject data)
        {
            var errors = new List<string>();

            if (component == null)
            {
                errors.Add("Component is null");
                return errors;
            }

            if (data == null || !data.HasValues)
            {
                errors.Add("Data is null or empty");
                return errors;
            }

#if UNITY_EDITOR
            // Record undo before modifying so changes can be reverted and are saved with the scene
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(component, $"MCP Set {component.GetType().Name}");
#endif

            var type = component.GetType();
            var reflData = ReflectionCache.GetReflectionData(type);
            bool anyFieldSet = false;

            // Deserialize each field present in JSON
            foreach (var field in reflData.SerializableFields)
            {
                // Skip fields not present in JSON (partial update)
                if (!data.ContainsKey(field.Name))
                    continue;

                try
                {
                    var token = data[field.Name];
                    if (token == null || token.Type == JTokenType.Null)
                        continue;

                    var value = DeserializeValue(token, field.Category, field.FieldType, field.ElementType);
                    if (value != null)
                    {
                        field.SetValue(component, value);
                        anyFieldSet = true;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Field '{field.Name}': {ex.Message}");
                    McpLog.Warn($"Deserializer: Failed to deserialize field '{field.Name}' on {type.Name}: {ex.Message}");
                }
            }

#if UNITY_EDITOR
            // Mark dirty so the scene is flagged as unsaved and changes persist
            if (anyFieldSet && !Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(component);
#endif

            return errors;
        }

        //! Applies JSON to a named component type on a GameObject.
        //! Supports indexed component names like "Drive_1" to target specific instances
        //! when multiple components of the same type exist on a GameObject.
        //! Returns a list of errors (empty if all succeeded).
        public static List<string> Deserialize(GameObject go, string componentTypeName, JObject data)
        {
            if (go == null)
                return new List<string> { "GameObject is null" };

            // Parse index suffix (e.g., "Drive_1" -> type="Drive", index=1)
            string actualTypeName = componentTypeName;
            int componentIndex = 0;
            int underscoreIndex = componentTypeName.LastIndexOf('_');
            if (underscoreIndex > 0 && int.TryParse(componentTypeName.Substring(underscoreIndex + 1), out int parsedIndex))
            {
                actualTypeName = componentTypeName.Substring(0, underscoreIndex);
                componentIndex = parsedIndex;
            }

            var type = realvirtual.MCP.Serialization.McpTypeResolver.Resolve(actualTypeName);
            if (type == null)
                return new List<string> { $"Type '{actualTypeName}' not found" };

            // Get the component at the specified index
            var comp = GetComponentAtIndex(go, type, componentIndex);
            if (comp == null)
                return new List<string> { $"Component '{componentTypeName}' (index {componentIndex}) not found on '{go.name}'" };

            return Deserialize(comp, data);
        }

        //! Gets a component at a specific index when multiple of the same type exist.
        //! Index 0 returns the first component, 1 returns the second, etc.
        private static MonoBehaviour GetComponentAtIndex(GameObject go, Type type, int index)
        {
            var components = go.GetComponents(type);
            if (components == null || index >= components.Length)
                return null;
            return components[index] as MonoBehaviour;
        }

        //! Applies only specific fields by name
        public static void DeserializeFields(MonoBehaviour component, JObject data, string[] fieldNames)
        {
            if (component == null)
            {
                McpLog.Warn("Deserializer: Component is null");
                return;
            }

            if (fieldNames == null || fieldNames.Length == 0)
            {
                McpLog.Warn("Deserializer: No field names specified");
                return;
            }

            var type = component.GetType();
            var reflData = ReflectionCache.GetReflectionData(type);
            var fieldNameSet = new HashSet<string>(fieldNames);

            // Deserialize only requested fields
            foreach (var field in reflData.SerializableFields)
            {
                if (!fieldNameSet.Contains(field.Name))
                    continue;

                if (!data.ContainsKey(field.Name))
                    continue;

                try
                {
                    var token = data[field.Name];
                    if (token == null || token.Type == JTokenType.Null)
                        continue;

                    var value = DeserializeValue(token, field.Category, field.FieldType, field.ElementType);
                    if (value != null)
                    {
                        field.SetValue(component, value);
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Deserializer: Failed to deserialize field '{field.Name}': {ex.Message}");
                }
            }
        }

        //! Applies multi-component JSON to all matching components.
        //! Returns a list of errors (empty if all succeeded).
        public static List<string> DeserializeAll(GameObject go, JObject data)
        {
            var errors = new List<string>();

            if (go == null)
            {
                errors.Add("GameObject is null");
                return errors;
            }

            if (data == null || !data.HasValues)
            {
                errors.Add("Data is null or empty");
                return errors;
            }

            // Iterate over each component type in JSON
            foreach (var property in data.Properties())
            {
                var componentTypeName = property.Name;
                var componentData = property.Value as JObject;

                if (componentData == null)
                    continue;

                // Handle indexed component names (e.g., "Drive_1")
                string actualTypeName = componentTypeName;
                int underscoreIndex = componentTypeName.LastIndexOf('_');
                if (underscoreIndex > 0 && int.TryParse(componentTypeName.Substring(underscoreIndex + 1), out _))
                {
                    actualTypeName = componentTypeName.Substring(0, underscoreIndex);
                }

                try
                {
                    var componentErrors = Deserialize(go, actualTypeName, componentData);
                    foreach (var err in componentErrors)
                        errors.Add($"{componentTypeName}: {err}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{componentTypeName}: {ex.Message}");
                }
            }

            return errors;
        }

        //! Deserializes a value based on its category
        private static object DeserializeValue(JToken token, FieldCategory category, Type fieldType, Type elementType)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            switch (category)
            {
                case FieldCategory.Primitive:
                    return DeserializePrimitive(token, fieldType);

                case FieldCategory.UnityPrimitive:
                    return DeserializeUnityPrimitive(token, fieldType);

                case FieldCategory.PrimitiveArray:
                case FieldCategory.UnityPrimitiveArray:
                    return DeserializeArray(token, category, elementType);

                case FieldCategory.ObjectArray:
                    return DeserializeObjectArray(token, elementType);

                case FieldCategory.ObjectList:
                    return DeserializeObjectList(token, elementType);

                case FieldCategory.GameObject:
                    return DeserializeGameObjectReference(token);

                case FieldCategory.Component:
                    return DeserializeComponentReference(token, fieldType);

                case FieldCategory.Material:
                    return DeserializeMaterialReference(token);

                case FieldCategory.ScriptableObject:
                    // ScriptableObject references cannot be reliably resolved by name alone
                    McpLog.Warn("Deserializer: Skipping ScriptableObject reference - not supported");
                    return null;

                case FieldCategory.Serializable:
                    return DeserializeSerializable(token, fieldType);

                default:
                    return null;
            }
        }

        private static object DeserializePrimitive(JToken token, Type fieldType)
        {
            // Enum handling - throw on invalid values so caller gets a clear error
            if (fieldType.IsEnum)
            {
                string enumString = token.ToString();
                try
                {
                    return Enum.Parse(fieldType, enumString, ignoreCase: true);
                }
                catch
                {
                    var validValues = string.Join(", ", Enum.GetNames(fieldType));
                    throw new ArgumentException($"Invalid enum value '{enumString}' for {fieldType.Name}. Valid values: [{validValues}]");
                }
            }

            // Primitive types
            try
            {
                return token.ToObject(fieldType);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Cannot convert '{token}' to {fieldType.Name}: {ex.Message}");
            }
        }

        private static object DeserializeUnityPrimitive(JToken token, Type fieldType)
        {
            if (token.Type != JTokenType.Object)
                return null;

            var obj = (JObject)token;

            try
            {
                if (fieldType == typeof(Vector2))
                {
                    return new Vector2(
                        obj["x"]?.Value<float>() ?? 0f,
                        obj["y"]?.Value<float>() ?? 0f
                    );
                }
                else if (fieldType == typeof(Vector3))
                {
                    return new Vector3(
                        obj["x"]?.Value<float>() ?? 0f,
                        obj["y"]?.Value<float>() ?? 0f,
                        obj["z"]?.Value<float>() ?? 0f
                    );
                }
                else if (fieldType == typeof(Vector4))
                {
                    return new Vector4(
                        obj["x"]?.Value<float>() ?? 0f,
                        obj["y"]?.Value<float>() ?? 0f,
                        obj["z"]?.Value<float>() ?? 0f,
                        obj["w"]?.Value<float>() ?? 0f
                    );
                }
                else if (fieldType == typeof(Quaternion))
                {
                    return new Quaternion(
                        obj["x"]?.Value<float>() ?? 0f,
                        obj["y"]?.Value<float>() ?? 0f,
                        obj["z"]?.Value<float>() ?? 0f,
                        obj["w"]?.Value<float>() ?? 1f
                    );
                }
                else if (fieldType == typeof(Color))
                {
                    return new Color(
                        obj["r"]?.Value<float>() ?? 0f,
                        obj["g"]?.Value<float>() ?? 0f,
                        obj["b"]?.Value<float>() ?? 0f,
                        obj["a"]?.Value<float>() ?? 1f
                    );
                }
                else if (fieldType == typeof(Color32))
                {
                    return new Color32(
                        obj["r"]?.Value<byte>() ?? 0,
                        obj["g"]?.Value<byte>() ?? 0,
                        obj["b"]?.Value<byte>() ?? 0,
                        obj["a"]?.Value<byte>() ?? 255
                    );
                }
                else if (fieldType == typeof(Rect))
                {
                    return new Rect(
                        obj["x"]?.Value<float>() ?? 0f,
                        obj["y"]?.Value<float>() ?? 0f,
                        obj["width"]?.Value<float>() ?? 0f,
                        obj["height"]?.Value<float>() ?? 0f
                    );
                }
                else if (fieldType == typeof(Bounds))
                {
                    var centerObj = obj["center"] as JObject;
                    var sizeObj = obj["size"] as JObject;

                    Vector3 center = Vector3.zero;
                    Vector3 size = Vector3.zero;

                    if (centerObj != null)
                    {
                        center = new Vector3(
                            centerObj["x"]?.Value<float>() ?? 0f,
                            centerObj["y"]?.Value<float>() ?? 0f,
                            centerObj["z"]?.Value<float>() ?? 0f
                        );
                    }

                    if (sizeObj != null)
                    {
                        size = new Vector3(
                            sizeObj["x"]?.Value<float>() ?? 0f,
                            sizeObj["y"]?.Value<float>() ?? 0f,
                            sizeObj["z"]?.Value<float>() ?? 0f
                        );
                    }

                    return new Bounds(center, size);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Deserializer: Failed to deserialize Unity primitive {fieldType.Name}: {ex.Message}");
            }

            return null;
        }

        private static object DeserializeArray(JToken token, FieldCategory category, Type elementType)
        {
            if (token.Type != JTokenType.Array)
                return null;

            var jArray = (JArray)token;

            try
            {
                if (category == FieldCategory.PrimitiveArray)
                {
                    var list = new ArrayList();
                    foreach (var item in jArray)
                    {
                        if (item.Type == JTokenType.Null)
                        {
                            list.Add(null);
                        }
                        else
                        {
                            var value = DeserializePrimitive(item, elementType);
                            list.Add(value);
                        }
                    }

                    // Convert to typed array
                    var array = Array.CreateInstance(elementType, list.Count);
                    list.CopyTo(array);
                    return array;
                }
                else if (category == FieldCategory.UnityPrimitiveArray)
                {
                    var list = new ArrayList();
                    foreach (var item in jArray)
                    {
                        if (item.Type == JTokenType.Null)
                        {
                            list.Add(null);
                        }
                        else
                        {
                            var value = DeserializeUnityPrimitive(item, elementType);
                            list.Add(value);
                        }
                    }

                    // Convert to typed array
                    var array = Array.CreateInstance(elementType, list.Count);
                    list.CopyTo(array);
                    return array;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Deserializer: Failed to deserialize array: {ex.Message}");
            }

            return null;
        }

        private static object DeserializeSerializable(JToken token, Type fieldType)
        {
            if (token.Type != JTokenType.Object)
                return null;

            try
            {
                var serializer = JsonSerializer.Create(_jsonSettings);
                return token.ToObject(fieldType, serializer);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Deserializer: Failed to deserialize [Serializable] object {fieldType.Name}: {ex.Message}");
                return null;
            }
        }

        //! Resolves a GameObject reference from a path string
        private static object DeserializeGameObjectReference(JToken token)
        {
            string path = token.Type == JTokenType.String ? token.Value<string>() : null;
            if (string.IsNullOrEmpty(path))
                return null;

            var go = FindGameObjectByPath(path);
            if (go == null)
                McpLog.Warn($"Deserializer: GameObject not found: '{path}'");
            return go;
        }

        //! Resolves a Component reference from path string or {path, type, index} object.
        //! Supports an optional "index" field to select a specific component when multiple
        //! of the same type exist on a GameObject (e.g., {"path": "MovingCube", "type": "Drive", "index": 1}).
        private static object DeserializeComponentReference(JToken token, Type fieldType)
        {
            string path;
            string typeName = null;
            int componentIndex = -1; // -1 means "use first match" (default behavior)

            if (token.Type == JTokenType.String)
            {
                // Simple path string - find GameObject and get component of fieldType
                path = token.Value<string>();
            }
            else if (token.Type == JTokenType.Object)
            {
                // {path: "...", type: "...", index: N} format
                var obj = (JObject)token;
                path = obj["path"]?.Value<string>();
                typeName = obj["type"]?.Value<string>();
                if (obj["index"] != null)
                    componentIndex = obj["index"].Value<int>();
            }
            else
            {
                return null;
            }

            if (string.IsNullOrEmpty(path))
                return null;

            var go = FindGameObjectByPath(path);
            if (go == null)
            {
                McpLog.Warn($"Deserializer: GameObject not found for component reference: '{path}'");
                return null;
            }

            // If index is specified, use indexed lookup
            if (componentIndex >= 0)
            {
                Type lookupType = fieldType;
                if (!string.IsNullOrEmpty(typeName))
                {
                    var resolved = McpTypeResolver.Resolve(typeName);
                    if (resolved != null && fieldType.IsAssignableFrom(resolved))
                        lookupType = resolved;
                }

                var components = go.GetComponents(lookupType);
                if (components != null && componentIndex < components.Length)
                    return components[componentIndex];

                McpLog.Warn($"Deserializer: Component '{lookupType.Name}' index {componentIndex} not found on '{path}' (has {components?.Length ?? 0})");
                return null;
            }

            // Default behavior: get first matching component
            var comp = go.GetComponent(fieldType);
            if (comp != null)
                return comp;

            // If typeName is specified and differs from fieldType, try resolving it
            if (!string.IsNullOrEmpty(typeName))
            {
                var resolvedType = McpTypeResolver.Resolve(typeName);
                if (resolvedType != null && fieldType.IsAssignableFrom(resolvedType))
                {
                    comp = go.GetComponent(resolvedType);
                    if (comp != null)
                        return comp;
                }
            }

            McpLog.Warn($"Deserializer: Component '{fieldType.Name}' not found on '{path}'");
            return null;
        }

        //! Resolves a Material reference by name
        private static object DeserializeMaterialReference(JToken token)
        {
            string materialName = token.Type == JTokenType.String ? token.Value<string>() : null;
            if (string.IsNullOrEmpty(materialName))
                return null;

#if UNITY_EDITOR
            // Search project materials via AssetDatabase
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:Material {materialName}");
            foreach (var guid in guids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (mat != null && mat.name == materialName)
                    return mat;
            }
#endif
            // Runtime fallback - search loaded materials
            var allMaterials = UnityEngine.Resources.FindObjectsOfTypeAll<Material>();
            foreach (var mat in allMaterials)
            {
                if (mat.name == materialName)
                    return mat;
            }

            McpLog.Warn($"Deserializer: Material not found: '{materialName}'");
            return null;
        }

        //! Deserializes an array of object references (GameObject[], Component[], etc.)
        private static object DeserializeObjectArray(JToken token, Type elementType)
        {
            if (token.Type != JTokenType.Array || elementType == null)
                return null;

            var jArray = (JArray)token;
            var array = Array.CreateInstance(elementType, jArray.Count);

            for (int i = 0; i < jArray.Count; i++)
            {
                var item = jArray[i];
                if (item == null || item.Type == JTokenType.Null)
                    continue;

                var resolved = ResolveObjectReference(item, elementType);
                if (resolved != null)
                    array.SetValue(resolved, i);
            }

            return array;
        }

        //! Deserializes a List of object references (List<GameObject>, List<Component>, etc.)
        private static object DeserializeObjectList(JToken token, Type elementType)
        {
            if (token.Type != JTokenType.Array || elementType == null)
                return null;

            var jArray = (JArray)token;
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType);

            foreach (var item in jArray)
            {
                if (item == null || item.Type == JTokenType.Null)
                {
                    list.Add(null);
                    continue;
                }

                var resolved = ResolveObjectReference(item, elementType);
                list.Add(resolved);
            }

            return list;
        }

        //! Resolves a single object reference (used by array/list deserializers)
        private static object ResolveObjectReference(JToken item, Type elementType)
        {
            if (typeof(GameObject).IsAssignableFrom(elementType))
            {
                return DeserializeGameObjectReference(item);
            }
            else if (typeof(Component).IsAssignableFrom(elementType))
            {
                return DeserializeComponentReference(item, elementType);
            }
            else if (typeof(Material).IsAssignableFrom(elementType))
            {
                return DeserializeMaterialReference(item);
            }

            return null;
        }

        //! Finds a GameObject by hierarchy path or name
        private static GameObject FindGameObjectByPath(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return null;

            // Try direct path first (active objects)
            var go = GameObject.Find(nameOrPath);
            if (go != null)
                return go;

            // Try just the last segment as name (handles partial paths)
            var lastSlash = nameOrPath.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                var name = nameOrPath.Substring(lastSlash + 1);
                go = GameObject.Find(name);
                if (go != null)
                    return go;
            }

            // Fallback: search ALL objects including inactive
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            // First try exact path match
            foreach (var obj in allObjects)
            {
                if (GetGameObjectPath(obj) == nameOrPath)
                    return obj;
            }

            // Then try name match
            var searchName = lastSlash >= 0 ? nameOrPath.Substring(lastSlash + 1) : nameOrPath;
            foreach (var obj in allObjects)
            {
                if (obj.name == searchName)
                    return obj;
            }

            return null;
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
