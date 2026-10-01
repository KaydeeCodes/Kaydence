using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Kaydence.Services;

// I talk to the classic Windows sound functions so voice notes need nothing extra installed
internal static class WinMm
{
    public const uint WaveMapper = 0xFFFFFFFF;
    public const int CallbackNull = 0;
    public const int HeaderDone = 0x1;
    public const int TimeBytes = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MmTime
    {
        public int Type;
        public int Value;
        public int Extra;
    }

    [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr handle);

    [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] public static extern int waveOutPause(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveOutRestart(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr handle);
    [DllImport("winmm.dll")] public static extern int waveOutGetPosition(IntPtr handle, ref MmTime time, int size);

    public static int HeaderSize => Marshal.SizeOf<WaveHeader>();

    public static WaveFormat Format(int rate, short channels, short bits) => new()
    {
        FormatTag = 1,
        Channels = channels,
        SamplesPerSec = rate,
        BitsPerSample = bits,
        BlockAlign = (short)(channels * bits / 8),
        AvgBytesPerSec = rate * channels * bits / 8,
        Size = 0
    };

    public static IntPtr NewHeader(int bytes)
    {
        var header = Marshal.AllocHGlobal(HeaderSize);
        Marshal.StructureToPtr(new WaveHeader { Data = Marshal.AllocHGlobal(bytes), BufferLength = bytes }, header, false);
        return header;
    }

    public static WaveHeader Read(IntPtr header) => Marshal.PtrToStructure<WaveHeader>(header);

    public static void Free(IntPtr header)
    {
        var data = Read(header).Data;
        if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
        Marshal.FreeHGlobal(header);
    }
}

// I record from my default microphone into memory, checking my buffers a few times a second
public sealed class VoiceRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int BufferMs = 100;
    private const int BufferCount = 8;

    private readonly List<IntPtr> _headers = new();
    private readonly Queue<IntPtr> _queue = new();
    private readonly MemoryStream _audio = new();
    private readonly DispatcherTimer _poll;
    private IntPtr _handle;
    private bool _running;

