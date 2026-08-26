using System.Text;
using System.Text.Json;

namespace ToolBox.Core.Diagnostics;

internal sealed class RollingJsonLogWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly LoggerOptions _options;
    private StreamWriter? _writer;
    private DateTime _activeDate;
    private int _activeSequence;
    private bool _disposed;

    public RollingJsonLogWriter(LoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.MaxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFileBytes must be positive.");
        }

        if (_options.MaxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFiles must be positive.");
        }
    }

    public void Write(LogEvent entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        var byteCount = Encoding.UTF8.GetByteCount(line);

        EnsureWriter(byteCount);
        _writer!.Write(line);
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseWriter();
    }

    private void EnsureWriter(int nextEntryBytes)
    {
        var now = DateTime.Now;
        var shouldRotate = _writer is null
            || _activeDate.Date != now.Date
            || _writer.BaseStream.Length + nextEntryBytes > _options.MaxFileBytes;

        if (!shouldRotate)
        {
            return;
        }

        var sequence = _writer is not null && _activeDate.Date == now.Date
            ? _activeSequence + 1
            : 1;

        CloseWriter();
        Directory.CreateDirectory(_options.DirectoryPath);

        string path;
        while (true)
        {
            path = BuildPath(now.Date, sequence);

            if (!File.Exists(path) || new FileInfo(path).Length + nextEntryBytes <= _options.MaxFileBytes)
            {
                break;
            }

            sequence++;
        }

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: false);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
        _activeDate = now.Date;
        _activeSequence = sequence;

        TrimFiles();
    }

    private string BuildPath(DateTime date, int sequence)
    {
        return Path.Combine(_options.DirectoryPath, $"toolbox-{date:yyyyMMdd}-{sequence:D3}.jsonl");
    }

    private void TrimFiles()
    {
        var cutoff = DateTime.UtcNow - _options.Retention;
        var activePath = _writer?.BaseStream is FileStream activeStream
            ? Path.GetFullPath(activeStream.Name)
            : null;
        var files = Directory
            .EnumerateFiles(_options.DirectoryPath, "toolbox-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (var file in files.Skip(_options.MaxFiles))
        {
            TryDelete(file);
        }

        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc < cutoff && !string.Equals(file.FullName, activePath, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(file);
            }
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException)
        {
            // Retention is best effort; a locked file must not take down the Host.
        }
        catch (UnauthorizedAccessException)
        {
            // Retention is best effort; a protected file must not take down the Host.
        }
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }
}
