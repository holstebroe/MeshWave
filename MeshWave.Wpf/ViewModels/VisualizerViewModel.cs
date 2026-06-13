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

        ShaderScript = demoPlasmaShader;
    }

    // TODO: Make selector for default shaders, and move these to separate files or embedded resources

    private string demoPlasmaShader = @"
#version 330 core

out vec4 FragColor;

// Update these names!
uniform vec2 u_resolution; 
uniform float u_time;       

void main() {
    // Normalize pixel coordinates (from 0.0 to 1.0)
    vec2 uv = gl_FragCoord.xy / u_resolution.xy;

    // Create a moving, swirling pattern using sine/cosine waves
    float r = sin(uv.x * 5.0 + u_time) * 0.5 + 0.5;
    float g = sin(uv.y * 5.0 + u_time * 1.3) * 0.5 + 0.5;
    float b = cos((uv.x + uv.y) * 4.0 - u_time * 0.8) * 0.5 + 0.5;

    // Mix them up a bit for a more organic feel
    r += sin(u_time + uv.y * 10.0) * 0.2;
    g += cos(u_time - uv.x * 10.0) * 0.2;

    // Output the final vibrant color
    FragColor = vec4(r, g, b, 1.0);
}
";

}
