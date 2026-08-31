using System.Text;
using ToolBox.Core.Plugins.Worker;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class WorkerProtocolTests
{
    [Fact]
    public void HelloAckWithExpectedIdentityIsAccepted()
    {
        var launchId = "launch-123";

        var exception = Record.Exception(() => WorkerHandshake.ValidateHelloAck(
            WorkerProtocol.CreateHelloAck(launchId),
            launchId));

        Assert.Null(exception);
    }

    [Fact]
    public void HelloAckWithDifferentLaunchIdIsRejected()
    {
        var exception = Assert.Throws<WorkerProtocolException>(() => WorkerHandshake.ValidateHelloAck(
            WorkerProtocol.CreateHelloAck("launch-other"),
            "launch-expected"));

        Assert.Equal("WORKER_LAUNCH_ID_MISMATCH", exception.ErrorCode);
    }

    [Fact]
    public void HelloAckWithDifferentProtocolMajorIsRejected()
    {
        var exception = Assert.Throws<WorkerProtocolException>(() => WorkerHandshake.ValidateHelloAck(
            new WorkerMessage(
                WorkerMessageType.HelloAck,
                WorkerProtocol.ProtocolMajor + 1,
                LaunchId: "launch-expected"),
            "launch-expected"));

        Assert.Equal("WORKER_PROTOCOL_MISMATCH", exception.ErrorCode);
    }

    [Fact]
    public async Task JsonLinesRoundTripPreservesRequestFields()
    {
        await using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(
                         stream,
                         new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                         bufferSize: 1024,
                         leaveOpen: true))
        {
            await WorkerProtocol.WriteAsync(
                writer,
                WorkerProtocol.CreateRequest(
                    "launch-123",
                    "request-456",
                    "start",
                    "payload"));
        }

        stream.Position = 0;

        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var message = await WorkerProtocol.ReadAsync(reader);

        Assert.Equal(WorkerMessageType.Request, message.Type);
        Assert.Equal("launch-123", message.LaunchId);
        Assert.Equal("request-456", message.RequestId);
        Assert.Equal("start", message.Operation);
        Assert.Equal("payload", message.Payload);
    }

    [Fact]
    public async Task WriteRejectsMessageAboveCharacterLimit()
    {
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        var message = WorkerProtocol.CreateRequest(
            "launch-123",
            "request-456",
            "ui.action",
            new string('a', WorkerProtocol.MaxMessageCharacters));

        var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
            () => WorkerProtocol.WriteAsync(writer, message).AsTask());

        Assert.Equal("WORKER_MESSAGE_TOO_LARGE", exception.ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task ReadRejectsLineAboveCharacterLimitWithoutWaitingForNewline()
    {
        var oversizedLine = new string('a', WorkerProtocol.MaxMessageCharacters + 1);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(oversizedLine));
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
            () => WorkerProtocol.ReadAsync(reader).AsTask());

        Assert.Equal("WORKER_MESSAGE_TOO_LARGE", exception.ErrorCode);
    }
}
