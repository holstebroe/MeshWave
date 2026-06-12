using System;
using MeshWave.Wpf.Mvvm;

namespace MeshWave.Wpf.ViewModels;

public class VisualizerViewModel : ViewModelBase
{
    private string _shaderScript = "";
    public string ShaderScript
    {
        get => _shaderScript;
        set => SetProperty(ref _shaderScript, value);
    }

    private float[] _pcmData = new float[1024];
    public float[] PcmData
    {
        get => _pcmData;
        set => SetProperty(ref _pcmData, value);
    }

    private float[] _fftData = new float[512];
    public float[] FftData
    {
        get => _fftData;
        set => SetProperty(ref _fftData, value);
    }

    public VisualizerViewModel()
    {
        LoadDefaultShader();
    }

    private void LoadDefaultShader()
    {
        ShaderScript = @"
#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform float u_time;
uniform vec2 u_resolution;
uniform float u_audioData[512];

void main()
{
    vec2 uv = gl_FragCoord.xy / u_resolution.xy;

    int index = int(uv.x * 512.0);
    float audioVal = u_audioData[index] * 10.0;

    vec3 color = vec3(0.0);
    if(uv.y < audioVal) {
        color = vec3(uv.x, 0.5, 1.0 - uv.x);
    }

    FragColor = vec4(color, 1.0);
}";
    }
}
