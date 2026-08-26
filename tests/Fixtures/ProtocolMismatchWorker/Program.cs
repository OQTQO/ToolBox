using System.IO.Pipes;
using System.Text;
using ToolBox.Core.Plugins.Worker;

namespace ProtocolMismatchWorker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = ReadArgument(args, "--pipe");
        var launchId = ReadArgument(args, "--launch-id");

        if (pipeName is null || launchId is null)
        {
            return 2;
        }

        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5_000).ConfigureAwait(false);

        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = false
        };

        _ = await WorkerProtocol.ReadAsync(reader).ConfigureAwait(false);
        await WorkerProtocol.WriteAsync(
                writer,
                new WorkerMessage(
                    WorkerMessageType.HelloAck,
                    WorkerProtocol.ProtocolMajor,
                    LaunchId: launchId + "-mismatch"))
            .ConfigureAwait(false);

        return 0;
    }

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
