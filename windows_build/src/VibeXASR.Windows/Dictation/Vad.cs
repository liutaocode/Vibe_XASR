using System;
using VibeXASR.Windows.Models;
using VibeXASR.Windows.Storage;

// TODO(win): confirm sherpa-onnx VAD types: VoiceActivityDetector, VadModelConfig,
// SileroVadModelConfig, CircularBuffer, SpeechSegment.
using SherpaOnnx;

namespace VibeXASR.Windows.Dictation;

/// <summary>
/// Wraps sherpa-onnx VoiceActivityDetector. Fed the same 16 kHz mono float frames as the
/// ASR; exposes a simple "is there speech right now" plus drained completed segments.
/// </summary>
public sealed class Vad : IDisposable
{
    private readonly VoiceActivityDetector _vad;
    private readonly int _sampleRate;

    public Vad(ModelPaths paths, VadKind kind, int sampleRate = 16000)
    {
        _sampleRate = sampleRate;

        var vadPath = paths.VadFileFor(kind);
        if (!paths.VadPresent(kind))
            throw new InvalidOperationException($"VAD model missing: {vadPath}");

        // TODO(win): FireRed VAD may need a different config struct than Silero. The skeleton
        // wires Silero; for FireRed, swap to the appropriate sherpa-onnx config once verified.
        var config = new VadModelConfig();
        config.SileroVad.Model = vadPath;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = 0.25f; // seconds of silence to end a segment.
        config.SileroVad.MinSpeechDuration = 0.10f;
        config.SileroVad.WindowSize = 512;           // Silero v5 frame size at 16 kHz.
        config.SampleRate = _sampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";

        // bufferSizeInSeconds: ring buffer the detector keeps internally.
        _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 30f);
    }

    /// <summary>Feed a frame. The detector accumulates and segments internally.</summary>
    public void AcceptWaveform(float[] samples) => _vad.AcceptWaveform(samples);

    /// <summary>True while the detector currently believes speech is ongoing.</summary>
    public bool IsSpeechDetected() => _vad.IsSpeechDetected();

    /// <summary>True if at least one completed speech segment is queued.</summary>
    public bool HasSegment() => !_vad.IsEmpty();

    /// <summary>
    /// Pop the oldest completed speech segment (16 kHz mono float). Call while
    /// <see cref="HasSegment"/> is true. Returns null if the queue is empty.
    /// </summary>
    public float[]? PopSegment()
    {
        if (_vad.IsEmpty()) return null;
        var seg = _vad.Front();   // SpeechSegment { Samples, Start }
        _vad.Pop();
        return seg.Samples;
    }

    /// <summary>Flush any buffered audio as a final segment (e.g. on hotkey release).</summary>
    public void Flush() => _vad.Flush();

    public void Reset() => _vad.Reset();

    public void Dispose() => _vad?.Dispose();
}
