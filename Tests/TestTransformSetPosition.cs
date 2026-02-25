// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies local and world position with parent offset
    public class TestTransformSetPosition : FeatureTestBase
    {
        protected override string TestName => "MCP TransformTools Set Position";

        private GameObject parent;
        private GameObject child;

        protected override void SetupTest()
        {
            parent = CreateGameObject("TestParent");
            parent.transform.position = new Vector3(100, 0, 0);

            child = CreateGameObject("TestChild");
            child.transform.SetParent(parent.transform);
            child.transform.localPosition = new Vector3(10, 20, 30);
        }

        protected override string ValidateResults()
        {
            var localPos = child.transform.localPosition;
            if (Mathf.Abs(localPos.x - 10f) > 0.01f || Mathf.Abs(localPos.y - 20f) > 0.01f || Mathf.Abs(localPos.z - 30f) > 0.01f)
                return $"Local position wrong: expected (10,20,30), got ({localPos.x},{localPos.y},{localPos.z})";

            var worldPos = child.transform.position;
            if (Mathf.Abs(worldPos.x - 110f) > 0.01f)
                return $"World X should be 110 (parent 100 + local 10), got {worldPos.x}";

            return "";
        }
    }
}
