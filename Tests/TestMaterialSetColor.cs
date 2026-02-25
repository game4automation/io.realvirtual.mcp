// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies setting material color on a Renderer
    public class TestMaterialSetColor : FeatureTestBase
    {
        protected override string TestName => "MCP MaterialTools Set Color";

        private GameObject cube;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            cube = CreatePrimitive(PrimitiveType.Cube, "TestCube");

            var renderer = cube.GetComponent<Renderer>();
            renderer.material.color = new Color(1f, 0f, 0f, 1f);
        }

        protected override string ValidateResults()
        {
            var renderer = cube.GetComponent<Renderer>();
            if (renderer == null)
                return "Renderer not found on cube";

            var color = renderer.material.color;
            if (Mathf.Abs(color.r - 1f) > 0.01f || Mathf.Abs(color.g) > 0.01f || Mathf.Abs(color.b) > 0.01f)
                return $"Color wrong: expected (1,0,0), got ({color.r},{color.g},{color.b})";

            return "";
        }
    }
}
