using System.Buffers.Binary;

namespace AIVES.BLL.Services.Speech;

/// <summary>Writes synthesised speech as a WAV file.</summary>
public static class Audio
{
    /// <summary>Little-endian 16-bit PCM bytes (as VieNeu returns them) to floats in [-1, 1].</summary>
    public static float[] FromPcm16(ReadOnlySpan<byte> bytes)
    {
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]) / 32768f;
        return samples;
    }

    /// <summary>Root mean square loudness of a stretch of samples.</summary>
    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return 0;
        double sum = 0;
        foreach (var sample in samples)
            sum += sample * sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>A mono 16-bit PCM WAV file.</summary>
    public static byte[] ToWav(ReadOnlySpan<float> samples, int sampleRate)
    {
        var data = samples.Length * 2;
        var wav = new byte[44 + data];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + data);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], data);
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], value);
        }
        return wav;
    }
}
