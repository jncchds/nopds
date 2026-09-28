using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nopds.Conversion.Converters;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Infrastructure.Settings;

namespace Nopds.Conversion;

public sealed record ConvertedFile(string Path, string Format, string MediaType);

/// <summary>
/// Converts books to other formats on demand. Built-in converters turn FB2, DOCX, ODT, RTF, TXT and HTML
/// into EPUB; configured external tools add more pairs. Conversions chain (e.g. DOCX → EPUB → AZW3)
/// along the shortest route. Results are cached on disk and evicted least-recently-used.
/// </summary>
public sealed class ConversionService
{
    /// <summary>Longest chain of conversion steps considered.</summary>
    private const int MaxSteps = 3;

    public static readonly IReadOnlyList<IBookConverter> BuiltIns =
    [
        new Fb2ToEpubConverter(),
        new DocxToEpubConverter(),
        new OdtToEpubConverter(),
        new RtfToEpubConverter(),
        new TxtToEpubConverter(),
        new HtmlToEpubConverter(),
    ];

    private readonly string _dir;
    private readonly Func<ConversionSettings> _settings;
    private readonly ILogger<ConversionService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _parallel = new(Math.Max(1, Environment.ProcessorCount / 2));
    private int _evicting;

    public ConversionService(string cacheDir, SettingsStore settings, ILogger<ConversionService> log)
        : this(cacheDir, () => settings.Current.Conversion, log)
    {
    }

    public ConversionService(string cacheDir, Func<ConversionSettings> settings, ILogger<ConversionService> log)
    {
        _dir = Path.Combine(cacheDir, "convert");
        Directory.CreateDirectory(_dir);
        _settings = settings;
        _log = log;
    }

    private sealed record Step(string From, string To, IBookConverter? BuiltIn, ExternalConverter? External);

    /// <summary>Available single steps; built-ins first so they win over external tools at equal length.</summary>
    private List<Step> Steps()
    {
        var conv = _settings();
        var steps = new List<Step>();
        if (conv.BuiltIn)
        {
            foreach (var c in BuiltIns)
            {
                steps.AddRange(c.Sources.Select(s => new Step(s, c.Target, c, null)));
            }
        }

        foreach (var ext in conv.External.Where(e => !string.IsNullOrWhiteSpace(e.Command) && !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target)))
        {
            steps.Add(new Step(ext.Source.Trim().ToLowerInvariant(), ext.Target.Trim().ToLowerInvariant(), null, ext));
        }