    public VoiceRecorder()
    {
        _poll = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };
        _poll.Tick += (_, _) => Collect();
    }

    // I hand out a loudness from 0 to 1 for every tenth of a second, for the live bars and the saved waveform
    public event Action<double>? Level;

    public List<double> Levels { get; } = new();

    public TimeSpan Elapsed => TimeSpan.FromSeconds(_audio.Length / (double)(SampleRate * 2));

    // I return a friendly problem if the microphone can't be opened
    public string? Start()
    {
        var format = WinMm.Format(SampleRate, 1, 16);
        var result = WinMm.waveInOpen(out _handle, WinMm.WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, WinMm.CallbackNull);
        Log.Info("Audio", $"Opening the default microphone at {SampleRate} Hz mono 16 bit: result {result}");
        if (result != 0)
        {
            _handle = IntPtr.Zero;
            return result switch
            {
                2 or 6 => "Kaydence couldn't find a microphone. Is one plugged in and set as the default in Windows sound settings?",
                4 => "The microphone is busy in another app. Close that app and try again.",
                _ => "Windows wouldn't let Kaydence use the microphone."
            };
        }

        var bytes = SampleRate * 2 * BufferMs / 1000;
        for (var i = 0; i < BufferCount; i++)
        {
            var header = WinMm.NewHeader(bytes);
            WinMm.waveInPrepareHeader(_handle, header, WinMm.HeaderSize);
            WinMm.waveInAddBuffer(_handle, header, WinMm.HeaderSize);
            _headers.Add(header);
            _queue.Enqueue(header);
        }
        WinMm.waveInStart(_handle);
        _running = true;
        _poll.Start();
        return null;
    }

    // I read finished buffers strictly in the order I handed them over, so the sound never gets jumbled
    private void Collect()
    {
        var fresh = new List<double>();
        while (_handle != IntPtr.Zero && _queue.Count > 0)
        {
            var header = _queue.Peek();
            var info = WinMm.Read(header);
            if ((info.Flags & WinMm.HeaderDone) == 0) break;
            _queue.Dequeue();

            if (info.BytesRecorded > 0)
            {
                var chunk = new byte[info.BytesRecorded];
                Marshal.Copy(info.Data, chunk, 0, chunk.Length);
                _audio.Write(chunk, 0, chunk.Length);
                var peak = 0;
                for (var i = 0; i + 1 < chunk.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(chunk, i)));
                var level = Math.Min(1, peak / 32768.0);
                Levels.Add(level);
                fresh.Add(level);
            }

            if (!_running) continue;
            // I hand the buffer straight back so recording never stops
            info.Flags &= ~WinMm.HeaderDone;
            info.BytesRecorded = 0;
            Marshal.StructureToPtr(info, header, false);
            WinMm.waveInAddBuffer(_handle, header, WinMm.HeaderSize);
            _queue.Enqueue(header);
        }

        // I only tell anyone about new sound once I've finished touching the buffers
        foreach (var level in fresh) Level?.Invoke(level);
    }

    // I stop, gather the last bits of sound and give back a finished WAV file
    public byte[] Stop()
    {
        if (_handle != IntPtr.Zero)
        {
            _running = false;
            _poll.Stop();
            WinMm.waveInReset(_handle);
            Collect();
        }
        Dispose();
        Log.Info("Audio", $"Recording stopped: {_audio.Length:N0} bytes, {Levels.Count / 10.0:0.0} seconds, loudest {(Levels.Count > 0 ? Levels.Max() : 0):0.000}");
        return Wav.Build(_audio.ToArray(), SampleRate, 1, 16);
    }

    public void Dispose()
    {
        _running = false;
        _poll.Stop();
        if (_handle == IntPtr.Zero) return;
        WinMm.waveInReset(_handle);
        foreach (var header in _headers)
        {
            WinMm.waveInUnprepareHeader(_handle, header, WinMm.HeaderSize);
            WinMm.Free(header);
        }
        _headers.Clear();
        _queue.Clear();
        WinMm.waveInClose(_handle);
        _handle = IntPtr.Zero;
    }
}

// I play a WAV from memory so encrypted voice notes never touch the disk unlocked
public sealed class VoicePlayer : IDisposable
{
    private static VoicePlayer? _current;

    private readonly byte[] _pcm;
    private readonly WinMm.WaveFormat _format;
    private readonly DispatcherTimer _poll;
    private IntPtr _handle;
    private IntPtr _header;
    private int _offset;
    private bool _paused;

