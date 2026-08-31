using System.Globalization;
using System.IO.Pipes;
using System.Text;
using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;

namespace ToolBox.PluginWorker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--child-sleeper", StringComparison.Ordinal)))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        if (!WorkerArguments.TryParse(args, out var workerArguments, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            return 2;
        }

        return await WorkerEntryPoint.RunAsync(workerArguments!).ConfigureAwait(false);
    }
}