        return steps;
    }

    /// <summary>Breadth-first search from <paramref name="source"/>: the shortest route to every reachable format.</summary>
    private Dictionary<string, List<Step>> Routes(string source)
    {
        source = source.ToLowerInvariant();
        var steps = Steps();
        var routes = new Dictionary<string, List<Step>>(StringComparer.Ordinal) { [source] = [] };
        var frontier = new List<string> { source };
        for (var depth = 0; depth < MaxSteps && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var from in frontier)
            {
                foreach (var step in steps.Where(s => s.From == from && !routes.ContainsKey(s.To)))
                {
                    routes[step.To] = [.. routes[from], step];
                    next.Add(step.To);
                }
            }

            frontier = next;
        }

        routes.Remove(source);
        return routes;
    }

    /// <summary>Target formats available for a source format (excluding the source itself), nearest first.</summary>
    public IReadOnlyList<string> TargetsFor(string format) =>
        Routes(format).OrderBy(r => r.Value.Count).Select(r => r.Key).ToList();

    public bool CanConvert(string source, string target) => Routes(source).ContainsKey(target.ToLowerInvariant());

    /// <summary>Source formats that can be turned into <paramref name="target"/>.</summary>
    public IReadOnlyList<string> SourcesFor(string target)
    {
        target = target.ToLowerInvariant();
        return Steps().Select(s => s.From).Distinct().Where(f => f != target && CanConvert(f, target)).Order().ToList();
    }

    public async Task<ConvertedFile?> ConvertAsync(Library library, Book book, string target, CancellationToken ct = default)
    {
        target = target.ToLowerInvariant();
        if (!Routes(book.Format).TryGetValue(target, out var route))
        {
            return null;
        }

        var key = $"{book.Id}_{book.FileSize}.{target}";
        var path = Path.Combine(_dir, key);
        if (File.Exists(path))
        {
            Touch(path);
            return new ConvertedFile(path, target, MediaTypes.ForFormat(target));
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                return new ConvertedFile(path, target, MediaTypes.ForFormat(target));
            }

            await _parallel.WaitAsync(ct);
            try
            {
                await using var source = BookStorage.Open(library, book);
                if (source is null)
                {
                    return null;
                }

                using var ms = new MemoryStream();
                await source.CopyToAsync(ms, ct);
                var tmp = path + ".tmp";
                var ok = await RunRouteAsync(ms.ToArray(), CatalogMetadata(book), route, tmp, ct);
                if (!ok || !File.Exists(tmp))
                {
                    return null;
                }

                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                _parallel.Release();
            }
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(key, out _);
        }

        _ = Task.Run(EvictAsync, CancellationToken.None);
        return new ConvertedFile(path, target, MediaTypes.ForFormat(target));
    }

    /// <summary>What the catalog knows about the book, for converters of formats without metadata of their own.</summary>
    private static BookMetadata CatalogMetadata(Book book)
    {
        var meta = new BookMetadata { Title = book.Title, Lang = book.Lang, Annotation = book.Annotation, DocDate = book.DocDate };
        meta.Authors.AddRange(book.Authors.OrderBy(a => a.Position).Select(a => a.Author?.FullName).OfType<string>());
        meta.Series.AddRange(book.Series.Where(s => s.Series is not null).Select(s => new SeriesRef(s.Series!.Name, s.SerNo)));
        meta.Genres.AddRange(book.Genres.Where(g => g.Genre is not null).Select(g => g.Genre!.Code));
        return meta;
    }

    private async Task<bool> RunRouteAsync(byte[] bytes, BookMetadata meta, List<Step> route, string outPath, CancellationToken ct)
    {
        var timeout = _settings().TimeoutSeconds;
        for (var i = 0; i < route.Count; i++)
        {
            var step = route[i];
            var last = i == route.Count - 1;
            var stepOut = last ? outPath : outPath + $".step{i}";
            try
            {
                if (step.BuiltIn is { } builtIn)
                {
                    await Task.Run(() =>
                    {
                        using var fs = File.Create(stepOut);
                        builtIn.Convert(bytes, meta, fs);
                    }, ct);
                }
                else if (!await RunExternalAsync(step.External!, bytes, step.From, stepOut, timeout, ct))
                {
                    return false;
                }

                if (!last)
                {
                    bytes = await File.ReadAllBytesAsync(stepOut, ct);
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or System.Xml.XmlException or IOException)
            {
                _log.LogWarning(ex, "Conversion {From} → {To} failed", step.From, step.To);
                TryDelete(stepOut);
                return false;
            }
            finally
            {
                if (!last)
                {
                    TryDelete(stepOut);
                }
            }
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Runs "command {input} {output}" with placeholders replaced by temp paths. Arguments are passed
    /// as an argument list (no shell), so book names cannot inject commands.
    /// </summary>
    private async Task<bool> RunExternalAsync(ExternalConverter conv, byte[] bytes, string sourceFormat, string outPath, int timeoutSeconds, CancellationToken ct)
    {
        var work = Path.Combine(_dir, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var input = Path.Combine(work, "book." + sourceFormat);
            var output = Path.Combine(work, "book." + conv.Target.ToLowerInvariant());
            await File.WriteAllBytesAsync(input, bytes, ct);

            var parts = SplitCommand(conv.Command);
            if (parts.Count == 0)
            {
                return false;
            }

            var psi = new ProcessStartInfo(parts[0])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = work,
            };
            foreach (var arg in parts.Skip(1))
            {
                psi.ArgumentList.Add(arg.Replace("{input}", input).Replace("{output}", output).Replace("{outdir}", work));
            }

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start " + parts[0]);
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                proc.Kill(entireProcessTree: true);
                _log.LogWarning("Converter {Command} timed out", parts[0]);
                return false;
            }

            if (proc.ExitCode != 0)
            {
                _log.LogWarning("Converter {Command} exited with {Code}: {Error}", parts[0], proc.ExitCode, (await stderr).Trim());
            }

            _ = await stdout;
            var produced = File.Exists(output)
                ? output
                : Directory.EnumerateFiles(work, "*." + conv.Target.ToLowerInvariant()).FirstOrDefault(f => f != input);
            if (produced is null)
            {
                return false;
            }

            File.Move(produced, outPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            _log.LogWarning(ex, "External conversion failed");
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static List<string> SplitCommand(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var ch in command)
        {
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
    }

    private Task EvictAsync()
    {
        if (Interlocked.Exchange(ref _evicting, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            var limit = (long)_settings().CacheSizeMb * 1024 * 1024;
            var files = new DirectoryInfo(_dir).EnumerateFiles().Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal)).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= limit)
                {
                    break;
                }

                total -= f.Length;
                f.Delete();
            }
        }
        catch (IOException ex)
        {
            _log.LogDebug(ex, "Conversion cache eviction failed");
        }
        finally
        {
            Volatile.Write(ref _evicting, 0);
        }

        return Task.CompletedTask;
    }
}
