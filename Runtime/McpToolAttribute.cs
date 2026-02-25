// realvirtual MCP - Unity MCP Server
// Copyright (c) 2026 realvirtual GmbH
// Licensed under the MIT License. See LICENSE file for details.

using System;

namespace realvirtual.MCP
{
    //! Marks a static method as an MCP tool that can be called by AI agents.
    //!
    //! The method must be public static and return a string (preferably JSON).
    //! Method name will be converted from PascalCase to snake_case automatically.
    //!
    //! Example:
    //! [McpTool("Start the simulation")]
    //! public static string SimPlay() { return "{\"status\":\"playing\"}"; }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class McpToolAttribute : Attribute
    {
        //! Description of what the tool does (shown to AI agents)
        public string Description { get; }

        //! Optional override for the tool name (defaults to method name converted to snake_case)
        public string Name { get; }

        //! Creates an MCP tool attribute with description and optional name override.
        //! @param description Brief description of the tool's purpose and function
        //! @param name Optional tool name override (if null, uses method name converted to snake_case)
        public McpToolAttribute(string description, string name = null)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Name = name;
        }
    }
}
