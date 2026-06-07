using System.IO;
using NAudio.Wave;

namespace MeshWave.Wpf.Services
{
    public static class WaveformService
    {
        public static float[] GenerateWaveform(string filePath, int points = 4096)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || points <= 0)
            {
                return [];
            }

            var accumulators = new float[points];
            var counts = new int[points];

            using var reader = new AudioFileReader(filePath);
            var totalFrames = Math.Max((long)(reader.TotalTime.TotalSeconds * reader.WaveFormat.SampleRate), 1L);
            var channels = Math.Max(reader.WaveFormat.Channels, 1);
            var buffer = new float[reader.WaveFormat.SampleRate * channels];
            long frameOffset = 0;

            int samplesRead;
            while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var framesRead = samplesRead / channels;
                for (var frame = 0; frame < framesRead; frame++)
                {
                    var sampleSum = 0f;
                    for (var c = 0; c < channels; c++)
                    {
                        sampleSum += Math.Abs(buffer[(frame * channels) + c]);
                    }

                    var monoSample = sampleSum / channels;
                    var globalFrame = frameOffset + frame;
                    var normalized = Math.Clamp((double)globalFrame / totalFrames, 0.0, 0.999999);
                    var index = (int)(normalized * points);

                    accumulators[index] += monoSample;
                    counts[index]++;
                }

                frameOffset += framesRead;
            }

            var maxValue = 0.0f;
            for (var i = 0; i < points; i++)
            {
                var value = counts[i] > 0 ? accumulators[i] / counts[i] : 0f;
                if (value > maxValue) maxValue = value;
            }

            // Scale to 1.1f
            var scale = maxValue > 0 ? 1f / maxValue : 1f;


            for (var i = 0; i < points; i++)
            {
                accumulators[i] = counts[i] > 0
                    ? Math.Clamp(scale * accumulators[i] / counts[i], 0f, 1f)
                    : 0f;
            }

            return accumulators;
        }
    }
}
