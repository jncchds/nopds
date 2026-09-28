using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Infrastructure.Settings;

namespace Nopds.Conversion;

public sealed record ConvertedFile(string Path, string Format, string MediaType);

/// <summary>
/// Converts books to other formats on demand. FB2→EPUB uses the built-in converter; other pairs use
/// configured external tools. Results are cached on disk and evicted least-recently-used.
/// </summary>
public sealed class ConversionService
{
    private readonly string _dir;
    private readonly SettingsStore _settings;
    private readonly ILogger<ConversionService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _parallel = new(Math.Max(1, Environment.ProcessorCount / 2));
    private int _evicting;

    public ConversionService(string cacheDir, SettingsStore settings, ILogger<ConversionService> log)
    {
        _dir = Path.Combine(cacheDir, "convert");
        Directory.CreateDirectory(_dir);
        _settings = settings;
        _log = log;
    }

    /// <summary>Target formats available for a source format (excluding the source itself).</summary>
    public IReadOnlyList<string> TargetsFor(string format)
    {
        var conv = _settings.Current.Conversion;
        var targets = new List<string>();
        if (format.Equals("fb2", StringComparison.OrdinalIgnoreCase) && conv.BuiltInFb2ToEpub)
        {
            targets.Add("epub");
        }

        foreach (var ext in conv.External.Where(e => e.Source.Equals(format, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Command)))
        {
            var t = ext.Target.ToLowerInvariant();
            if (!targets.Contains(t) && t != format)
            {
                targets.Add(t);
            }
        }

        // Two-step chains through EPUB (e.g. fb2 → epub → kepub/azw3).
        if (targets.Contains("epub"))
        {
            foreach (var ext in conv.External.Where(e => e.Source.Equals("epub", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Command)))
            {
                var t = ext.Target.ToLowerInvariant();
                if (!targets.Contains(t) && t != format)
                {
                    targets.Add(t);
                }
            }
        }

        return targets;
    }

    public async Task<ConvertedFile?> ConvertAsync(Library library, Book book, string target, CancellationToken ct = default)
    {
        target = target.ToLowerInvariant();
        if (!TargetsFor(book.Format).Contains(target))
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
                var bytes = ms.ToArray();
                var tmp = path + ".tmp";
                var ok = await ConvertBytesAsync(bytes, book.Format.ToLowerInvariant(), target, tmp, ct);
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

    private async Task<bool> ConvertBytesAsync(byte[] bytes, string source, string target, string outPath, CancellationToken ct)
    {
        var conv = _settings.Current.Conversion;
        if (source == "fb2" && target == "epub" && conv.BuiltInFb2ToEpub)
        {
            await using var fs = File.Create(outPath);
            new Fb2ToEpubConverter().Convert(bytes, fs);
            return true;
        }

        var direct = conv.External.FirstOrDefault(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && e.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return await RunExternalAsync(direct, bytes, source, outPath, conv.TimeoutSeconds, ct);
        }

        if (source == "fb2" && conv.BuiltInFb2ToEpub)
        {
            var viaEpub = conv.External.FirstOrDefault(e => e.Source.Equals("epub", StringComparison.OrdinalIgnoreCase) && e.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (viaEpub is not null)
            {
                using var epub = new MemoryStream();
                new Fb2ToEpubConverter().Convert(bytes, epub);
                return await RunExternalAsync(viaEpub, epub.ToArray(), "epub", outPath, conv.TimeoutSeconds, ct);
            }
        }

        return false;
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
            var limit = (long)_settings.Current.Conversion.CacheSizeMb * 1024 * 1024;
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
