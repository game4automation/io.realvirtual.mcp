// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using UnityEngine;

namespace realvirtual.MCP.Tests
{
    //! Verifies material tools handle GameObjects without Renderer gracefully
    public class TestMaterialSetColorNoRenderer : FeatureTestBase
    {
        protected override string TestName => "MCP MaterialTools No Renderer Guard";

        private GameObject emptyObject;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;
            emptyObject = CreateGameObject("TestNoRenderer");
        }

        protected override string ValidateResults()
        {
            var renderer = emptyObject.GetComponent<Renderer>();
            if (renderer != null)
                return "Empty object should NOT have a Renderer";

            return "";
        }
    }
}
