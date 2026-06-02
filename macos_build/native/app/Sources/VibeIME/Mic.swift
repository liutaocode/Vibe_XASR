import AVFoundation
import AudioToolbox

/// Captures an input device and delivers 16 kHz mono float32 samples. Uses the
/// system default unless `preferredDeviceUID` names a specific microphone.
final class Mic {
    private let engine = AVAudioEngine()
    private var converter: AVAudioConverter?
    private let target = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                       sampleRate: 16000, channels: 1, interleaved: false)!
    /// Called on the audio thread with a chunk of 16 kHz mono samples.
    var onSamples: (([Float]) -> Void)?
    /// Preferred input device UID ("" / nil = system default). Applied at start().
    var preferredDeviceUID: String?

    func start() throws {
        let input = engine.inputNode
        applyPreferredDevice(to: input)
        let inFormat = input.inputFormat(forBus: 0)
        converter = AVAudioConverter(from: inFormat, to: target)
        input.installTap(onBus: 0, bufferSize: 1600, format: inFormat) { [weak self] buf, _ in
            self?.handle(buf)
        }
        engine.prepare()
        try engine.start()
    }

    func stop() {
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
    }

    /// Point the input node's AUHAL at the chosen device (macOS). No-op for the
    /// system default or if the device is gone.
    private func applyPreferredDevice(to input: AVAudioInputNode) {
        guard let uid = preferredDeviceUID, !uid.isEmpty,
              let devID = AudioDevices.deviceID(forUID: uid),
              let au = input.audioUnit else { return }
        var d = devID
        AudioUnitSetProperty(au, kAudioOutputUnitProperty_CurrentDevice,
                             kAudioUnitScope_Global, 0, &d,
                             UInt32(MemoryLayout<AudioDeviceID>.size))
    }

    private func handle(_ buffer: AVAudioPCMBuffer) {
        guard let converter else { return }
        let ratio = target.sampleRate / buffer.format.sampleRate
        let cap = AVAudioFrameCount(Double(buffer.frameLength) * ratio + 32)
        guard let out = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: cap) else { return }
        var err: NSError?
        var supplied = false
        let status = converter.convert(to: out, error: &err) { _, outStatus in
            if supplied { outStatus.pointee = .noDataNow; return nil }
            supplied = true; outStatus.pointee = .haveData; return buffer
        }
        guard status == .haveData || status == .inputRanDry,
              let ch = out.floatChannelData, out.frameLength > 0 else { return }
        onSamples?(Array(UnsafeBufferPointer(start: ch[0], count: Int(out.frameLength))))
    }
}
