// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies adding Rigidbody with custom properties
    public class TestPhysicsAddRigidbody : FeatureTestBase
    {
        protected override string TestName => "MCP PhysicsTools Add Rigidbody";

        private GameObject target;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            target = CreateGameObject("TestPhysics");
            var rb = target.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        protected override string ValidateResults()
        {
            var rb = target.GetComponent<Rigidbody>();
            if (rb == null)
                return "Rigidbody not found";
            if (Mathf.Abs(rb.mass - 5f) > 0.01f)
                return $"Mass wrong: expected 5, got {rb.mass}";
            if (rb.useGravity)
                return "useGravity should be false";
            if (!rb.isKinematic)
                return "isKinematic should be true";
            return "";
        }
    }
}
