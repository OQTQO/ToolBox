using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using ToolBox.PluginSdk;
using ToolBox.Core.Plugins.Worker;

namespace ToolBox.Core.Plugins;

[SupportedOSPlatform("windows")]
public sealed class OutOfProcessPluginRuntime
{
    public const int DefaultConnectionTimeoutMilliseconds = 5_000;

    private readonly PluginDiscovery _discovery;
    private readonly WorkerProcessLauncher _processLauncher;

    public OutOfProcessPluginRuntime(
        string workerExecutablePath,
        PluginDiscovery? discovery = null,
        WorkerProcessLauncher? processLauncher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerExecutablePath);

        WorkerExecutablePath = Path.GetFullPath(workerExecutablePath);
        _discovery = discovery ?? new PluginDiscovery();
        _processLauncher = processLauncher ?? new WorkerProcessLauncher();
    }

    public string WorkerExecutablePath { get; }

    public IReadOnlyList<DiscoveredPlugin> Discover(string pluginsRoot)
    {
        return _discovery.Discover(pluginsRoot);
    }

    public DiscoveredPlugin DiscoverSingle(string pluginDirectory)
    {
        return _discovery.DiscoverSingle(pluginDirectory);
    }

    public Task<OutOfProcessPluginSession> StartAsync(
        string pluginDirectory,
        CancellationToken cancellationToken = default)
    {
        return StartAsync(DiscoverSingle(pluginDirectory), cancellationToken);
    }

    public async Task<OutOfProcessPluginSession> StartAsync(
        DiscoveredPlugin discoveredPlugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveredPlugin);

        if (discoveredPlugin.Manifest.Runtime is null
            || !discoveredPlugin.Manifest.Runtime.SupportedModes.Contains(PluginExecutionMode.OutOfProcess))
        {
            throw new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{discoveredPlugin.Manifest.Id}' does not support '{PluginExecutionMode.OutOfProcess}' execution.");
        }

        var launchId = Guid.NewGuid().ToString("N");
        var pipeName = $"ToolBox.Worker.{launchId}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        WorkerProcessHandle? worker = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;

        try
        {
            worker = _processLauncher.Start(
                WorkerExecutablePath,
                discoveredPlugin.DirectoryPath,
                pipeName,
                launchId);

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(DefaultConnectionTimeoutMilliseconds);

            try
            {
                await pipe.WaitForConnectionAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WorkerProtocolException(
                    "WORKER_CONNECT_TIMEOUT",
                    "The PluginWorker did not connect to its control channel in time.");
            }

            reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = false
            };

            WorkerMessage helloAck;

            try
            {
                await WorkerProtocol.WriteAsync(
                        writer,
                        WorkerProtocol.CreateHello(launchId),
                        timeoutCancellation.Token)
                    .ConfigureAwait(false);

                helloAck = await WorkerProtocol.ReadAsync(reader, timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WorkerProtocolException(
                    "WORKER_HANDSHAKE_TIMEOUT",
                    "The PluginWorker did not complete its control-channel handshake in time.");
            }

            if (helloAck.Type == WorkerMessageType.Error)
            {
                throw new WorkerProtocolException(
                    helloAck.ErrorCode ?? "WORKER_HANDSHAKE_FAILED",
                    helloAck.ErrorMessage ?? "The PluginWorker rejected the control-channel handshake.");
            }

            WorkerHandshake.ValidateHelloAck(helloAck, launchId);

            return new OutOfProcessPluginSession(
                discoveredPlugin,
                launchId,
                worker,
                pipe,
                reader,
                writer);
        }
        catch
        {
            writer?.Dispose();
            reader?.Dispose();
            pipe.Dispose();
            worker?.Dispose();
            throw;
        }
    }
}
