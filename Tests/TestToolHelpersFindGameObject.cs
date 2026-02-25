// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;
using realvirtual.MCP.Tools;

namespace realvirtual.MCP.Tests
{
    //! Verifies ToolHelpers.FindGameObject works by name, by path, and finds deactivated objects
    public class TestToolHelpersFindGameObject : FeatureTestBase
    {
        protected override string TestName => "MCP ToolHelpers FindGameObject";

        private GameObject active;
        private GameObject inactive;
        private GameObject nested;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            active = CreateGameObject("FindTestActive");
            inactive = CreateGameObject("FindTestInactive");
            inactive.SetActive(false);

            var parent = CreateGameObject("FindTestParent");
            nested = CreateGameObject("FindTestNested");
            nested.transform.SetParent(parent.transform);
        }

        protected override string ValidateResults()
        {
            var foundActive = ToolHelpers.FindGameObject("FindTestActive");
            if (foundActive == null)
                return "Could not find active object by name";

            var foundInactive = ToolHelpers.FindGameObject("FindTestInactive");
            if (foundInactive == null)
                return "Could not find inactive object - FindObjectsByType must use FindObjectsInactive.Include";

            var path = ToolHelpers.GetGameObjectPath(nested);
            var foundByPath = ToolHelpers.FindGameObject(path);
            if (foundByPath == null)
                return $"Could not find nested object by path '{path}'";

            var foundNull = ToolHelpers.FindGameObject(null);
            if (foundNull != null)
                return "FindGameObject(null) should return null";

            var foundEmpty = ToolHelpers.FindGameObject("");
            if (foundEmpty != null)
                return "FindGameObject('') should return null";

            return "";
        }
    }
}
