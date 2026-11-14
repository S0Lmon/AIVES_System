using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vosk;

namespace AIVES.BLL.Services.Speech;

public sealed record SpeechRecognitionUpdate(string Text, bool IsFinal);

public sealed class VoskSpeechRecognitionSession : IAsyncDisposable
{
    private const int SampleRate = 16000;
    private readonly BlockingCollection<byte[]> input = new(new ConcurrentQueue<byte[]>(), 100);
    private readonly VoskRecognizer recognizer;
    private readonly Func<SpeechRecognitionUpdate, Task> publish;
    private readonly ILogger<VoskSpeechRecognitionSession> logger;
    private readonly Task processing;
    private int stopped;
    private int disposed;

    public VoskSpeechRecognitionSession(
        Model model,
        Func<SpeechRecognitionUpdate, Task> publish,
        ILogger<VoskSpeechRecognitionSession> logger)
    {
        this.publish = publish;
        this.logger = logger;
        recognizer = new VoskRecognizer(model, SampleRate);
        processing = Task.Run(ProcessAudioAsync);
    }

    public void WriteAudio(byte[]? pcm)
    {
        if (Volatile.Read(ref stopped) != 0)
            throw new InvalidOperationException("Speech recognition has already stopped.");
        if (pcm is null || pcm.Length == 0 || (pcm.Length & 1) != 0 || pcm.Length > 32768)
            throw new ArgumentException("Audio must be a non-empty, even-sized PCM16 frame of at most 32 KB.", nameof(pcm));
        if (!input.TryAdd(pcm, TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("Vosk is not keeping up with the audio stream.");
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 0)
            input.CompleteAdding();
        await processing;
    }

    private async Task ProcessAudioAsync()
    {
        foreach (var frame in input.GetConsumingEnumerable())
        {
            if (recognizer.AcceptWaveform(frame, frame.Length))
                await PublishJsonAsync(recognizer.Result(), "text", true);
            else
                await PublishJsonAsync(recognizer.PartialResult(), "partial", false);
        }

        await PublishJsonAsync(recognizer.FinalResult(), "text", true);
    }

    private async Task PublishJsonAsync(string json, string propertyName, bool isFinal)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var value))
            return;
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            await publish(new SpeechRecognitionUpdate(text, isFinal));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send Vosk speech recognition result to the candidate");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            await StopAsync();
        }
        finally
        {
            recognizer.Dispose();
            input.Dispose();
        }
    }
}
