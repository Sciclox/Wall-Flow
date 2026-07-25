using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WallFlow.Services;

public enum SoundType
{
    Hover,
    Click,
    Whoosh,
    Ping
}

public static class SoundService
{
    private static readonly string SoundsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallFlow", "Sounds");

    private const int SampleRate = 44100;

    private static readonly Dictionary<string, CachedSound> _cachedSounds = new Dictionary<string, CachedSound>();
    private static readonly object _cacheLock = new object();

    static SoundService()
    {
        try
        {
            // Force clean regeneration of sounds on start to fix any old corrupted/static files
            if (Directory.Exists(SoundsFolder))
            {
                foreach (var file in Directory.GetFiles(SoundsFolder, "*.wav"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static void Shutdown()
    {
    }

    private class CachedSound
    {
        public float[] AudioData { get; }
        public WaveFormat WaveFormat { get; }

        public CachedSound(string filePath)
        {
            using (var reader = new AudioFileReader(filePath))
            {
                WaveFormat = reader.WaveFormat;
                var wholeFile = new List<float>((int)(reader.Length / 4));
                var readBuffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                int samplesRead;
                while ((samplesRead = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    for (int i = 0; i < samplesRead; i++)
                    {
                        wholeFile.Add(readBuffer[i]);
                    }
                }
                AudioData = wholeFile.ToArray();
            }
        }
    }

    private class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound _cachedSound;
        private long _position;

        public CachedSoundSampleProvider(CachedSound cachedSound)
        {
            _cachedSound = cachedSound;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var availableSamples = _cachedSound.AudioData.Length - _position;
            var samplesToCopy = (int)Math.Min(availableSamples, count);
            if (samplesToCopy <= 0) return 0;

            Array.Copy(_cachedSound.AudioData, _position, buffer, offset, samplesToCopy);
            _position += samplesToCopy;
            return samplesToCopy;
        }

        public WaveFormat WaveFormat => _cachedSound.WaveFormat;
    }

    public static void PreloadAsync()
    {
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(SoundsFolder);
                var profiles = new[] { "Zen", "Cyberpunk", "Holographic" };
                var types = new[] { SoundType.Hover, SoundType.Click, SoundType.Whoosh, SoundType.Ping };

                foreach (var profile in profiles)
                {
                    foreach (var type in types)
                    {
                        string fileName = $"{profile}_{type}.wav";
                        string filePath = Path.Combine(SoundsFolder, fileName);
                        
                        EnsureSoundExists(profile, type, filePath);
                        
                        lock (_cacheLock)
                        {
                            if (!_cachedSounds.ContainsKey(filePath))
                            {
                                if (File.Exists(filePath))
                                {
                                    _cachedSounds[filePath] = new CachedSound(filePath);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        });
    }

    public static void Play(SoundType type)
    {
        var s = SettingsService.Instance;
        if (!s.SoundEnabled) return;

        string profile = s.SoundProfile ?? "Zen";
        double volume = s.SoundVolume;

        PlayFile(profile, type, volume);
    }

    public static void PlayPreview(string profile, double volume)
    {
        PlayFile(profile, SoundType.Click, volume);
    }

    private static void PlayFile(string profile, SoundType type, double volume)
    {
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(SoundsFolder);
                string fileName = $"{profile}_{type}.wav";
                string filePath = Path.Combine(SoundsFolder, fileName);

                EnsureSoundExists(profile, type, filePath);

                CachedSound? cachedSound;
                lock (_cacheLock)
                {
                    if (!_cachedSounds.TryGetValue(filePath, out cachedSound))
                    {
                        if (!File.Exists(filePath)) return;
                        cachedSound = new CachedSound(filePath);
                        _cachedSounds[filePath] = cachedSound;
                    }
                }

                ISampleProvider provider = new CachedSoundSampleProvider(cachedSound);

                var volumeProvider = new VolumeSampleProvider(provider)
                {
                    Volume = (float)Math.Clamp(volume, 0.0, 1.0)
                };

                var waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += (s, ev) =>
                {
                    try { waveOut.Dispose(); } catch { }
                };

                waveOut.Init(volumeProvider);
                waveOut.Play();
            }
            catch { }
        });
    }

    private static void EnsureSoundExists(string profile, SoundType type, string filePath)
    {
        if (File.Exists(filePath)) return;

        try
        {
            short[] samples;
            switch (profile.ToLowerInvariant())
            {
                case "cyberpunk":
                    samples = GenerateCyberpunkSound(type);
                    break;
                case "holographic":
                    samples = GenerateHolographicSound(type);
                    break;
                case "zen":
                default:
                    samples = GenerateZenSound(type);
                    break;
            }

            WriteWavFile(filePath, samples, SampleRate);
        }
        catch { }
    }

    private static short[] GenerateZenSound(SoundType type)
    {
        int numSamples;
        short[] samples;

        switch (type)
        {
            case SoundType.Hover:
                {
                    double duration = 0.05;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double freq = 150.0 - 70.0 * (t / duration);
                        phase += 2 * Math.PI * freq / SampleRate;
                        double envelope = Math.Sin(Math.Min(1.0, t / 0.01) * Math.PI / 2) * (1.0 - t / duration);
                        samples[i] = (short)(Math.Sin(phase) * envelope * 0.4 * 32767);
                    }
                }
                break;

            case SoundType.Click:
                {
                    double duration = 0.03;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double phase = 2 * Math.PI * 800.0 * t;
                        double envelope = Math.Exp(-t / 0.005);
                        double click = 0;
                        if (t < 0.002)
                        {
                            click = Math.Sin(2 * Math.PI * 2500.0 * t) * (1.0 - t / 0.002);
                        }
                        samples[i] = (short)((Math.Sin(phase) * 0.8 + click * 0.2) * envelope * 0.7 * 32767);
                    }
                }
                break;

            case SoundType.Whoosh:
                {
                    double duration = 0.15;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double freq = 130.0 + 110.0 * (t / duration);
                        phase += 2 * Math.PI * freq / SampleRate;
                        double envelope = Math.Sin(t / duration * Math.PI);
                        samples[i] = (short)(Math.Sin(phase) * envelope * 0.5 * 32767);
                    }
                }
                break;

            case SoundType.Ping:
                {
                    double duration = 0.3;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double fund = Math.Sin(2 * Math.PI * 1100.0 * t);
                        double harm = Math.Sin(2 * Math.PI * 2200.0 * t) * 0.25;
                        double envelope = Math.Exp(-t / 0.06);
                        samples[i] = (short)((fund + harm) / 1.25 * envelope * 0.5 * 32767);
                    }
                }
                break;

            default:
                samples = new short[0];
                break;
        }

        return samples;
    }

    private static short[] GenerateCyberpunkSound(SoundType type)
    {
        int numSamples;
        short[] samples;
        var rand = new Random();

        switch (type)
        {
            case SoundType.Hover:
                {
                    double duration = 0.04;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double freq = 250.0 - 150.0 * (t / duration);
                        phase += 2 * Math.PI * freq / SampleRate;
                        double sqVal = Math.Sin(phase) >= 0 ? 1.0 : -1.0;
                        double envelope = (1.0 - t / duration);
                        samples[i] = (short)(sqVal * envelope * 0.15 * 32767);
                    }
                }
                break;

            case SoundType.Click:
                {
                    double duration = 0.02;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double triVal = 2.0 * Math.Abs((2.0 * (t * 500.0 - Math.Floor(t * 500.0 + 0.5)))) - 1.0;
                        double noiseVal = rand.NextDouble() * 2.0 - 1.0;
                        double envelope = Math.Exp(-t / 0.004);
                        double val = triVal * 0.6 + noiseVal * 0.4;
                        samples[i] = (short)(val * envelope * 0.5 * 32767);
                    }
                }
                break;

            case SoundType.Whoosh:
                {
                    double duration = 0.12;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double vibrato = Math.Sin(2 * Math.PI * 40.0 * t) * 15.0;
                        double freq = 100.0 + 250.0 * (t / duration) + vibrato;
                        phase += 2 * Math.PI * freq / SampleRate;
                        double envelope = Math.Sin(t / duration * Math.PI);
                        samples[i] = (short)(Math.Sin(phase) * envelope * 0.4 * 32767);
                    }
                }
                break;

