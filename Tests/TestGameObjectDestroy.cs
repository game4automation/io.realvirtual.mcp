// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies GameObject destruction works correctly
    public class TestGameObjectDestroy : FeatureTestBase
    {
        protected override string TestName => "MCP GameObjectTools Destroy";

        private string objectName = "TestDestroyTarget";

        protected override void SetupTest()
        {
            var go = CreateGameObject(objectName);
            Object.DestroyImmediate(go);
        }

        protected override string ValidateResults()
        {
            var found = GameObject.Find(objectName);
            if (found != null)
                return "GameObject should have been destroyed but was still found";
            return "";
        }
    }
}
