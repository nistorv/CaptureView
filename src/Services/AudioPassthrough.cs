using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CaptureView.Services;

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Plays a WASAPI capture device (line-in, HDMI audio, ...) straight to an output device,
/// keeping the buffered latency capped instead of letting clock drift build it up.
/// </summary>
public sealed class AudioPassthrough : IDisposable
{
    private static readonly TimeSpan MaxBufferedLatency = TimeSpan.FromMilliseconds(80);
    private const int CaptureBufferMs = 10;
    private const int OutputLatencyMs = 30;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 1f;
    private bool _muted;

    /// <summary>Raised (possibly from a background thread) when capture or playback stops with an error.</summary>
    public event EventHandler<string>? Failed;

    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; ApplyVolume(); }
    }

    public static IReadOnlyList<AudioDevice> GetInputDevices() => GetDevices(DataFlow.Capture);

    public static IReadOnlyList<AudioDevice> GetOutputDevices() => GetDevices(DataFlow.Render);

    private static List<AudioDevice> GetDevices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName))
            .ToList();
        foreach (var d in devices)
            Debug.WriteLine($"Audio {flow} device: {d.Name} ({d.Id})");
        return devices;
    }

    /// <param name="inputId">Capture endpoint id.</param>
    /// <param name="outputId">Render endpoint id, or null for the default output.</param>
    public void Start(string inputId, string? outputId)
    {
        Stop();

        using var enumerator = new MMDeviceEnumerator();
        var input = enumerator.GetDevice(inputId);
        var output = outputId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(outputId);

        var capture = new WasapiCapture(input, true, CaptureBufferMs);
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        Debug.WriteLine($"Audio input format: {capture.WaveFormat}");

        capture.DataAvailable += (_, e) =>
        {
            // Input and output run on different clocks; drop the backlog rather than drifting into lag.
            if (buffer.BufferedDuration > MaxBufferedLatency)
                buffer.ClearBuffer();
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                Failed?.Invoke(this, $"Audio input stopped: {e.Exception.Message}");
        };

        _volumeProvider = new VolumeSampleProvider(buffer.ToSampleProvider());
        ApplyVolume();

        var player = new WasapiOut(output, AudioClientShareMode.Shared, true, OutputLatencyMs);
        player.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
                Failed?.Invoke(this, $"Audio output stopped: {e.Exception.Message}");
        };

        try
        {
            player.Init(_volumeProvider);
            capture.StartRecording();
            player.Play();
        }
        catch
        {
            capture.Dispose();
            player.Dispose();
            _volumeProvider = null;
            throw;
        }

        _capture = capture;
        _output = player;
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _volumeProvider = null;
    }

    private void ApplyVolume()
    {
        if (_volumeProvider is not null)
            _volumeProvider.Volume = _muted ? 0f : _volume;
    }

    public void Dispose() => Stop();
}