    public VoicePlayer(byte[] wav)
    {
        (_format, _pcm) = Wav.Parse(wav);
        _poll = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(50) };
        _poll.Tick += (_, _) => Tick();
    }

    public event Action<double>? Progress;
    public event Action? Finished;

    public double Duration => _pcm.Length / (double)Math.Max(1, _format.AvgBytesPerSec);

    public bool IsPlaying => _handle != IntPtr.Zero && !_paused;

    public string? Play(double fromFraction = -1)
    {
        // I only ever play one voice note at a time
        if (_current != null && !ReferenceEquals(_current, this)) _current.Stop();
        _current = this;

        if (_paused && fromFraction < 0 && _handle != IntPtr.Zero)
        {
            WinMm.waveOutRestart(_handle);
            _paused = false;
            _poll.Start();
            return null;
        }

        var start = fromFraction >= 0 ? (int)(_pcm.Length * Math.Clamp(fromFraction, 0, 0.999)) : 0;
        start -= start % Math.Max(1, (int)_format.BlockAlign);
        Release();

        var format = _format;
        var result = WinMm.waveOutOpen(out _handle, WinMm.WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, WinMm.CallbackNull);
        Log.Info("Audio", $"Playing a voice note from {Math.Round(start / (double)Math.Max(1, _pcm.Length) * 100)}%: {format.SamplesPerSec} Hz, {format.Channels} channel, result {result}");
        if (result != 0)
        {
            _handle = IntPtr.Zero;
            return "Windows wouldn't play the sound. Are your speakers or headphones connected?";
        }

        var length = _pcm.Length - start;
        _header = WinMm.NewHeader(Math.Max(1, length));
        Marshal.Copy(_pcm, start, WinMm.Read(_header).Data, length);
        WinMm.waveOutPrepareHeader(_handle, _header, WinMm.HeaderSize);
        WinMm.waveOutWrite(_handle, _header, WinMm.HeaderSize);
        _offset = start;
        _paused = false;
        _poll.Start();
        return null;
    }

    public void Pause()
    {
        if (_handle == IntPtr.Zero || _paused) return;
        WinMm.waveOutPause(_handle);
        _paused = true;
        _poll.Stop();
    }

    public void Stop()
    {
        _poll.Stop();
        Release();
        _paused = false;
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private void Tick()
    {
        if (_handle == IntPtr.Zero) return;
        var time = new WinMm.MmTime { Type = WinMm.TimeBytes };
        WinMm.waveOutGetPosition(_handle, ref time, Marshal.SizeOf<WinMm.MmTime>());
        var played = _offset + (time.Type == WinMm.TimeBytes ? time.Value : 0);
        Progress?.Invoke(Math.Min(1, played / (double)Math.Max(1, _pcm.Length)));

        if ((WinMm.Read(_header).Flags & WinMm.HeaderDone) == 0) return;
        Stop();
        Progress?.Invoke(0);
        Finished?.Invoke();
    }

    private void Release()
    {
        if (_handle == IntPtr.Zero) return;
        WinMm.waveOutReset(_handle);
        if (_header != IntPtr.Zero)
        {
            WinMm.waveOutUnprepareHeader(_handle, _header, WinMm.HeaderSize);
            WinMm.Free(_header);
            _header = IntPtr.Zero;
        }
        WinMm.waveOutClose(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose() => Stop();
}

// I build and read plain PCM WAV files
public static class Wav
{
    public static byte[] Build(byte[] pcm, int rate, short channels, short bits)
    {
        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    internal static (WinMm.WaveFormat Format, byte[] Pcm) Parse(byte[] wav)
    {
        using var reader = new BinaryReader(new MemoryStream(wav));
        if (wav.Length < 12 || reader.ReadInt32() != 0x46464952) throw new InvalidDataException("That isn't a WAV file.");
        reader.ReadInt32();
        reader.ReadInt32();
        var format = WinMm.Format(VoiceRecorder.SampleRate, 1, 16);
        byte[]? pcm = null;
        while (reader.BaseStream.Position + 8 <= wav.Length && pcm == null)
        {
            var id = reader.ReadInt32();
            var size = reader.ReadInt32();
            if (id == 0x20746D66)
            {
                var tag = reader.ReadInt16();
                var channels = reader.ReadInt16();
                var rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                var bits = reader.ReadInt16();
                if (tag != 1) throw new InvalidDataException("Kaydence can only play plain WAV voice notes.");
                format = WinMm.Format(rate, channels, bits);
                reader.BaseStream.Position += size - 16;
            }
            else if (id == 0x61746164)
            {
                pcm = reader.ReadBytes(Math.Min(size, wav.Length - (int)reader.BaseStream.Position));
            }
            else
            {
                reader.BaseStream.Position += size;
            }
        }
        return (format, pcm ?? Array.Empty<byte>());
    }

    // I squash the loudness readings down to a handful of bars for the little waveform
    public static byte[] Shape(IReadOnlyList<double> levels, int bars)
    {
        var result = new byte[bars];
        if (levels.Count == 0) return result;
        var loudest = Math.Max(0.02, levels.Max());
        for (var i = 0; i < bars; i++)
        {
            var from = i * levels.Count / bars;
            var to = Math.Max(from + 1, (i + 1) * levels.Count / bars);
            var peak = 0.0;
            for (var j = from; j < to && j < levels.Count; j++) peak = Math.Max(peak, levels[j]);
            result[i] = (byte)Math.Round(Math.Sqrt(peak / loudest) * 255);
        }
        return result;
    }
}
