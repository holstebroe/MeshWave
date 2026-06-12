using System;
using NAudio.Wave;
using NAudio.Dsp;

namespace MeshWave.Wpf.Services;

public class AudioAnalysisService : IAudioAnalysisService
{
    public event EventHandler<float[]>? OnPcmDataAvailable;
    public event EventHandler<float[]>? OnFftDataAvailable;

    public ISampleProvider CreateInterceptor(ISampleProvider source)
    {
        return new SampleProviderInterceptor(source, this);
    }

    internal void RaisePcmDataAvailable(float[] pcm)
    {
        OnPcmDataAvailable?.Invoke(this, pcm);
    }

    internal void RaiseFftDataAvailable(float[] fft)
    {
        OnFftDataAvailable?.Invoke(this, fft);
    }

    private class SampleProviderInterceptor : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly AudioAnalysisService _parent;
        private readonly int _fftLength;
        private readonly int _m;
        private readonly float[] _pcmBuffer;
        private int _pcmBufferPos;
        private readonly int _channels;

        public SampleProviderInterceptor(ISampleProvider source, AudioAnalysisService parent, int fftLength = 1024)
        {
            _source = source;
            _parent = parent;
            _fftLength = fftLength;
            _m = (int)Math.Log(fftLength, 2.0);
            _pcmBuffer = new float[fftLength];
            _pcmBufferPos = 0;
            _channels = source.WaveFormat.Channels;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            // We read "samplesRead" values from the buffer.
            // If it's stereo (_channels == 2), 2 values equal 1 frame.
            int framesRead = samplesRead / _channels;

            for (int i = 0; i < framesRead; i++)
            {
                float sampleSum = 0;
                for (int c = 0; c < _channels; c++)
                {
                    sampleSum += buffer[offset + i * _channels + c];
                }

                // Downmix to mono by averaging the channels
                float monoSample = sampleSum / _channels;
                _pcmBuffer[_pcmBufferPos++] = monoSample;

                if (_pcmBufferPos >= _fftLength)
                {
                    // Raise PCM event
                    float[] pcmCopy = new float[_fftLength];
                    Array.Copy(_pcmBuffer, pcmCopy, _fftLength);
                    _parent.RaisePcmDataAvailable(pcmCopy);

#if DEBUG
                    float sum = 0;
                    foreach (var sample in pcmCopy) sum += Math.Abs(sample);
                    System.Diagnostics.Debug.WriteLine($"Average Amplitude: {sum / pcmCopy.Length}");
#endif

                    // Compute FFT
                    Complex[] complexBuffer = new Complex[_fftLength];
                    for (int j = 0; j < _fftLength; j++)
                    {
                        // Apply Hamming window
                        complexBuffer[j].X = (float)(_pcmBuffer[j] * FastFourierTransform.HammingWindow(j, _fftLength));
                        complexBuffer[j].Y = 0;
                    }

                    FastFourierTransform.FFT(true, _m, complexBuffer);

                    float[] fftCopy = new float[_fftLength / 2];
                    for (int j = 0; j < _fftLength / 2; j++)
                    {
                        // Calculate magnitude
                        float magnitude = (float)Math.Sqrt(complexBuffer[j].X * complexBuffer[j].X + complexBuffer[j].Y * complexBuffer[j].Y);
                        fftCopy[j] = magnitude;
                    }
                    _parent.RaiseFftDataAvailable(fftCopy);

                    _pcmBufferPos = 0;
                }
            }

            return samplesRead;
        }
    }
}