            case SoundType.Ping:
                {
                    double duration = 0.25;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double tone1 = Math.Sin(2 * Math.PI * 900.0 * t);
                        double tone2 = Math.Sin(2 * Math.PI * 1300.0 * t);
                        double envelope = Math.Exp(-t / 0.05);
                        samples[i] = (short)((tone1 + tone2) * 0.25 * envelope * 32767);
                    }
                }
                break;

            default:
                samples = new short[0];
                break;
        }

        return samples;
    }

    private static short[] GenerateHolographicSound(SoundType type)
    {
        int numSamples;
        short[] samples;

        switch (type)
        {
            case SoundType.Hover:
                {
                    double duration = 0.04;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double envelope = Math.Exp(-t / 0.008);
                        samples[i] = (short)(Math.Sin(2 * Math.PI * 880.0 * t) * envelope * 0.4 * 32767);
                    }
                }
                break;

            case SoundType.Click:
                {
                    double duration = 0.015;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double freq = 4000.0 - 2500.0 * (t / duration);
                        phase += 2 * Math.PI * freq / SampleRate;
                        double envelope = Math.Exp(-t / 0.003);
                        samples[i] = (short)(Math.Sin(phase) * envelope * 0.6 * 32767);
                    }
                }
                break;

            case SoundType.Whoosh:
                {
                    double duration = 0.12;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    double phase = 0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double freq = 200.0 + 400.0 * (t / duration);
                        phase += 2 * Math.PI * freq / SampleRate;
                        double envelope = Math.Sin(t / duration * Math.PI);
                        samples[i] = (short)(Math.Sin(phase) * envelope * 0.4 * 32767);
                    }
                }
                break;

            case SoundType.Ping:
                {
                    double duration = 0.35;
                    numSamples = (int)(duration * SampleRate);
                    samples = new short[numSamples];
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / SampleRate;
                        double c = Math.Sin(2 * Math.PI * 523.25 * t);
                        double e = Math.Sin(2 * Math.PI * 659.25 * t);
                        double g = Math.Sin(2 * Math.PI * 783.99 * t);
                        double envelope = Math.Exp(-t / 0.08);
                        samples[i] = (short)((c + e + g) / 3.0 * envelope * 0.4 * 32767);
                    }
                }
                break;

            default:
                samples = new short[0];
                break;
        }

        return samples;
    }

    private static void WriteWavFile(string filePath, short[] samples, int sampleRate)
    {
        using (var fs = new FileStream(filePath, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            int dataSize = samples.Length * 2;

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // format chunk size
            bw.Write((short)1); // PCM format
            bw.Write((short)1); // mono channel
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // byte rate (sampleRate * bitsPerSample * channels / 8)
            bw.Write((short)2); // block align
            bw.Write((short)16); // bits per sample
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);

            foreach (var sample in samples)
            {
                bw.Write(sample);
            }
        }
    }
}
