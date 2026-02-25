// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace realvirtual.MCP.Tools
{
    //! MCP tool for running Unity Asset Store Publishing Tools validation via reflection.
    //!
    //! All Asset Store Tools validator classes are internal, so this tool uses reflection
    //! to instantiate and invoke the validator programmatically.
    //! Uses deferred execution via EditorApplication.update to avoid MCP dispatch timeout.
    public static class AssetStoreValidatorTools
    {
        private static Assembly _astAssembly;
        private static bool _isRunning;
        private static string _resultJson;
        private static string _pendingPaths;
        private static string _pendingType;

        private static Assembly GetAssetStoreToolsAssembly()
        {
            if (_astAssembly != null) return _astAssembly;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "asset-store-tools-editor")
                {
                    _astAssembly = asm;
                    return _astAssembly;
                }
            }
            return null;
        }

        //! Start validation with defaults - callable via editor_invoke_method
        public static string RunValidation()
        {
            return AssetstoreValidate("", "unitypackage");
        }

        //! Get validation status - callable via editor_invoke_method
        public static string GetValidationStatus()
        {
            return AssetstoreValidateStatus();
        }

        //! Start Asset Store Publishing Tools validation (async - returns immediately, poll with assetstore_validate_status)
        [McpTool("Start Asset Store Publishing Tools validation on Unity packages. Returns immediately - poll assetstore_validate_status for results.", "assetstore_validate")]
        public static string AssetstoreValidate(
            [McpParam("Comma-separated validation paths (default: both starter and professional packages)")] string paths = "",
            [McpParam("Validation type: 'generic' or 'unitypackage' (default: unitypackage)")] string type = "unitypackage")
        {
            if (_isRunning)
            {
                return new JObject
                {
                    ["status"] = "already_running",
                    ["message"] = "Validation is already in progress. Use assetstore_validate_status to check progress."
                }.ToString(Formatting.None);
            }

            var asm = GetAssetStoreToolsAssembly();
            if (asm == null)
                return ToolHelpers.Error("Asset Store Tools package not found. Install it via Unity Package Manager.");

            // Store params and defer execution
            _pendingPaths = paths;
            _pendingType = type;
            _isRunning = true;
            _resultJson = null;

            EditorApplication.update += RunValidationDeferred;

            var result = new JObject
            {
                ["status"] = "started",
                ["message"] = "Validation started. Use assetstore_validate_status to poll for results."
            };
            return result.ToString(Formatting.None);
        }

        //! Get Asset Store validation status and results
        [McpTool("Get Asset Store validation status. Returns results when validation is complete.", "assetstore_validate_status")]
        public static string AssetstoreValidateStatus()
        {
            if (_isRunning)
            {
                return new JObject
                {
                    ["status"] = "running",
                    ["message"] = "Validation is still in progress. Poll again in a few seconds."
                }.ToString(Formatting.None);
            }

            if (_resultJson != null)
            {
                return _resultJson;
            }

            return new JObject
            {
                ["status"] = "idle",
                ["message"] = "No validation has been run. Use assetstore_validate to start one."
            }.ToString(Formatting.None);
        }

        private static void RunValidationDeferred()
        {
            EditorApplication.update -= RunValidationDeferred;

            try
            {
                _resultJson = ExecuteValidation(_pendingPaths, _pendingType);
            }
            catch (Exception e)
            {
                _resultJson = ToolHelpers.Error($"Validation failed: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _isRunning = false;
                _pendingPaths = null;
                _pendingType = null;
            }
        }

        private static string ExecuteValidation(string paths, string type)
        {
            var asm = GetAssetStoreToolsAssembly();
            if (asm == null)
                return ToolHelpers.Error("Asset Store Tools package not found.");

            // Resolve types
            var settingsType = asm.GetType("AssetStoreTools.Validator.Data.CurrentProjectValidationSettings");
            var validatorType = asm.GetType("AssetStoreTools.Validator.CurrentProjectValidator");
            var validationTypeEnum = asm.GetType("AssetStoreTools.Validator.Data.ValidationType");
            var testResultStatusEnum = asm.GetType("AssetStoreTools.Validator.Data.TestResultStatus");
            var validationStatusEnum = asm.GetType("AssetStoreTools.Validator.Data.ValidationStatus");

            if (settingsType == null || validatorType == null)
                return ToolHelpers.Error("Could not find Asset Store Tools validator types. Package version may be incompatible.");

            // Create settings instance
            var settings = Activator.CreateInstance(settingsType);

            // Set ValidationPaths
            var validationPaths = new List<string>();
            if (string.IsNullOrEmpty(paths))
            {
                validationPaths.Add("Packages/io.realvirtual.starter");
                validationPaths.Add("Packages/io.realvirtual.professional");
            }
            else
            {
                foreach (var p in paths.Split(','))
                {
                    var trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        validationPaths.Add(trimmed);
                }
            }

            var pathsField = settingsType.GetField("ValidationPaths", BindingFlags.Public | BindingFlags.Instance);
            if (pathsField != null)
                pathsField.SetValue(settings, validationPaths);

            // Set ValidationType
            if (validationTypeEnum != null)
            {
                var vtField = settingsType.GetField("ValidationType", BindingFlags.Public | BindingFlags.Instance);
                if (vtField != null)
                {
                    var enumVal = type.ToLowerInvariant() == "generic"
                        ? Enum.Parse(validationTypeEnum, "Generic")
                        : Enum.Parse(validationTypeEnum, "UnityPackage");
                    vtField.SetValue(settings, enumVal);
                }
            }

            // Create validator
            var validator = Activator.CreateInstance(validatorType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { settings }, null);

            // Call Validate()
            var validateMethod = validatorType.GetMethod("Validate",
                BindingFlags.Public | BindingFlags.Instance)
                ?? validatorType.BaseType?.GetMethod("Validate",
                    BindingFlags.Public | BindingFlags.Instance);

            if (validateMethod == null)
                return ToolHelpers.Error("Could not find Validate() method on validator.");

            var result = validateMethod.Invoke(validator, null);

            if (result == null)
                return ToolHelpers.Error("Validation returned null result.");

            // Extract results via reflection
            return ExtractResults(result, testResultStatusEnum, validationStatusEnum);
        }

        private static string ExtractResults(object result, Type testResultStatusEnum, Type validationStatusEnum)
        {
            var resultType = result.GetType();

            // Read Status
            var statusField = resultType.GetField("Status", BindingFlags.Public | BindingFlags.Instance);
            var status = statusField?.GetValue(result);
            var statusStr = status?.ToString() ?? "Unknown";

            // Read HadCompilationErrors
            var compErrorsField = resultType.GetField("HadCompilationErrors", BindingFlags.Public | BindingFlags.Instance);
            var hadCompErrors = compErrorsField != null && (bool)compErrorsField.GetValue(result);

            // Read Exception
            var exceptionField = resultType.GetField("Exception", BindingFlags.Public | BindingFlags.Instance);
            var exception = exceptionField?.GetValue(result) as Exception;

            // Read Tests list
            var testsField = resultType.GetField("Tests", BindingFlags.Public | BindingFlags.Instance);
            var tests = testsField?.GetValue(result);

            int totalTests = 0;
            int passed = 0;
            int failed = 0;
            int warning = 0;
            int undefined = 0;

            var testsArray = new JArray();

            if (tests is System.Collections.IList testList)
            {
                totalTests = testList.Count;

                foreach (var test in testList)
                {
                    var testType = test.GetType();

                    // Read Title (inherited from ValidationTest)
                    var titleField = testType.GetField("Title", BindingFlags.Public | BindingFlags.Instance)
                        ?? testType.BaseType?.GetField("Title", BindingFlags.Public | BindingFlags.Instance);
                    var title = titleField?.GetValue(test)?.ToString() ?? "Unknown";

                    // Read Description
                    var descField = testType.GetField("Description", BindingFlags.Public | BindingFlags.Instance)
                        ?? testType.BaseType?.GetField("Description", BindingFlags.Public | BindingFlags.Instance);
                    var description = descField?.GetValue(test)?.ToString() ?? "";

                    // Read Id
                    var idField = testType.GetField("Id", BindingFlags.Public | BindingFlags.Instance)
                        ?? testType.BaseType?.GetField("Id", BindingFlags.Public | BindingFlags.Instance);
                    var id = idField != null ? (int)idField.GetValue(test) : 0;

                    // Read Result (TestResult struct)
                    var resultField = testType.GetField("Result", BindingFlags.Public | BindingFlags.Instance)
                        ?? testType.BaseType?.GetField("Result", BindingFlags.Public | BindingFlags.Instance);

                    string testStatus = "Undefined";
                    var messages = new JArray();

                    if (resultField != null)
                    {
                        var testResult = resultField.GetValue(test);
                        var trType = testResult.GetType();

                        // Read TestResult.Status
                        var trStatusField = trType.GetField("Status", BindingFlags.Public | BindingFlags.Instance);
                        if (trStatusField != null)
                        {
                            testStatus = trStatusField.GetValue(testResult)?.ToString() ?? "Undefined";
                        }

                        // Read MessageCount
                        var msgCountProp = trType.GetProperty("MessageCount", BindingFlags.Public | BindingFlags.Instance);
                        int msgCount = 0;
                        if (msgCountProp != null)
                            msgCount = (int)msgCountProp.GetValue(testResult);

                        // Read messages via GetMessage(int index)
                        var getMessageMethod = trType.GetMethod("GetMessage", BindingFlags.Public | BindingFlags.Instance);
                        if (getMessageMethod != null && msgCount > 0)
                        {
                            for (int i = 0; i < msgCount; i++)
                            {
                                try
                                {
                                    var msg = getMessageMethod.Invoke(testResult, new object[] { i });
                                    if (msg != null)
                                    {
                                        var getTextMethod = msg.GetType().GetMethod("GetText", BindingFlags.Public | BindingFlags.Instance);
                                        var text = getTextMethod?.Invoke(msg, null)?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(text))
                                            messages.Add(text);
                                    }
                                }
                                catch
                                {
                                    // Skip messages that can't be read
                                }
                            }
                        }
                    }

                    // Count by status
                    switch (testStatus)
                    {
                        case "Pass": passed++; break;
                        case "Fail": failed++; break;
                        case "Warning": case "VariableSeverityIssue": warning++; break;
                        default: undefined++; break;
                    }

                    var testObj = new JObject
                    {
                        ["id"] = id,
                        ["title"] = title,
                        ["status"] = testStatus,
                    };

                    if (!string.IsNullOrEmpty(description))
                        testObj["description"] = description;

                    if (messages.Count > 0)
                        testObj["messages"] = messages;

                    testsArray.Add(testObj);
                }
            }

            var response = new JObject
            {
                ["status"] = "ok",
                ["validationStatus"] = statusStr,
                ["hadCompilationErrors"] = hadCompErrors,
                ["summary"] = new JObject
                {
                    ["total"] = totalTests,
                    ["passed"] = passed,
                    ["failed"] = failed,
                    ["warning"] = warning,
                    ["undefined"] = undefined
                },
                ["tests"] = testsArray
            };

            if (exception != null)
                response["exception"] = exception.Message;

            return response.ToString(Formatting.None);
        }
    }
}
#endif
