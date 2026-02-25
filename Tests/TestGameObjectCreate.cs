// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;
using realvirtual.MCP.Tools;

namespace realvirtual.MCP.Tests
{
    //! Verifies GameObjectTools create with parent hierarchy
    public class TestGameObjectCreate : FeatureTestBase
    {
        protected override string TestName => "MCP GameObjectTools Create";

        private GameObject parent;
        private GameObject child;

        protected override void SetupTest()
        {
            parent = CreateGameObject("TestParent");
            child = CreateGameObject("TestChild", parent.transform);
        }

        protected override string ValidateResults()
        {
            if (child == null)
                return "Child GameObject is null";
            if (child.transform.parent != parent.transform)
                return $"Child parent is wrong: expected TestParent, got {child.transform.parent?.name}";
            if (child.name != "TestChild")
                return $"Child name wrong: expected TestChild, got {child.name}";

            var path = ToolHelpers.GetGameObjectPath(child);
            if (!path.Contains("TestParent"))
                return $"Path should contain parent: got {path}";

            return "";
        }
    }
}
