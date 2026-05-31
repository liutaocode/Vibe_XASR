import Foundation

/// Streaming ASR backed by the sherpa-onnx streaming zipformer2 transducer
/// (wraps `SherpaOnnxRecognizer`). Mirrors the verified native xasr_stream.cc
/// recipe exactly: model_type "zipformer2", greedy_search, numThreads 2,
/// provider "cpu", enableEndpoint false; finalize by appending 1.0 s of zeros,
/// inputFinished, drain, getResult, reset.
///
/// `asrDir` must contain encoder/decoder/joiner-<tier>ms.onnx + tokens.txt.
/// `tier` is the streaming chunk size in ms ("160"/"480"/"960"/"1920"); the
/// bundled model is "960". The model files are named with the tier suffix.
final class SherpaASR: StreamingASR {
    private let recognizer: SherpaOnnxRecognizer
    private let sampleRate = 16000

    init(asrDir: String, tier: String = "960") {
        let encoder = asrDir + "/encoder-\(tier)ms.onnx"
        let decoder = asrDir + "/decoder-\(tier)ms.onnx"
        let joiner  = asrDir + "/joiner-\(tier)ms.onnx"
        let tokens  = asrDir + "/tokens.txt"

        let transducer = sherpaOnnxOnlineTransducerModelConfig(
            encoder: encoder,
            decoder: decoder,
            joiner: joiner
        )
        let modelConfig = sherpaOnnxOnlineModelConfig(
            tokens: tokens,
            transducer: transducer,
            numThreads: 2,
            provider: "cpu",
            debug: 0,
            modelType: "zipformer2"
        )
        let featConfig = sherpaOnnxFeatureConfig(sampleRate: 16000, featureDim: 80)
        var config = sherpaOnnxOnlineRecognizerConfig(
            featConfig: featConfig,
            modelConfig: modelConfig,
            enableEndpoint: false,
            decodingMethod: "greedy_search",
            maxActivePaths: 4
        )
        self.recognizer = SherpaOnnxRecognizer(config: &config)
    }

    /// Start a fresh sentence with a brand-new stream (Python parity — avoids the
    /// previous sentence's trailing punctuation leaking into this one).
    func startSentence() {
        recognizer.newStream()
    }

    // MARK: CJK de-spacing (port of the Python normalize_cjk)
    // sherpa's zipformer BPE inserts spaces between tokens; drop the spaces that
    // sit between two CJK glyphs/punct, and any space directly before ASCII punct.
    private static let cjkPunct = Set("，。！？；：、（）《》〈〉【】「」『』“”‘’")
    private static let asciiPunct = Set(",.!?;:%)]}")

    private static func isCJK(_ c: Character) -> Bool {
        guard c.unicodeScalars.count == 1, let v = c.unicodeScalars.first?.value else { return false }
        return (0x3400...0x4DBF).contains(v) || (0x4E00...0x9FFF).contains(v) || (0xF900...0xFAFF).contains(v)
    }
    private static func isCJKish(_ c: Character) -> Bool { isCJK(c) || cjkPunct.contains(c) }

    static func normalizeCJK(_ text: String) -> String {
        let chars = Array(text)
        var out: [Character] = []
        out.reserveCapacity(chars.count)
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if c == " " {
                let prev = out.last
                var j = i + 1
                while j < chars.count && chars[j] == " " { j += 1 }   // next non-space
                let next: Character? = j < chars.count ? chars[j] : nil
                let dropAscii = next != nil && asciiPunct.contains(next!)
                let dropCJK = prev != nil && next != nil && isCJKish(prev!) && isCJKish(next!)
                if dropAscii || dropCJK { i += 1; continue }          // drop this space
            }
            out.append(c)
            i += 1
        }
        return String(out)
    }

    /// Feed a chunk of 16 kHz float [-1, 1] and drain any ready decode work so
    /// the partial text stays current.
    func accept(_ samples16k: [Float]) {
        recognizer.acceptWaveform(samples: samples16k, sampleRate: sampleRate)
        while recognizer.isReady() {
            recognizer.decode()
        }
    }

    /// Current decoded text (CJK de-spaced).
    func partialText() -> String {
        SherpaASR.normalizeCJK(recognizer.getResult().text)
    }

    /// Finalize: pad 1.0 s of trailing zeros to flush the last zipformer2 chunk,
    /// signal input-finished, drain, read the final text, then reset for reuse.
    func finalizeSentence() -> String {
        let tail = [Float](repeating: 0, count: sampleRate)   // 1.0 s of zeros
        recognizer.acceptWaveform(samples: tail, sampleRate: sampleRate)
        recognizer.inputFinished()
        while recognizer.isReady() {
            recognizer.decode()
        }
        let text = SherpaASR.normalizeCJK(recognizer.getResult().text)
        recognizer.newStream()
        return text
    }
}
