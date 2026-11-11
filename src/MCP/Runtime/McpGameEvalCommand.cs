using System;
using System.Text;
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

            // Opt-in clean slate: drops REPL variables/classes and recompiles the default usings.
            // Useful after a compiler crash or when a snippet redeclares an existing variable.
            if (command.GetBoolean("fresh", false))
                ConsoleController.RecreateMcpEvaluator();

            string compilerOutput;
            object returnValue;
            try
            {
                returnValue = ConsoleController.EvaluateCode(code, out compilerOutput);
            }
            catch (Exception ex)
            {
                Exception actual = McpReflection.Unwrap(ex);
                throw new McpCommandException("evaluation_failed", DescribeFailure(actual));
            }

            McpJsonValue r = McpJsonValue.Object();
            r.ObjectValue["success"] = McpJsonValue.From(true);
            r.ObjectValue["returnValue"] = codec.Serialize(returnValue, options.MaximumSnapshotDepth, options.MaximumSerializedItems);
            if (!string.IsNullOrEmpty(compilerOutput))
                r.ObjectValue["compilerOutput"] = McpJsonValue.From(compilerOutput.Trim());

            return r;
        }

        /// <summary>
        /// Mono.CSharp folds the real cause of an internal error into an InnerException, and CS0584
        /// only prints the outer message. Walk the chain so the agent can see what actually failed.
        /// </summary>
        private static string DescribeFailure(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" <- ").Append(current.GetType().Name).Append(": ");
                sb.Append(current.Message);
            }

            string message = sb.ToString();
            if (message.Contains("CS0127"))
                message += "\nHint: the REPL body is compiled as void. Use the last expression as the result instead of `return value;`.";
            else if (message.Contains("CS0584"))
                message += "\nHint: IL2CPP metadata resolution failed even after an evaluator reset. Prefer get_member/invoke_method for this type.";

            return message;
        }
    }
}
