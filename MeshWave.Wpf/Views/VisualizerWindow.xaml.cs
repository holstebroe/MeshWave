using System;
using System.Diagnostics;
using System.Windows;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Wpf;
using MeshWave.Wpf.ViewModels;
using MeshWave.Wpf.Services;

namespace MeshWave.Wpf.Views;

public partial class VisualizerWindow : Window
{
    private int _shaderProgram;
    private int _vao;
    private int _vbo;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private int _fboA, _fboB;
    private int _texA, _texB;
    private int _width = -1, _height = -1;
    private bool _pingpong = false;

    public PlaybackViewModel? Playback { get; set; }
    public AudioAnalysisService? AudioAnalysis { get; set; }

    public VisualizerWindow()
    {
        InitializeComponent();

        var settings = new GLWpfControlSettings
        {
            MajorVersion = 3,
            MinorVersion = 3,
            RenderContinuously = true
        };
        OpenGlControl.Start(settings);

        InitializeGL();
        _stopwatch.Start();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VisualizerViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }

        if (e.NewValue is VisualizerViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
        }

        CompileShader();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualizerViewModel.SelectedShader))
        {
            CompileShader();
        }
    }

    private void InitializeGL()
    {
        float[] vertices = {
            -1.0f, -1.0f, 0.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 0.0f, 1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
             1.0f,  1.0f, 0.0f, 1.0f, 1.0f
        };

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
    }



    private void CompileShader()
    {
        if (DataContext is not VisualizerViewModel vm) return;

        string vertexShaderSource = @"
            #version 330 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec2 aTexCoords;
            out vec2 TexCoords;
            void main()
            {
                gl_Position = vec4(aPos, 1.0);
                TexCoords = aTexCoords;
            }";

        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexShaderSource);
        GL.CompileShader(vertexShader);

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        var vmShaderScript = vm.ShaderScript;
        GL.ShaderSource(fragmentShader, vmShaderScript);
        GL.CompileShader(fragmentShader);

        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = GL.GetShaderInfoLog(fragmentShader);
            System.Diagnostics.Debug.WriteLine($"Fragment Shader Error: {infoLog}");
            return;
        }

        if (_shaderProgram != 0)
        {
            GL.DeleteProgram(_shaderProgram);
        }

        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vertexShader);
        GL.AttachShader(_shaderProgram, fragmentShader);
        GL.LinkProgram(_shaderProgram);

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    private void ReloadShader_Click(object sender, RoutedEventArgs e)
    {
        CompileShader();
    }

    private void EnsureFramebuffers(int width, int height)
    {
        if (_width == width && _height == height) return;

        if (_fboA != 0) GL.DeleteFramebuffer(_fboA);
        if (_fboB != 0) GL.DeleteFramebuffer(_fboB);
        if (_texA != 0) GL.DeleteTexture(_texA);
        if (_texB != 0) GL.DeleteTexture(_texB);

        _width = width;
        _height = height;

        _fboA = GL.GenFramebuffer();
        _fboB = GL.GenFramebuffer();

        _texA = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texA);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboA);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texA, 0);

        _texB = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texB);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboB);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texB, 0);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void OpenGlControl_OnRender(TimeSpan delta)
    {
        int currentFbo = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int width = (int)OpenGlControl.ActualWidth;
        int height = (int)OpenGlControl.ActualHeight;
        if (width <= 0 || height <= 0) return;

        EnsureFramebuffers(width, height);

        int targetFbo = _pingpong ? _fboB : _fboA;
        int prevTex = _pingpong ? _texA : _texB;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        GL.Viewport(0, 0, width, height);

        GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_shaderProgram == 0 || DataContext is not VisualizerViewModel vm)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, currentFbo);
            return;
        }

        GL.UseProgram(_shaderProgram);

        int timeLoc = GL.GetUniformLocation(_shaderProgram, "u_time");
        if (timeLoc != -1) GL.Uniform1(timeLoc, (float)_stopwatch.Elapsed.TotalSeconds);

        int resLoc = GL.GetUniformLocation(_shaderProgram, "u_resolution");
        if (resLoc != -1) GL.Uniform2(resLoc, (float)OpenGlControl.ActualWidth, (float)OpenGlControl.ActualHeight);

        int prevFrameLoc = GL.GetUniformLocation(_shaderProgram, "u_prevFrame");
        if (prevFrameLoc != -1)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, prevTex);
            GL.Uniform1(prevFrameLoc, 0);
        }

        // Fetch audio data right before render if we are playing
        if (Playback != null && AudioAnalysis != null && Playback.IsPlaying && !string.IsNullOrEmpty(Playback.SelectedAlbumTrack?.FilePath))
        {
            var data = AudioAnalysis.GetAudioDataAt(Playback.CurrentPosition);
            vm.FftData = data.FftData;
            vm.PcmData = data.PcmData;
        }
        else
        {
            vm.FftData = new float[512];
            vm.PcmData = new float[1024];
        }

        int audioLoc = GL.GetUniformLocation(_shaderProgram, "u_audioData");
        if (audioLoc != -1 && vm.FftData != null)
        {
            GL.Uniform1(audioLoc, vm.FftData.Length, vm.FftData);
        }

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        // Blit to screen
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, targetFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, currentFbo);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        // Restore drawing to the original FBO
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, currentFbo);

        _pingpong = !_pingpong;
    }
}
