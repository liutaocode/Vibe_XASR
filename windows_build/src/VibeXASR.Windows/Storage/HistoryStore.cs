using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VibeXASR.Windows.Storage;

/// <summary>
/// One recognized utterance. Mirrors the macOS <c>HistoryItem</c>: stable id, a
/// timestamp, the text, the dictation mode that produced it ("paste"/"type"/"oncall"),
/// and an optional <see cref="ExpiresAt"/> for ephemeral records kept only ~60 s when
/// "save history" is off.
/// </summary>
public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Text { get; set; } = string.Empty;
    public string Mode { get; set; } = "paste";
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>On-disk shape: cumulative stats + the entry list (object form).</summary>
internal sealed class HistoryFile
{
    public long LifetimeChars { get; set; }
    public List<HistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// Local dictation history persisted as JSON in %APPDATA%/VibeXASR/history.json.
/// Loaded fully into memory (small); each mutation rewrites the whole file
/// (atomic write-then-rename). Tracks a cumulative character count that survives
/// per-row deletion (only "Clear all" resets it), matching the macOS History stats.
/// </summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly List<HistoryEntry> _entries = new();
    private long _lifetimeChars;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(AppPaths.DataDir, "history.json");

    /// <summary>Cumulative characters dictated (survives row deletion; reset by ClearAll).</summary>
    public long LifetimeChars { get { lock (_gate) return _lifetimeChars; } }

    public HistoryStore() => Reload();

    public void Reload()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lifetimeChars = 0;
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var file = TryParse(json);
                    if (file is not null)
                    {
                        _entries.AddRange(file.Entries);
                        _lifetimeChars = file.LifetimeChars;
                    }
                }
            }
            catch { /* corrupt history -> start empty */ }
            PruneExpired();
        }
    }

    /// <summary>Parse the object form; fall back to a bare array (legacy skeleton format).</summary>
    private static HistoryFile? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<HistoryFile>(json, JsonOpts); }
        catch { }
        try
        {
            var arr = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOpts);
            if (arr is not null)
                return new HistoryFile { Entries = arr, LifetimeChars = arr.Sum(e => (long)e.Text.Length) };
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Append a finalized utterance. <paramref name="ephemeral"/> records (history off)
    /// get a 60 s <see cref="HistoryEntry.ExpiresAt"/> and are pruned automatically.
    /// Always advances the cumulative <see cref="LifetimeChars"/>.
    /// </summary>
    public HistoryEntry? Append(string text, string mode = "paste", bool ephemeral = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        lock (_gate)
        {
            var entry = new HistoryEntry
            {
                Text = trimmed,
                Mode = mode,
                ExpiresAt = ephemeral ? DateTimeOffset.Now.AddSeconds(60) : null,
            };
            _entries.Add(entry);
            _lifetimeChars += trimmed.Length;
            Persist();
            return entry;
        }
    }

    /// <summary>Most-recent-first snapshot, expired records already dropped.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        lock (_gate)
        {
            PruneExpired();
            return _entries.OrderByDescending(e => e.Timestamp).ToList();
        }
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) > 0) Persist();
        }
    }

    public void Update(Guid id, string text)
    {
        lock (_gate)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e is not null) { e.Text = text.Trim(); Persist(); }
        }
    }

    /// <summary>Clear all rows AND reset the cumulative stats (destructive).</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lifetimeChars = 0;
            Persist();
        }
    }

    /// <summary>Drop any records whose ephemeral lifetime elapsed. Caller holds the lock.</summary>
    private void PruneExpired()
    {
        var now = DateTimeOffset.Now;
        _entries.RemoveAll(e => e.ExpiresAt is { } exp && exp <= now);
    }

    private void Persist()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var file = new HistoryFile { LifetimeChars = _lifetimeChars, Entries = _entries };
        var json = JsonSerializer.Serialize(file, JsonOpts);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}
