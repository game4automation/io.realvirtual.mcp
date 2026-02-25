// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies that adding a Rigidbody to an object that already has one is guarded
    public class TestPhysicsAddDuplicateRigidbody : FeatureTestBase
    {
        protected override string TestName => "MCP PhysicsTools Duplicate Rigidbody Guard";

        private GameObject target;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            target = CreateGameObject("TestDuplicateRB");
            target.AddComponent<Rigidbody>();
        }

        protected override string ValidateResults()
        {
            var rb = target.GetComponent<Rigidbody>();
            if (rb == null)
                return "First Rigidbody should exist";

            var existingRb = target.GetComponent<Rigidbody>();
            if (existingRb == null)
                return "Rigidbody disappeared unexpectedly";

            return "";
        }
    }
}
