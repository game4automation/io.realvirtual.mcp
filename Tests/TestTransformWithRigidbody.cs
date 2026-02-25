// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies that TransformTools detects Rigidbody and can set position
    public class TestTransformWithRigidbody : FeatureTestBase
    {
        protected override string TestName => "MCP TransformTools Rigidbody Detection";

        private GameObject rbObject;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            rbObject = CreateGameObject("TestRBTransform");
            var rb = rbObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rbObject.transform.position = new Vector3(0, 0, 0);
        }

        protected override string ValidateResults()
        {
            if (!rbObject.TryGetComponent<Rigidbody>(out var rb))
                return "Rigidbody should be present";

            rbObject.transform.position = new Vector3(50, 100, 150);
            var pos = rbObject.transform.position;
            if (Mathf.Abs(pos.x - 50f) > 0.01f || Mathf.Abs(pos.y - 100f) > 0.01f || Mathf.Abs(pos.z - 150f) > 0.01f)
                return $"Position wrong: expected (50,100,150), got ({pos.x},{pos.y},{pos.z})";

            return "";
        }
    }
}
