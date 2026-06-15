using System;
using System.IO;
using NAudio.Wave;
using LibOpenMPT.NET;

namespace MeshWave.Wpf.OpenMpt;

public class OpenMptWaveStream : WaveStream
{
    private IntPtr _module;
    private readonly WaveFormat _waveFormat;
    private readonly long _length;
    private long _position;

    public OpenMptWaveStream(string filename)
    {
        var bytes = File.ReadAllBytes(filename);
        InitializeModule(bytes);

        _waveFormat = new WaveFormat(48000, 16, 2);

        unsafe
        {
            double durationSeconds = NativeMethods.module_get_duration_seconds((Module*)_module);
            _length = (long)(durationSeconds * _waveFormat.AverageBytesPerSecond);
        }
    }

    public OpenMptWaveStream(Stream stream)
    {
        // For P2P growing streams, tracker files need the entire file fully downloaded
        // to parse patterns and samples accurately. Waiting synchronously blocks the thread.
        // We will read what is currently available, but if it fails to initialize,
        // it means the stream isn't fully downloaded or valid yet.
        // A proper solution would require async initialization, but WaveStream constructors are sync.
        // For the scope of this experimental plugin, we assume the stream is fully available or we read fully.
        // The caller (AudioPlaybackService) waits for 256KB before calling this constructor, which covers most trackers.
        // Ensure stream is at beginning if possible.
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        InitializeModule(bytes);

        _waveFormat = new WaveFormat(48000, 16, 2);

        unsafe
        {
            double durationSeconds = NativeMethods.module_get_duration_seconds((Module*)_module);
            _length = (long)(durationSeconds * _waveFormat.AverageBytesPerSecond);
        }
    }

    private void InitializeModule(byte[] bytes)
    {
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                // LibOpenMPT.NET module_create_from_memory2 allows null callbacks
                _module = (IntPtr)NativeMethods.module_create_from_memory2(ptr, (nuint)bytes.Length, null, null, null, null, null, null, null);
            }
        }

        if (_module == IntPtr.Zero)
        {
            throw new InvalidDataException("Failed to load module using LibOpenMPT. The file may be corrupt or not a supported tracker format.");
        }
    }

    public override WaveFormat WaveFormat => _waveFormat;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (_module == IntPtr.Zero) return;

            value = Math.Max(0, Math.Min(value, _length));
            value -= (value % WaveFormat.BlockAlign);

            _position = value;
            double seconds = (double)_position / WaveFormat.AverageBytesPerSecond;

            unsafe
            {
                NativeMethods.module_set_position_seconds((Module*)_module, seconds);
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_module == IntPtr.Zero) return 0;

        count -= (count % WaveFormat.BlockAlign);
        if (count == 0) return 0;

        unsafe
        {
            fixed (byte* pBuffer = &buffer[offset])
            {
                int framesToRead = count / WaveFormat.BlockAlign;
                nuint readFrames = NativeMethods.module_read_interleaved_stereo((Module*)_module, WaveFormat.SampleRate, (nuint)framesToRead, (short*)pBuffer);

                int bytesRead = (int)readFrames * WaveFormat.BlockAlign;
                _position += bytesRead;

                return bytesRead;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_module != IntPtr.Zero)
        {
            unsafe
            {
                NativeMethods.module_destroy((Module*)_module);
            }
            _module = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }
}
