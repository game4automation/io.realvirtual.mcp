// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace realvirtual.MCP.Tools
{
    //! MCP tools for querying the Unity scene hierarchy and finding objects.
    //!
    //! Provides commands to get scene information, find GameObjects, and query transforms.
    //! These tools enable AI agents to explore and understand the scene structure.
    public static class SceneTools
    {
        //! Gets the scene hierarchy structure with configurable depth and optional root
        [McpTool("Get scene hierarchy")]
        public static string SceneHierarchy(
            [McpParam("Max depth to traverse (default 3)")] int depth = 3,
            [McpParam("Root GameObject name/path to start from (optional, empty=full scene)")] string root = "")
        {
            var scene = SceneManager.GetActiveScene();

            var arr = new JArray();

            if (!string.IsNullOrEmpty(root))
            {
                var rootGo = ToolHelpers.FindGameObject(root);
                if (rootGo == null)
                    return ToolHelpers.Error($"Root GameObject '{root}' not found");
                arr.Add(BuildGameObjectInfo(rootGo, 0, maxDepth: depth));
            }
            else
            {
                var rootObjects = scene.GetRootGameObjects();
                foreach (var obj in rootObjects)
                {
                    arr.Add(BuildGameObjectInfo(obj, 0, maxDepth: depth));
                }
            }

            return new JObject
            {
                ["scene"] = scene.name,
                ["rootObjects"] = arr,
                ["depth"] = depth,
                ["count"] = arr.Count
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Finds GameObjects by name
        [McpTool("Find GameObjects by name")]
        public static string SceneFind(
            [McpParam("Search term")] string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return ToolHelpers.Error("Search term cannot be empty");

            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var arr = new JArray();

            foreach (var obj in allObjects)
            {
                if (obj.name.IndexOf(searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    arr.Add(new JObject
                    {
                        ["name"] = obj.name,
                        ["path"] = ToolHelpers.GetGameObjectPath(obj),
                        ["active"] = obj.activeInHierarchy
                    });
                }
            }

            return new JObject
            {
                ["objects"] = arr,
                ["count"] = arr.Count,
                ["searchTerm"] = searchTerm
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets transform information for a GameObject
        [McpTool("Get GameObject transform")]
        public static string SceneGetTransform(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var t = go.transform;

            return new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["position"] = ToolHelpers.Vec3ToJson(t.position),
                ["rotation"] = ToolHelpers.Vec3ToJson(t.rotation.eulerAngles),
                ["localPosition"] = ToolHelpers.Vec3ToJson(t.localPosition),
                ["localRotation"] = ToolHelpers.Vec3ToJson(t.localRotation.eulerAngles),
                ["scale"] = ToolHelpers.Vec3ToJson(t.localScale)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets components attached to a GameObject
        [McpTool("Get GameObject components")]
        public static string SceneGetComponents(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            var components = go.GetComponents<Component>();
            var arr = new JArray();
            foreach (var comp in components)
            {
                if (comp != null)
                    arr.Add(comp.GetType().Name);
            }

            return new JObject
            {
                ["name"] = go.name,
                ["path"] = ToolHelpers.GetGameObjectPath(go),
                ["components"] = arr,
                ["count"] = components.Length
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Gets active scene information
        [McpTool("Get active scene info")]
        public static string SceneGetInfo()
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            return new JObject
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isLoaded"] = scene.isLoaded,
                ["buildIndex"] = scene.buildIndex,
                ["rootCount"] = rootObjects.Length
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

#if UNITY_EDITOR
        //! Creates a new empty scene with optional save path
        [McpTool("Create new empty scene", "scene_new")]
        public static string SceneNew(
            [McpParam("Save path (e.g. Assets/Scenes/MyScene.unity)")] string path = "")
        {
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var result = new JObject
            {
                ["status"] = "ok",
                ["scene"] = newScene.name
            };

            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith(".unity"))
                    path += ".unity";

                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                EditorSceneManager.SaveScene(newScene, path);
                result["path"] = path;
                result["message"] = $"Scene created and saved at {path}";
            }
            else
            {
                result["message"] = "New unsaved scene created";
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }
#endif

#if UNITY_EDITOR
        //! Lists all scene files in the project
        [McpTool("List all scene files in project", "scene_list")]
        public static string SceneList(
            [McpParam("Optional folder filter (e.g. Assets/Scenes)")] string folder = "")
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Scene",
                string.IsNullOrEmpty(folder) ? null : new[] { folder });

            var arr = new JArray();
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".unity"))
                {
                    arr.Add(new JObject
                    {
                        ["path"] = path,
                        ["name"] = System.IO.Path.GetFileNameWithoutExtension(path)
                    });
                }
            }

            return new JObject
            {
                ["scenes"] = arr,
                ["count"] = arr.Count
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
#endif

        //! Builds a JObject for a GameObject recursively
        private static JObject BuildGameObjectInfo(GameObject obj, int depth, int maxDepth)
        {
            var jobj = new JObject
            {
                ["name"] = obj.name,
                ["active"] = obj.activeInHierarchy
            };

            if (depth < maxDepth && obj.transform.childCount > 0)
            {
                var children = new JArray();
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    children.Add(BuildGameObjectInfo(obj.transform.GetChild(i).gameObject, depth + 1, maxDepth));
                }
                jobj["children"] = children;
            }
            else if (obj.transform.childCount > 0)
            {
                jobj["childCount"] = obj.transform.childCount;
            }

            return jobj;
        }
    }
}
