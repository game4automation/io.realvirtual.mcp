// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;
using realvirtual.MCP.Tools;

namespace realvirtual.MCP.Tests
{
    //! Verifies that a primitive can be created and found via ToolHelpers (simulating prefab instantiate flow)
    public class TestPrefabInstantiate : FeatureTestBase
    {
        protected override string TestName => "MCP PrefabTools Create Primitive";

        private GameObject sphere;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            sphere = CreatePrimitive(PrimitiveType.Sphere, "InstantiatedSphere");

            sphere.transform.position = new Vector3(5, 10, 15);
        }

        protected override string ValidateResults()
        {
            var found = ToolHelpers.FindGameObject("InstantiatedSphere");
            if (found == null)
                return "Could not find InstantiatedSphere via ToolHelpers.FindGameObject";

            var pos = found.transform.position;
            if (Mathf.Abs(pos.x - 5f) > 0.01f || Mathf.Abs(pos.y - 10f) > 0.01f || Mathf.Abs(pos.z - 15f) > 0.01f)
                return $"Position wrong: expected (5,10,15), got ({pos.x},{pos.y},{pos.z})";

            if (found.GetComponent<MeshRenderer>() == null)
                return "Sphere should have MeshRenderer";
            if (found.GetComponent<SphereCollider>() == null)
                return "Sphere should have SphereCollider";

            return "";
        }
    }
}
