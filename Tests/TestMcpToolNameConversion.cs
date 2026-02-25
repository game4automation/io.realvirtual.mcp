// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System.Linq;
using realvirtual.MCP;

namespace realvirtual.MCP.Tests
{
    //! Validates that PascalCase method names are correctly converted to snake_case tool names
    public class TestMcpToolNameConversion : FeatureTestBase
    {
        protected override string TestName => "MCP tool names convert from PascalCase to snake_case";

        private bool hasDriveList;
        private bool hasDriveJogForward;
        private bool hasSceneGetTransform;
        private bool hasSimSetSpeed;
        private bool hasSensorGetOccupied;

        protected override void SetupTest()
        {
            MinTestTime = 0.5f;

            var registry = new McpToolRegistry();
            registry.DiscoverTools();
            var names = registry.ToolNames.ToList();

            // PascalCase Methodennamen -> snake_case Tool-Namen
            // DriveList -> drive_list
            // DriveJogForward -> drive_jog_forward
            // SceneGetTransform -> scene_get_transform
            // SimSetSpeed -> sim_set_speed
            // SensorGetOccupied -> sensor_get_occupied
            hasDriveList = names.Contains("drive_list");
            hasDriveJogForward = names.Contains("drive_jog_forward");
            hasSceneGetTransform = names.Contains("scene_get_transform");
            hasSimSetSpeed = names.Contains("sim_set_speed");
            hasSensorGetOccupied = names.Contains("sensor_get_occupied");
        }

        protected override string ValidateResults()
        {
            if (!hasDriveList)
                return "DriveList was not converted to 'drive_list'";
            if (!hasDriveJogForward)
                return "DriveJogForward was not converted to 'drive_jog_forward'";
            if (!hasSceneGetTransform)
                return "SceneGetTransform was not converted to 'scene_get_transform'";
            if (!hasSimSetSpeed)
                return "SimSetSpeed was not converted to 'sim_set_speed'";
            if (!hasSensorGetOccupied)
                return "SensorGetOccupied was not converted to 'sensor_get_occupied'";

            return "";
        }
    }
}
