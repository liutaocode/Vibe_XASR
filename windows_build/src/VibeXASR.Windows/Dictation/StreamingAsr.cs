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

        // ---- build sherpa-onnx config (mirrors the macOS SherpaASR "verified recipe") ----
        var config = new OnlineRecognizerConfig();

        config.FeatConfig.SampleRate = _sampleRate;
        config.FeatConfig.FeatureDim = 80; // zipformer2 default.

        config.ModelConfig.Transducer.Encoder = paths.Encoder;
        config.ModelConfig.Transducer.Decoder = paths.Decoder;
        config.ModelConfig.Transducer.Joiner  = paths.Joiner;
        config.ModelConfig.Tokens = paths.Tokens;
        config.ModelConfig.ModelType = "zipformer2"; // explicit, matching macOS
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Provider = "cpu"; // "cpu" is safe on both x64 and arm64.
        config.ModelConfig.Debug = 0;

        config.DecodingMethod = "greedy_search";
        config.MaxActivePaths = 4;

        // Endpointing OFF — segmentation is driven by our VAD (OnCall) / the hotkey
        // (push-to-talk), exactly like the macOS engine. Letting sherpa fire its own
        // endpoint would reset the stream mid-utterance and fragment the text.
        config.EnableEndpoint = 0;

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

    /// <summary>Current incremental hypothesis (CJK-normalized, edges trimmed).</summary>
    public string Partial()
    {
        var result = _recognizer.GetResult(_stream);
        return NormalizeCjk(result.Text ?? string.Empty).Trim();
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
        // 1.5 s of trailing zeros + InputFinished so the last zipformer2 chunk is decoded —
        // a soft/short final syllable would otherwise be dropped (was 0.5 s; matches macOS).
        _stream.AcceptWaveform(_sampleRate, new float[_sampleRate * 3 / 2]);
        _stream.InputFinished();
        while (_recognizer.IsReady(_stream))
            _recognizer.Decode(_stream);

        var text = Partial();
        ResetStream();
        // The streaming model only emits a sentence's closing punctuation when it hears the
        // NEXT sentence, so the FINAL sentence never gets one — add it if missing.
        return EnsureFinalPunct(text);
    }

    /// <summary>Start a fresh stream (utterance boundary). A new stream is used rather than
    /// Reset() because the previous one has had InputFinished() called on it.</summary>
    public void ResetStream()
    {
        var old = _stream;
        _stream = _recognizer.CreateStream();
        old?.Dispose();
    }

    // ---- CJK normalization (faithful port of the macOS SherpaASR.normalizeCJK) ----

    private static readonly HashSet<char> CjkPunct =
        new("，。！？；：、（）《》〈〉【】「」『』“”‘’".ToCharArray());
    private static readonly HashSet<char> AsciiPunct = new(",.!?;:%)]}".ToCharArray());

    private static bool IsCjk(char c) =>
        (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF);
    private static bool IsCjkish(char c) => IsCjk(c) || CjkPunct.Contains(c);

    /// <summary>
    /// The zipformer BPE inserts spaces between tokens. Drop a space when (a) the next
    /// non-space char is ASCII punctuation ("word ." → "word."), or (b) both neighbours are
    /// CJK-ish ("你 好" → "你好"). Latin words keep their spaces ("hello 你好" stays readable).
    /// </summary>
    public static string NormalizeCjk(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        var sb = new StringBuilder(chars.Length);
        int i = 0;
        while (i < chars.Length)
        {
            char c = chars[i];
            if (c == ' ')
            {
                char? prev = sb.Length > 0 ? sb[sb.Length - 1] : null;
                int j = i + 1;
                while (j < chars.Length && chars[j] == ' ') j++; // next non-space
                char? next = j < chars.Length ? chars[j] : null;
                bool dropAscii = next.HasValue && AsciiPunct.Contains(next.Value);
                bool dropCjk = prev.HasValue && next.HasValue && IsCjkish(prev.Value) && IsCjkish(next.Value);
                if (dropAscii || dropCjk) { i++; continue; } // drop this space
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Append a closing 。 (CJK) or . (otherwise) when the final text lacks one.</summary>
    public static string EnsureFinalPunct(string text)
    {
        if (text.Length == 0) return text;
        char last = text[^1];
        if (CjkPunct.Contains(last) || AsciiPunct.Contains(last)) return text;
        return text + (IsCjk(last) ? "。" : ".");
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _recognizer?.Dispose();
    }
}
