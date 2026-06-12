using System;
using System.IO;
using NAudio.Wave;
using NAudio.Dsp;

namespace MeshWave.Wpf.Services;

public class AudioAnalysisService : IDisposable
{
    private AudioFileReader? _reader;
    private readonly object _lockObj = new();
    private string? _currentFilePath;
    private float[] _fftData = new float[512];
    private float[] _pcmData = new float[1024];
    private readonly Complex[] _complexBuffer = new Complex[1024];
    private readonly float[] _sampleBuffer = new float[1024];

    public void StartAnalysis(string filePath)
    {
        lock (_lockObj)
        {
            try
            {
                if (_currentFilePath != filePath)
                {
                    _reader?.Dispose();
                    _currentFilePath = filePath;
                    _reader = new AudioFileReader(filePath);
                }
            }
            catch (Exception)
            {
                _reader = null;
            }
        }
    }

    public void StopAnalysis()
    {
        lock (_lockObj)
        {
            _reader?.Dispose();
            _reader = null;
            _currentFilePath = null;
        }
    }

    public (float[] PcmData, float[] FftData) GetAudioDataAt(TimeSpan position)
    {
        lock (_lockObj)
        {
            if (_reader == null) return (new float[1024], new float[512]);

            // Prevent Seeking errors if position is out of bounds
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > _reader.TotalTime) position = _reader.TotalTime;

            _reader.CurrentTime = position;

            int ffftLength = 1024;
            int m = (int)Math.Log(ffftLength, 2.0);

            int read = _reader.Read(_sampleBuffer, 0, ffftLength);

            if (read > 0)
            {
                // Copy PCM
                for (int i = 0; i < Math.Min(read, _pcmData.Length); i++)
                {
                    _pcmData[i] = _sampleBuffer[i];
                }
                for (int i = read; i < _pcmData.Length; i++)
                {
                    _pcmData[i] = 0;
                }

                // Prepare FFT
                for (int i = 0; i < ffftLength; i++)
                {
                    _complexBuffer[i].X = (float)(_sampleBuffer[i] * FastFourierTransform.HammingWindow(i, ffftLength));
                    _complexBuffer[i].Y = 0;
                }

                FastFourierTransform.FFT(true, m, _complexBuffer);

                for (int i = 0; i < _fftData.Length; i++)
                {
                    _fftData[i] = (float)Math.Sqrt(_complexBuffer[i].X * _complexBuffer[i].X + _complexBuffer[i].Y * _complexBuffer[i].Y);
                }
            }
            else
            {
               Array.Clear(_pcmData, 0, _pcmData.Length);
               Array.Clear(_fftData, 0, _fftData.Length);
            }

            return ((float[])_pcmData.Clone(), (float[])_fftData.Clone());
        }
    }

    public void Dispose()
    {
        StopAnalysis();
    }
}
