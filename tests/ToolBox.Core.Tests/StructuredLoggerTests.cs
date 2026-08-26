using System.Text.Json;
using ToolBox.Core.Diagnostics;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class StructuredLoggerTests
{
    [Fact]
    public async Task LoggerWritesStructuredJsonLines()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var options = new LoggerOptions
            {
                DirectoryPath = directory,
                MaxFileBytes = 1024 * 1024,
                MaxFiles = 4,
                Retention = TimeSpan.FromDays(1)
            };

            await using (var logger = new StructuredLogger(options, "session-test", "0.1.0"))
            {
                logger.Info("Test", "structured event", "operation-test");
            }

            var file = Assert.Single(Directory.EnumerateFiles(directory, "*.jsonl"));
            var line = (await File.ReadAllLinesAsync(file)).Single();
            using var document = JsonDocument.Parse(line);

            Assert.Equal("session-test", document.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal("operation-test", document.RootElement.GetProperty("operationId").GetString());
            Assert.Equal("Test", document.RootElement.GetProperty("module").GetString());
            Assert.Equal("structured event", document.RootElement.GetProperty("message").GetString());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task LoggerRollsFilesWhenSizeLimitIsReached()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var options = new LoggerOptions
            {
                DirectoryPath = directory,
                MaxFileBytes = 400,
                MaxFiles = 12,
                Retention = TimeSpan.FromDays(1)
            };

            await using (var logger = new StructuredLogger(options, "session-test", "0.1.0"))
            {
                for (var index = 0; index < 20; index++)
                {
                    logger.Info("Test", $"event-{index:00}-rolling-check");
                }
            }

            Assert.True(Directory.EnumerateFiles(directory, "*.jsonl").Count() > 1);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ToolBoxCoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
