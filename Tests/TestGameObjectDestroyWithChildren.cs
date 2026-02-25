// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies destroying parent also destroys children
    public class TestGameObjectDestroyWithChildren : FeatureTestBase
    {
        protected override string TestName => "MCP GameObjectTools Destroy With Children";

        private string parentName = "TestDestroyParent";
        private string childName = "TestDestroyChild";

        protected override void SetupTest()
        {
            var parent = CreateGameObject(parentName);
            var child = CreateGameObject(childName, parent.transform);
            Object.DestroyImmediate(parent);
        }

        protected override string ValidateResults()
        {
            var foundParent = GameObject.Find(parentName);
            if (foundParent != null)
                return "Parent should have been destroyed";

            var foundChild = GameObject.Find(childName);
            if (foundChild != null)
                return "Child should have been destroyed with parent";

            return "";
        }
    }
}
