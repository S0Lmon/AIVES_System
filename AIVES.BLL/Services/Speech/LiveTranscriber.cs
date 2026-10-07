using AIVES.DTO;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Turns a stream of microphone audio into text while the candidate is still speaking.
/// Audio is cut into windows at quiet points; each window is transcribed once for good
/// ("committed"), and only the unconfirmed tail is re-read for the live preview. That keeps
/// every Whisper call short however long the answer runs, and leaves only the last few seconds
/// to transcribe when the candidate stops.
/// </summary>
public sealed class LiveTranscriber(ISpeechToText speech, SpeechOptions options, AppLanguage language, string? prompt, int maxSeconds)
{
    private const int FrameSamples = Audio.SampleRate * 20 / 1000;

    private readonly List<float> samples = [];
    private readonly List<string> committed = [];
    private int committedUntil;
    private int previewedUntil;
    private string preview = string.Empty;

    /// <summary>Everything heard so far, for the recording.</summary>
    public IReadOnlyList<float> Samples => samples;

    public double Seconds => samples.Count / (double)Audio.SampleRate;

    /// <summary>The audio reached the answer's time limit; further audio is ignored.</summary>
    public bool IsFull => samples.Count >= MaxSamples;

    private int MaxSamples => Math.Max(1, maxSeconds) * Audio.SampleRate;

    /// <summary>Committed text followed by the latest preview of the tail.</summary>
    public string Text => Join(committed.Append(preview));

    public void Append(ReadOnlySpan<float> chunk)
    {
        var room = MaxSamples - samples.Count;
        if (room <= 0)
            return;
        samples.AddRange(chunk.Length <= room ? chunk : chunk[..room]);
    }

    /// <summary>
    /// Commits a window if the tail has grown long enough, then refreshes the preview when enough
    /// new audio has arrived. Returns the new text, or null when nothing was worth re-reading yet.
    /// </summary>
    public async Task<string?> UpdateAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        var tail = samples.Count - committedUntil;
        if (tail >= Seconds2Samples(options.CommitAfterSeconds))
        {
            var cut = QuietPoint(committedUntil, samples.Count);
            await CommitAsync(cut, cancellationToken);
            changed = true;
        }

        if (samples.Count - previewedUntil >= Seconds2Samples(options.PreviewEverySeconds) || changed)
        {
            previewedUntil = samples.Count;
            preview = committedUntil < samples.Count
                ? await speech.TranscribeAsync(Slice(committedUntil, samples.Count), language, prompt, preview: true, cancellationToken)
                : string.Empty;
            return Text;
        }
        return null;
    }

    /// <summary>Transcribes whatever is left with the full model and returns the whole answer.</summary>
    public async Task<string> FinishAsync(CancellationToken cancellationToken = default)
    {
        while (samples.Count - committedUntil > Seconds2Samples(options.MaxWindowSeconds))
            await CommitAsync(QuietPoint(committedUntil, samples.Count), cancellationToken);
        if (committedUntil < samples.Count)
            await CommitAsync(samples.Count, cancellationToken);
        preview = string.Empty;
        previewedUntil = samples.Count;
        return Text;
    }

    private async Task CommitAsync(int end, CancellationToken cancellationToken)
    {
        var text = await speech.TranscribeAsync(Slice(committedUntil, end), language, prompt, preview: false, cancellationToken);
        if (text.Length > 0)
            committed.Add(text);
        committedUntil = end;
    }

    /// <summary>
    /// The quietest 20 ms frame in the last third of the window, so a cut falls between words.
    /// A window longer than the maximum is cut at the maximum.
    /// </summary>
    internal int QuietPoint(int start, int end)
    {
        end = Math.Min(end, start + Seconds2Samples(options.MaxWindowSeconds));
        var searchFrom = start + (end - start) * 2 / 3;
        var best = end;
        var bestRms = float.MaxValue;
        for (var frame = searchFrom; frame + FrameSamples <= end; frame += FrameSamples)
        {
            var rms = Audio.Rms(Slice(frame, frame + FrameSamples).Span);
            if (rms < bestRms)
            {
                bestRms = rms;
                best = frame + FrameSamples / 2;
            }
        }
        return best;
    }

    private ReadOnlyMemory<float> Slice(int start, int end) =>
        samples.GetRange(start, end - start).ToArray();

    private static int Seconds2Samples(double seconds) => (int)(Math.Max(0.1, seconds) * Audio.SampleRate);

    private static string Join(IEnumerable<string> parts) =>
        string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
}
