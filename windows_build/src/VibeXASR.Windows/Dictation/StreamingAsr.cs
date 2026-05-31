using System;
using System.Globalization;
using System.Text;
using VibeXASR.Windows.Models;

// sherpa-onnx C# namespace. TODO(win): confirm the exact namespace/type names against the
// installed org.k2fsa.sherpa.onnx version — historically it is `SherpaOnnx` with types
// OnlineRecognizer, OnlineRecognizerConfig, OnlineStream, OnlineModelConfig,
// OnlineTransducerModelConfig, FeatureConfig, OnlineRecognizerResult.
using SherpaOnnx;

namespace VibeXASR.Windows.Dictation;

/// <summary>
/// Thin wrapper over sherpa-onnx streaming (online) zipformer2 transducer with greedy
/// decoding. 16 kHz mono float in, incremental text out. Includes the CJK de-spacing
/// port from the macOS app (drop spaces between adjacent CJK glyphs).
/// </summary>
public sealed class StreamingAsr : IDisposable
{
    private readonly OnlineRecognizer _recognizer;
    private OnlineStream _stream;
    private readonly int _sampleRate;

    public StreamingAsr(ModelPaths paths, int sampleRate = 16000)
    {
        _sampleRate = sampleRate;

        if (!paths.AsrModelPresent())
            throw new InvalidOperationException(
                $"ASR model files missing under {paths.TierDir}. Run ModelDownloader first.");

        // ---- build sherpa-onnx config ----
        // TODO(win): field names below match sherpa-onnx ~1.10.x. If restore pulls a different
        // major, adjust property names (the library occasionally renames config fields).
        var config = new OnlineRecognizerConfig();

        config.FeatConfig.SampleRate = _sampleRate;
        config.FeatConfig.FeatureDim = 80; // zipformer2 default.

        config.ModelConfig.Transducer.Encoder = paths.Encoder;
        config.ModelConfig.Transducer.Decoder = paths.Decoder;
        config.ModelConfig.Transducer.Joiner  = paths.Joiner;
        config.ModelConfig.Tokens = paths.Tokens;

        // TODO(win): tune to match macOS. Number of CPU threads for ONNX Runtime.
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Provider = "cpu"; // "cpu" is safe on both x64 and arm64.
        config.ModelConfig.Debug = 0;

        // Greedy search, matching the macOS engine.
        config.DecodingMethod = "greedy_search";

        // Endpointing: sherpa can flag an endpoint when speech stops. We mostly drive
        // segmentation from our own VAD in DictationEngine, but enabling this gives the
        // recognizer a sane internal reset boundary. TODO(win): mirror macOS rule timings.
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = 1.2f;
        config.Rule3MinUtteranceLength = 20.0f;

        _recognizer = new OnlineRecognizer(config);
        _stream = _recognizer.CreateStream();
    }

    /// <summary>Feed a frame of 16 kHz mono float samples in [-1, 1].</summary>
    public void AcceptWaveform(float[] samples)
    {
        _stream.AcceptWaveform(_sampleRate, samples);
        while (_recognizer.IsReady(_stream))
            _recognizer.Decode(_stream);
    }

    /// <summary>Current incremental hypothesis (CJK de-spaced, edges trimmed).</summary>
    public string Partial()
    {
        var result = _recognizer.GetResult(_stream);
        return DeSpaceCjk(result.Text ?? string.Empty).Trim();
    }

    /// <summary>
    /// True if sherpa thinks the current utterance has ended (trailing silence).
    /// DictationEngine may use this in addition to the external VAD.
    /// </summary>
    public bool IsEndpoint() => _recognizer.IsEndpoint(_stream);

    /// <summary>
    /// Finalize the current utterance: tell the recognizer no more audio is coming (so it
    /// flushes the trailing tokens — without InputFinished a streaming model leaves the last
    /// chunk undecoded), read the text, then start a fresh stream. Returns de-spaced text.
    /// </summary>
    public string Finalize()
    {
        // Pad with a little trailing silence + signal end so the decoder drains the tail.
        _stream.AcceptWaveform(_sampleRate, new float[_sampleRate / 2]); // 0.5 s of silence
        _stream.InputFinished();
        while (_recognizer.IsReady(_stream))
            _recognizer.Decode(_stream);

        var text = Partial();
        ResetStream();
        return text;
    }

    /// <summary>Start a fresh stream (utterance boundary). A new stream is used rather than
    /// Reset() because the previous one has had InputFinished() called on it.</summary>
    public void ResetStream()
    {
        var old = _stream;
        _stream = _recognizer.CreateStream();
        old?.Dispose();
    }

    // ---- CJK de-spacing (port of the macOS post-processor) ----

    /// <summary>
    /// The transducer emits a space between most tokens. For CJK text that produces
    /// "你 好 世 界"; we want "你好世界". Rule (matching macOS): remove a space when BOTH
    /// of its neighbours are CJK; keep the space when either side is a Latin/digit token
    /// (so "hello 你好" and "数字 123" stay readable).
    /// </summary>
    public static string DeSpaceCjk(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);

        // We need lookbehind/lookahead around spaces; collect into a list of runes.
        var list = new System.Collections.Generic.List<Rune>(text.Length);
        foreach (var r in text.EnumerateRunes()) list.Add(r);

        for (int i = 0; i < list.Count; i++)
        {
            var cur = list[i];
            if (cur.Value == ' ')
            {
                // Look at previous non-? and next rune.
                bool prevCjk = i > 0 && IsCjk(list[i - 1]);
                bool nextCjk = i + 1 < list.Count && IsCjk(list[i + 1]);
                if (prevCjk && nextCjk)
                    continue; // drop this space
            }
            sb.Append(cur.ToString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Is this rune a CJK ideograph / kana / Hangul / fullwidth punctuation that should
    /// abut its neighbours without a space?
    /// </summary>
    private static bool IsCjk(Rune r)
    {
        int c = r.Value;
        return
            (c >= 0x4E00 && c <= 0x9FFF)   || // CJK Unified Ideographs
            (c >= 0x3400 && c <= 0x4DBF)   || // CJK Ext A
            (c >= 0x20000 && c <= 0x2A6DF) || // CJK Ext B
            (c >= 0x3040 && c <= 0x30FF)   || // Hiragana + Katakana
            (c >= 0xAC00 && c <= 0xD7A3)   || // Hangul syllables
            (c >= 0x3000 && c <= 0x303F)   || // CJK symbols & punctuation
            (c >= 0xFF00 && c <= 0xFFEF);     // Halfwidth/Fullwidth forms
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _recognizer?.Dispose();
    }
}
