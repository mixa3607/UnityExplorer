using System;
using UnityExplorer.CSConsole;

namespace UnityExplorer.MCP.Runtime
{
    public sealed partial class McpGameCapabilityExecutor
    {
        private McpJsonValue ExecuteCSharp(McpJsonValue command)
        {
            if (!options.AllowMethodInvocation)
                throw new McpCommandException("disabled", "Code execution is disabled by policy.");

            string code = RequiredString(command, "code");
            string compilerOutput;
            object returnValue;
            try
            {
                returnValue = ConsoleController.EvaluateCode(code, out compilerOutput);
            }
            catch (Exception ex)
            {
                Exception actual = McpReflection.Unwrap(ex);
                throw new McpCommandException("evaluation_failed", actual.Message);
            }

            McpJsonValue r = McpJsonValue.Object();
            r.ObjectValue["success"] = McpJsonValue.From(true);
            r.ObjectValue["returnValue"] = codec.Serialize(returnValue, options.MaximumSnapshotDepth, options.MaximumSerializedItems);
            if (!string.IsNullOrEmpty(compilerOutput))
                r.ObjectValue["compilerOutput"] = McpJsonValue.From(compilerOutput.Trim());

            return r;
        }
    }
}
