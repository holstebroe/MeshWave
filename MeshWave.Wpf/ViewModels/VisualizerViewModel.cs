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
        // Default to feedback tunnel shader to test it out for now.
        // It can be easily toggled back if needed.
        ShaderScript = feedbackTunnelShader;
    }

    // TODO: Make selector for default shaders, and move these to separate files or embedded resources

    private string _defaultAudioShader = @"
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

    // Tunnel shader with feedback loop - creates a swirling tunnel effect that evolves over time
    // TODO:
    // Because your current GLWpfControl setup renders directly to the control's internal surface, texture(u_prevFrame, sample_uv) has nothing to read. To make this run, you need to implement Ping-Pong Textures in your C# render loop.
    // Here is the high-level logic you will need to add to your InitializeGL and OpenGlControl_OnRender methods:
    // Create Two Framebuffers(FBOs) and Two Textures: Let's call them A and B. Both textures must match the resolution of your window.
    // The Ping-Pong Loop: * Frame 1: Bind FBO A.Clear it.Bind Texture B to u_prevFrame.Draw the shader.
    // Frame 2: Bind FBO B.Clear it. Bind Texture A to u_prevFrame.Draw the shader.
    // Blit to Screen: After drawing to your FBO, you must copy (or draw) the resulting texture to the default framebuffer (the screen) so GLWpfControl can display it.

    private string feedbackTunnelShader = @"
#version 330 core

out vec4 FragColor;

uniform vec2 u_resolution;
uniform float u_time;

// THIS IS NEW: You must pass the previous frame's texture here
uniform sampler2D u_prevFrame; 

void main() {
    // 1. Normalize coordinates (0.0 to 1.0)
    vec2 uv = gl_FragCoord.xy / u_resolution.xy;
    
    // Center the coordinates (-0.5 to 0.5) to build our vector field
    vec2 centered = uv - 0.5;
    
    // Correct for aspect ratio so our seed is perfectly round
    float aspect = u_resolution.x / u_resolution.y;
    vec2 aspectCentered = vec2(centered.x * aspect, centered.y);

    // 2. Create the Vector Field Distortion
    // Rotate the pull direction over time to twist the tunnel
    float angle = u_time * 0.5;
    float s = sin(angle);
    float c = cos(angle);
    mat2 rot = mat2(c, -s, s, c);
    
    vec2 twistedCenter = rot * centered;
    
    // Add sine/cosine ripples to the vector field itself
    vec2 ripples = vec2(
        sin(uv.y * 15.0 - u_time * 2.0), 
        cos(uv.x * 15.0 + u_time * 2.0)
    ) * 0.003;
    
    // 3. Sample the Previous Frame
    // To make pixels expand OUTWARD, we sample INWARD (towards the center)
    float pullSpeed = 0.03; 
    vec2 sample_uv = uv - (twistedCenter * pullSpeed) + ripples;

    // Grab the pixel from the last frame
    vec4 feedback = texture(u_prevFrame, sample_uv);
    
    // Dim the feedback slightly so the trails slowly fade into darkness
    feedback *= 0.98; 

    // 4. Generate the ""Seed"" in the center
    vec3 seedColor = vec3(0.0);
    float dist = length(aspectCentered);
    
    // Make the seed pulse in size
    float pulseRadius = (sin(u_time * 4.0) * 0.5 + 0.5) * 0.03 + 0.01;
    
    if (dist < pulseRadius) {
        // Shimmering neon colors for the seed
        seedColor = vec3(
            sin(u_time * 3.0) * 0.5 + 0.5,
            cos(u_time * 2.3) * 0.5 + 0.5,
            sin(u_time * 1.7) * 0.5 + 0.5
        );
        
        // Make the core bright white to ensure it survives the feedback loop
        if (dist < pulseRadius * 0.5) {
            seedColor = vec3(1.0); 
        }
    }

    // 5. Combine and Output
    // Use max() so the bright seed punches through the fading feedback trails
    vec3 finalColor = max(feedback.rgb, seedColor);

    FragColor = vec4(finalColor, 1.0);
}
";
}
