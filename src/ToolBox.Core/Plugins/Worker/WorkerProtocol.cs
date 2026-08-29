using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBox.Core.Plugins.Worker;

public enum WorkerMessageType
{
    Hello,
    HelloAck,
    Request,
    Response,
    Event,
    Error,
    Cancel,
    Heartbeat,
    Shutdown
}

public sealed record WorkerMessage(
    [property: JsonPropertyName("type")] WorkerMessageType Type,
    [property: JsonPropertyName("protocolMajor")] int ProtocolMajor,
    [property: JsonPropertyName("launchId")] string? LaunchId = null,
    [property: JsonPropertyName("requestId")] string? RequestId = null,
    [property: JsonPropertyName("operation")] string? Operation = null,
    [property: JsonPropertyName("payload")] string? Payload = null,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null);

public static class WorkerProtocol
{
    public const int ProtocolMajor = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static WorkerMessage CreateHello(string launchId)
    {
        return new WorkerMessage(
            WorkerMessageType.Hello,
            ProtocolMajor,
            LaunchId: launchId);
    }

    public static WorkerMessage CreateHelloAck(string launchId)
    {
        return new WorkerMessage(
            WorkerMessageType.HelloAck,
            ProtocolMajor,
            LaunchId: launchId);
    }

    public static WorkerMessage CreateRequest(string launchId, string requestId, string operation, string? payload = null)
    {
        return new WorkerMessage(
            WorkerMessageType.Request,
            ProtocolMajor,
            LaunchId: launchId,
            RequestId: requestId,
            Operation: operation,
            Payload: payload);
    }

    public static WorkerMessage CreateResponse(string launchId, string requestId, string operation, string? payload = null)
    {
        return new WorkerMessage(
            WorkerMessageType.Response,
            ProtocolMajor,
            LaunchId: launchId,
            RequestId: requestId,
            Operation: operation,
            Payload: payload);
    }

    public static WorkerMessage CreateEvent(string launchId, string operation, string? payload = null)
    {
        return new WorkerMessage(
            WorkerMessageType.Event,
            ProtocolMajor,
            LaunchId: launchId,
            Operation: operation,
            Payload: payload);
    }

    public static WorkerMessage CreateError(
        string launchId,
        string? requestId,
        string errorCode,
        string errorMessage)
    {
        return new WorkerMessage(
            WorkerMessageType.Error,
            ProtocolMajor,
            LaunchId: launchId,
            RequestId: requestId,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);
    }

    public static WorkerMessage CreateHeartbeat(string launchId, string? requestId = null)
    {
        return new WorkerMessage(
            WorkerMessageType.Heartbeat,
            ProtocolMajor,
            LaunchId: launchId,
            RequestId: requestId,
            Payload: "alive");
    }

    public static string SerializePayload<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static T DeserializePayload<T>(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new WorkerProtocolException(
                "WORKER_PAYLOAD_EMPTY",
                "The Worker control message did not contain the required payload.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new WorkerProtocolException(
                    "WORKER_PAYLOAD_EMPTY",
                    "The Worker control message returned an empty payload.");
        }
        catch (JsonException exception)
        {
            throw new WorkerProtocolException(
                "WORKER_PAYLOAD_INVALID",
                "The Worker control message returned an invalid payload.",
                exception);
        }
    }

    public static async ValueTask WriteAsync(
        StreamWriter writer,
        WorkerMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<WorkerMessage> ReadAsync(
        StreamReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (line is null)
        {
            throw new WorkerProtocolException(
                "WORKER_PIPE_CLOSED",
                "The Worker control channel closed before a complete message was received.");
        }

        try
        {
            return JsonSerializer.Deserialize<WorkerMessage>(line, JsonOptions)
                ?? throw new WorkerProtocolException(
                    "WORKER_MESSAGE_EMPTY",
                    "The Worker control channel returned an empty message.");
        }
        catch (JsonException exception)
        {
            throw new WorkerProtocolException(
                "WORKER_MESSAGE_INVALID",
                "The Worker control channel returned invalid JSON.",
                exception);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }
}

public sealed class WorkerProtocolException : InvalidOperationException
{
    public WorkerProtocolException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public WorkerProtocolException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public static class WorkerHandshake
{
    public static void ValidateHello(WorkerMessage message, string expectedLaunchId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLaunchId);

        if (message.Type != WorkerMessageType.Hello)
        {
            throw new WorkerProtocolException(
                "WORKER_HELLO_REQUIRED",
                $"Expected Hello but received '{message.Type}'.");
        }

        ValidateProtocol(message);
        ValidateLaunchId(message, expectedLaunchId);
    }

    public static void ValidateHelloAck(WorkerMessage message, string expectedLaunchId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLaunchId);

        if (message.Type != WorkerMessageType.HelloAck)
        {
            throw new WorkerProtocolException(
                "WORKER_HELLO_ACK_REQUIRED",
                $"Expected HelloAck but received '{message.Type}'.");
        }

        ValidateProtocol(message);
        ValidateLaunchId(message, expectedLaunchId);
    }

    private static void ValidateProtocol(WorkerMessage message)
    {
        if (message.ProtocolMajor != WorkerProtocol.ProtocolMajor)
        {
            throw new WorkerProtocolException(
                "WORKER_PROTOCOL_MISMATCH",
                $"Worker protocol major '{message.ProtocolMajor}' is not supported.");
        }
    }

    private static void ValidateLaunchId(WorkerMessage message, string expectedLaunchId)
    {
        if (!string.Equals(message.LaunchId, expectedLaunchId, StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                "WORKER_LAUNCH_ID_MISMATCH",
                "The Worker did not prove the expected launch identity.");
        }
    }
}
