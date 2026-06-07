namespace MeshWave.Wpf.Services;

/// <summary>
/// Visual style used when drawing the waveform on the playback canvas.
/// </summary>
public enum WaveformStyle
{
    /// <summary>Plain solid bars centred on the horizontal axis.</summary>
    Filled,

    /// <summary>
    /// Cloudy / atmospheric look:
    ///   - Every third bar is omitted (gap creates a "cloudy" spaced feel).
    ///   - Lower half is drawn at half the amplitude with a cooler tint.
    ///   - A thin mask strip hides the very centre line.
    ///   - Comment icons float above the signal.
    /// </summary>
    Cloudy,

    /// <summary>
    /// Perfect vertical mirror: top half is normal, bottom half is an exact
    /// reflection drawn at 70 % opacity and a slightly shifted hue.
    /// </summary>
    Mirror,

    /// <summary>
    /// Neon glow: thin bright bars with wide dark gaps; a second semi-transparent
    /// wider bar gives a bloom / glow halo behind each bar.
    /// </summary>
    Neon,

    /// <summary>
    /// Smooth envelope: sample amplitudes are convolved with a Hann window to
    /// produce a continuous, smooth outline curve.  The enclosed region is filled
    /// with a vertical linear gradient (bright accent at the centre fading to
    /// transparent at the edges).
    /// </summary>
    Smooth
}
