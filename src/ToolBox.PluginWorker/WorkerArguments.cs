using System.Globalization;
using System.IO.Pipes;
using System.Text;
using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;

namespace ToolBox.PluginWorker;

public sealed record WorkerArguments(string PipeName, string LaunchId, string PluginDirectory)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out WorkerArguments? arguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? pipeName = null;
        string? launchId = null;
        string? pluginDirectory = null;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];

            if (index + 1 >= args.Count)
            {
                arguments = null;
                error = $"Missing value for '{argument}'.";
                return false;
            }

            var value = args[++index];

            switch (argument)
            {
                case "--pipe":
                    pipeName = value;
                    break;
                case "--launch-id":
                    launchId = value;
                    break;
                case "--plugin-directory":
                    pluginDirectory = value;
                    break;
                default:
                    arguments = null;
                    error = $"Unknown Worker argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName)
            || string.IsNullOrWhiteSpace(launchId)
            || string.IsNullOrWhiteSpace(pluginDirectory))
        {
            arguments = null;
            error = "Worker requires --pipe, --launch-id, and --plugin-directory.";
            return false;
        }

        arguments = new WorkerArguments(pipeName, launchId, Path.GetFullPath(pluginDirectory));
        error = string.Empty;
        return true;
    }
}
