// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies transform parenting, reparenting, and unparenting
    public class TestTransformSetParent : FeatureTestBase
    {
        protected override string TestName => "MCP TransformTools Set Parent";

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
        }

        protected override string ValidateResults()
        {
            var parent1 = CreateGameObject("SetParentTest_Parent1");
            var parent2 = CreateGameObject("SetParentTest_Parent2");
            var child = CreateGameObject("SetParentTest_Child");

            child.transform.SetParent(parent1.transform);
            if (child.transform.parent != parent1.transform)
                return $"Child should be parented to Parent1, got {(child.transform.parent != null ? child.transform.parent.name : "null")}";

            child.transform.SetParent(parent2.transform);
            if (child.transform.parent != parent2.transform)
                return "Child should be parented to Parent2 after reparent";

            child.transform.SetParent(null);
            if (child.transform.parent != null)
                return "Child should be at root after unparenting";

            return "";
        }
    }
}
