// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace realvirtual.MCP.Tools
{
    //! MCP tools for Unity Editor scene operations.
    //!
    //! Provides commands to save scenes, undo operations, select and focus GameObjects
    //! in the Unity Editor. These tools are Editor-only (excluded from builds by assembly definition).
    public static class EditorSceneTools
    {
        //! Saves the current scene. If path is provided, saves as a new scene file (Save As).
        [McpTool("Save current scene")]
        public static string EditorSaveScene(
            [McpParam("Optional: asset path to save as (e.g. 'Assets/Scenes/MyScene.unity'). If empty, saves to current path.")] string path = "")
        {
            var scene = SceneManager.GetActiveScene();

            bool saved;
            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith(".unity"))
                    path += ".unity";

                // Ensure directory exists
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                saved = EditorSceneManager.SaveScene(scene, path);
            }
            else
            {
                saved = EditorSceneManager.SaveScene(scene);
            }

            if (saved)
            {
                scene = SceneManager.GetActiveScene();
                return new JObject
                {
                    ["status"] = "ok",
                    ["message"] = string.IsNullOrEmpty(path) ? "Scene saved" : $"Scene saved as '{path}'",
                    ["scenePath"] = scene.path
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            return new JObject
            {
                ["status"] = "error",
                ["error"] = "Failed to save scene"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Performs an undo operation
        [McpTool("Undo last operation")]
        public static string EditorUndo()
        {
            Undo.PerformUndo();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = "Undo performed"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Selects a GameObject in the Editor hierarchy
        [McpTool("Select GameObject in hierarchy")]
        public static string EditorSelect(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            Selection.activeGameObject = go;

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Selected '{go.name}'",
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Returns the GameObjects currently selected in the Editor (Hierarchy/Scene View) - full hierarchy
        //! paths plus world position, so an MCP session can pick up objects the user just clicked
        //! ("die aktuell selektierten"). activeGameObject is reported separately (it is the one whose
        //! Inspector is shown and the last one clicked).
        [McpTool("Get the GameObjects currently selected in the Editor", "editor_get_selection")]
        public static string EditorGetSelection()
        {
            var selected = new JArray();
            foreach (var go in Selection.gameObjects)
            {
                selected.Add(new JObject
                {
                    ["name"] = go.name,
                    ["path"] = ToolHelpers.GetGameObjectPath(go),
                    ["position"] = new JObject
                    {
                        ["x"] = go.transform.position.x,
                        ["y"] = go.transform.position.y,
                        ["z"] = go.transform.position.z
                    }
                });
            }

            return new JObject
            {
                ["status"] = "ok",
                ["count"] = selected.Count,
                ["activeGameObject"] = Selection.activeGameObject != null ? ToolHelpers.GetGameObjectPath(Selection.activeGameObject) : null,
                ["selected"] = selected
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Opens a scene by asset path
        [McpTool("Open scene by path", "editor_open_scene")]
        public static string EditorOpenScene(
            [McpParam("Scene asset path (e.g. Assets/Scenes/MyScene.unity)")] string path,
            [McpParam("Save current scene before opening")] bool save = true)
        {
            if (string.IsNullOrEmpty(path))
                return ToolHelpers.Error("Scene path cannot be empty");

            if (!path.EndsWith(".unity"))
                path += ".unity";

            if (!System.IO.File.Exists(path))
                return ToolHelpers.Error($"Scene not found at '{path}'");

            if (save)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Opened scene '{scene.name}'",
                ["scenePath"] = scene.path,
                ["rootCount"] = scene.GetRootGameObjects().Length
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Focuses the Scene view camera on a GameObject
        [McpTool("Focus camera on GameObject")]
        public static string EditorFocus(
            [McpParam("GameObject name or path")] string name)
        {
            var go = ToolHelpers.FindGameObject(name);
            if (go == null)
                return ToolHelpers.Error($"GameObject '{name}' not found");

            Selection.activeGameObject = go;
            SceneView.FrameLastActiveSceneView();

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Focused on '{go.name}'",
                ["path"] = ToolHelpers.GetGameObjectPath(go)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
        //! Executes a Unity Editor menu item by path
        [McpTool("Execute Unity Editor menu item by path", "editor_execute_menu")]
        public static string EditorExecuteMenu(
            [McpParam("Menu item path (e.g. 'Window/General/Console')")] string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
                return ToolHelpers.Error("Menu path cannot be empty");

            bool executed = EditorApplication.ExecuteMenuItem(menuPath);
            if (!executed)
                return ToolHelpers.Error($"Menu item '{menuPath}' not found or could not be executed");

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Executed menu: {menuPath}"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Invokes a static method by fully qualified class and method name
        [McpTool("Invoke a static method by class and method name", "editor_invoke_method")]
        public static string EditorInvokeMethod(
            [McpParam("Fully qualified class name (e.g. 'realvirtual.ProjectBuilder')")] string className,
            [McpParam("Static method name (e.g. 'RunTests')")] string methodName)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName))
                return ToolHelpers.Error("Class name and method name are required");

            // Find the type across all loaded assemblies
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(className);
                if (type != null)
                    break;
            }

            if (type == null)
                return ToolHelpers.Error($"Type '{className}' not found");

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                return ToolHelpers.Error($"Static method '{methodName}' not found on '{className}'");

            try
            {
                var result = method.Invoke(null, null);
                var response = new JObject
                {
                    ["status"] = "ok",
                    ["message"] = $"Invoked {className}.{methodName}()"
                };

                if (result != null)
                    response["result"] = result.ToString();

                return response.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return ToolHelpers.Error($"Method execution failed: {inner.Message}");
            }
        }

        //! Gets the current scene camera position, rotation, pivot and size
        [McpTool("Get scene camera view", "editor_get_camera")]
        public static string EditorGetCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                return ToolHelpers.Error("No active SceneView found");

            var pos = sv.camera.transform.position;
            var rot = sv.camera.transform.rotation.eulerAngles;
            var pivot = sv.pivot;
            var pivotRot = sv.rotation.eulerAngles;

            return new JObject
            {
                ["status"] = "ok",
                ["position"] = new JObject { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z },
                ["rotation"] = new JObject { ["x"] = rot.x, ["y"] = rot.y, ["z"] = rot.z },
                ["pivot"] = new JObject { ["x"] = pivot.x, ["y"] = pivot.y, ["z"] = pivot.z },
                ["pivotRotation"] = new JObject { ["x"] = pivotRot.x, ["y"] = pivotRot.y, ["z"] = pivotRot.z },
                ["size"] = sv.size,
                ["orthographic"] = sv.orthographic
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Sets the scene camera view by pivot, rotation and size
        [McpTool("Set scene camera view", "editor_set_camera")]
        public static string EditorSetCamera(
            [McpParam("Pivot X position")] float pivotX = float.NaN,
            [McpParam("Pivot Y position")] float pivotY = float.NaN,
            [McpParam("Pivot Z position")] float pivotZ = float.NaN,
            [McpParam("Rotation X (pitch in degrees)")] float rotationX = float.NaN,
            [McpParam("Rotation Y (yaw in degrees)")] float rotationY = float.NaN,
            [McpParam("Rotation Z (roll in degrees)")] float rotationZ = float.NaN,
            [McpParam("Camera zoom size")] float size = float.NaN,
            [McpParam("Orthographic view")] bool orthographic = false)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                return ToolHelpers.Error("No active SceneView found");

            if (!float.IsNaN(pivotX) || !float.IsNaN(pivotY) || !float.IsNaN(pivotZ))
            {
                var pivot = sv.pivot;
                if (!float.IsNaN(pivotX)) pivot.x = pivotX;
                if (!float.IsNaN(pivotY)) pivot.y = pivotY;
                if (!float.IsNaN(pivotZ)) pivot.z = pivotZ;
                sv.pivot = pivot;
            }

            if (!float.IsNaN(rotationX) || !float.IsNaN(rotationY) || !float.IsNaN(rotationZ))
            {
                var euler = sv.rotation.eulerAngles;
                if (!float.IsNaN(rotationX)) euler.x = rotationX;
                if (!float.IsNaN(rotationY)) euler.y = rotationY;
                if (!float.IsNaN(rotationZ)) euler.z = rotationZ;
                sv.rotation = Quaternion.Euler(euler);
            }

            if (!float.IsNaN(size))
                sv.size = size;

            sv.orthographic = orthographic;
            sv.Repaint();

            var newPivot = sv.pivot;
            var newRot = sv.rotation.eulerAngles;
            return new JObject
            {
                ["status"] = "ok",
                ["pivot"] = new JObject { ["x"] = newPivot.x, ["y"] = newPivot.y, ["z"] = newPivot.z },
                ["pivotRotation"] = new JObject { ["x"] = newRot.x, ["y"] = newRot.y, ["z"] = newRot.z },
                ["size"] = sv.size,
                ["orthographic"] = sv.orthographic
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Removes all MonoBehaviour components with missing script references from the loaded scene(s)
        //! or from a single prefab asset.
        //! Orphaned missing-script components (e.g. left over after a script or assembly was deleted) cause
        //! Unity to log a warning for each one on every scene load and play-mode entry, which can block the
        //! main thread for tens of seconds in large scenes. For scenes this strips them in-memory; the scene
        //! is only written to disk when save is true (default false, so the change can be reverted by reloading).
        //! When prefabPath is given, the prefab asset is loaded, stripped and (if anything was removed) saved.
        [McpTool("Remove MonoBehaviours with missing scripts from loaded scene(s) or a prefab asset", "editor_remove_missing_scripts")]
        public static string EditorRemoveMissingScripts(
            [McpParam("Save the modified scene(s) to disk after removal. Default false (in-memory only, revertable by reloading).")] bool save = false,
            [McpParam("Optional prefab asset path (e.g. 'Assets/Foo.prefab'). If set, strips missing scripts from that prefab asset and saves it instead of processing scenes.")] string prefabPath = "")
        {
            // Prefab asset mode
            if (!string.IsNullOrEmpty(prefabPath))
            {
                if (!prefabPath.EndsWith(".prefab"))
                    prefabPath += ".prefab";
                if (!System.IO.File.Exists(prefabPath))
                    return ToolHelpers.Error($"Prefab not found at '{prefabPath}'");

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                int pRemoved = 0;
                int pAffected = 0;
                try
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int r = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                        if (r > 0) { pRemoved += r; pAffected++; }
                    }

                    if (pRemoved > 0)
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                return new JObject
                {
                    ["status"] = "ok",
                    ["message"] = $"Removed {pRemoved} missing-script component(s) from {pAffected} GameObject(s) in prefab '{prefabPath}'",
                    ["removedComponents"] = pRemoved,
                    ["affectedGameObjects"] = pAffected,
                    ["prefab"] = prefabPath,
                    ["saved"] = pRemoved > 0
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            int removedComponents = 0;
            int affectedGameObjects = 0;
            int scenesProcessed = 0;
            var dirtyScenes = new System.Collections.Generic.List<Scene>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;

                scenesProcessed++;
                int removedInScene = 0;

                foreach (var root in scene.GetRootGameObjects())
                {
                    // true => include inactive GameObjects
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                        if (removed > 0)
                        {
                            removedComponents += removed;
                            affectedGameObjects++;
                            removedInScene += removed;
                        }
                    }
                }

                if (removedInScene > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    dirtyScenes.Add(scene);
                }
            }

            bool saved = false;
            if (save && dirtyScenes.Count > 0)
                saved = EditorSceneManager.SaveScenes(dirtyScenes.ToArray());

            return new JObject
            {
                ["status"] = "ok",
                ["message"] = $"Removed {removedComponents} missing-script component(s) from {affectedGameObjects} GameObject(s)",
                ["removedComponents"] = removedComponents,
                ["affectedGameObjects"] = affectedGameObjects,
                ["scenesProcessed"] = scenesProcessed,
                ["saved"] = saved
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        //! Read-only scan for MonoBehaviours with missing script references in the loaded scene(s) and,
        //! optionally, in all prefab assets under a folder. Reports the count per affected asset so a single
        //! call reveals every scene and prefab that still carries orphaned components (the usual cause of a
        //! slow scene load / play-mode entry). Does not modify anything – pair with editor_remove_missing_scripts.
        [McpTool("Scan loaded scene(s) and optionally project prefabs for missing scripts (read-only)", "editor_scan_missing_scripts")]
        public static string EditorScanMissingScripts(
            [McpParam("Optional folder to also scan all prefab assets under (e.g. 'Assets/Mauser_Projekt_CL30'). Empty = loaded scene(s) only.")] string prefabFolder = "",
            [McpParam("Maximum number of affected assets to list (default 50).")] int maxResults = 50)
        {
            var assets = new System.Collections.Generic.List<JObject>();
            int totalMissing = 0;

            // Loaded scenes
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;

                int cnt = 0, gos = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int c = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (c > 0) { cnt += c; gos++; }
                    }
                }

                if (cnt > 0)
                {
                    totalMissing += cnt;
                    assets.Add(new JObject
                    {
                        ["type"] = "scene",
                        ["path"] = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path,
                        ["missingComponents"] = cnt,
                        ["affectedGameObjects"] = gos
                    });
                }
            }

            // Prefab assets in folder
            int prefabsScanned = 0;
            if (!string.IsNullOrEmpty(prefabFolder))
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null)
                        continue;

                    prefabsScanned++;
                    int cnt = 0, gos = 0;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        int c = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (c > 0) { cnt += c; gos++; }
                    }

                    if (cnt > 0)
                    {
                        totalMissing += cnt;
                        assets.Add(new JObject
                        {
                            ["type"] = "prefab",
                            ["path"] = path,
                            ["missingComponents"] = cnt,
                            ["affectedGameObjects"] = gos
                        });
                    }
                }
            }

            // Sort by missing count descending, cap to maxResults
            assets.Sort((a, b) => ((int)b["missingComponents"]).CompareTo((int)a["missingComponents"]));
            var result = new JArray();
            foreach (var a in assets.Take(maxResults))
                result.Add(a);

            return new JObject
            {
                ["status"] = "ok",
                ["totalMissingComponents"] = totalMissing,
                ["affectedAssets"] = assets.Count,
                ["prefabsScanned"] = prefabsScanned,
                ["assets"] = result
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
