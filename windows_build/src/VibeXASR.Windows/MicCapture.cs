using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VibeXASR.Windows;

/// <summary>
/// Captures the default microphone via WASAPI (shared mode) and delivers 16 kHz mono
/// float frames to <see cref="FrameAvailable"/>. NAudio gives us device-native format
/// (often 44.1/48 kHz, stereo); we down-mix to mono and resample to 16 kHz.
/// </summary>
public sealed class MicCapture : IDisposable
{
    private const int TargetRate = 16000;

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampledMono;

    /// <summary>Raised with a 16 kHz mono float frame (length depends on capture buffer).</summary>
    public event EventHandler<float[]>? FrameAvailable;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;

        // Default capture (microphone) endpoint.
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        // TODO(win): WasapiCapture default is shared-mode, event-driven. Confirm the device's
        // WaveFormat (sample rate, channels, IeeeFloat vs PCM) on your hardware; the resample
        // chain below assumes we can get an ISampleProvider from whatever it produces.
        _capture = new WasapiCapture(device)
        {
            // Lower latency = more frequent, smaller frames -> snappier partials.
            // TODO(win): tune; 30 ms is a reasonable streaming-ASR frame.
        };

        var srcFormat = _capture.WaveFormat;
        _buffer = new BufferedWaveProvider(srcFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };

        // Build: source -> sample provider -> mono -> 16 kHz resample.
        ISampleProvider sample = _buffer.ToSampleProvider();
        if (srcFormat.Channels > 1)
            sample = sample.ToMono(); // down-mix
        // WdlResamplingSampleProvider ships with NAudio and works cross-arch.
        _resampledMono = new WdlResamplingSampleProvider(sample, TargetRate);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _capture.StartRecording();
        IsRunning = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_buffer is null || _resampledMono is null) return;

        // Push raw bytes into the buffer, then pull resampled mono floats out.
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Read everything currently available from the resample chain.
        // Frame size: read in fixed chunks for steady cadence.
        // TODO(win): pick a chunk size aligned with the VAD window (Silero v5 = 512 @16k).
        const int chunk = 512;
        var floats = new float[chunk];
        int read;
        while ((read = _resampledMono.Read(floats, 0, chunk)) > 0)
        {
            if (read == chunk)
            {
                FrameAvailable?.Invoke(this, (float[])floats.Clone());
            }
            else
            {
                var tail = new float[read];
                Array.Copy(floats, tail, read);
                FrameAvailable?.Invoke(this, tail);
                break; // partial read => buffer drained
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // TODO(win): surface e.Exception (device unplugged etc.) to the tray as a balloon tip.
        IsRunning = false;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _capture?.StopRecording();
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
    }
}
