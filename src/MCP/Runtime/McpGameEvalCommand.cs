using System;
using System.IO;
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

            string code;
            string sourceFile = null;
            string file = command.GetString("file", null);
            if (!string.IsNullOrEmpty(file))
            {
                sourceFile = ResolveScriptPath(file);
                if (sourceFile == null || !File.Exists(sourceFile))
                    throw new McpCommandException("file_not_found", "Script file was not found: " + file);

                try
                {
                    code = File.ReadAllText(sourceFile);
                }
                catch (Exception ex)
                {
                    throw new McpCommandException("file_not_found", "Could not read '" + sourceFile + "': " + McpReflection.Unwrap(ex).Message);
                }
            }
            else
            {
                code = RequiredString(command, "code");
            }

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
            if (sourceFile != null)
                r.ObjectValue["sourceFile"] = McpJsonValue.From(sourceFile);

            return r;
        }

        /// <summary>
        /// Absolute paths are used as-is. Relative paths are looked up next to the game, in the
        /// current directory, and in the UnityExplorer Scripts folder.
        /// </summary>
        private static string ResolveScriptPath(string path)
        {
            try
            {
                if (Path.IsPathRooted(path))
                    return path;

                string[] roots =
                {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Directory.GetCurrentDirectory(),
                    ConsoleController.ScriptsFolder,
                };

                foreach (string root in roots)
                {
                    string candidate = Path.Combine(root, path);
                    if (File.Exists(candidate))
                        return candidate;
                }

                return Path.Combine(ConsoleController.ScriptsFolder, path);
            }
            catch
            {
                return null;
            }
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
            else if (message.Contains("CS0101"))
                message += "\nHint: the file defines a type that already exists in this REPL session. Use fresh=true to reset, or keep shared classes in the Scripts/startup.cs.";
            else if (message.Contains("CS0584"))
                message += "\nHint: IL2CPP metadata resolution failed even after an evaluator reset. Prefer get_member/invoke_method for this type.";

            return message;
        }
    }
}
