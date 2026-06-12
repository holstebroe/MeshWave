using System;
using NAudio.Wave;

namespace MeshWave.Wpf.Services;

public interface IAudioAnalysisService
{
    event EventHandler<float[]>? OnPcmDataAvailable;
    event EventHandler<float[]>? OnFftDataAvailable;

    ISampleProvider CreateInterceptor(ISampleProvider source);
}
