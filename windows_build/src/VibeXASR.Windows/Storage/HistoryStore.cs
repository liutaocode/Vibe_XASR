using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VibeXASR.Windows.Storage;

/// <summary>One recognized utterance with a timestamp.</summary>
public sealed record HistoryEntry(DateTimeOffset Timestamp, string Text);

/// <summary>
/// Append-only local dictation history persisted as JSON in
/// %APPDATA%/VibeXASR/history.json. Loaded fully into memory (the file is small);
/// each append rewrites the whole array (atomic write-then-rename).
/// </summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly List<HistoryEntry> _entries = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static string FilePath =>
        Path.Combine(AppPaths.DataDir, "history.json");

    public HistoryStore() => Reload();

    public void Reload()
    {
        lock (_gate)
        {
            _entries.Clear();
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOpts);
                    if (loaded is not null) _entries.AddRange(loaded);
                }
            }
            catch
            {
                // Ignore corrupt history; start empty.
            }
        }
    }

    /// <summary>Append a finalized utterance. Empty/whitespace text is ignored.</summary>
    public void Append(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_gate)
        {
            _entries.Add(new HistoryEntry(DateTimeOffset.Now, text.Trim()));
            Persist();
        }
    }

    /// <summary>Most-recent-first snapshot.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        lock (_gate)
        {
            return _entries.OrderByDescending(e => e.Timestamp).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Persist();
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var json = JsonSerializer.Serialize(_entries, JsonOpts);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}
