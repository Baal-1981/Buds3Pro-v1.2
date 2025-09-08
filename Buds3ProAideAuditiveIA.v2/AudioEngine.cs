using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;

namespace Buds3ProAideAuditivelA.v2
{
    public interface ILogSink
    {
        void Log(string msg);
    }

    public sealed class AudioEngine : Java.Lang.Object
    {
        private readonly ILogSink _log;

        private AudioRecord _rec;
        private AudioTrack _track;
        private CancellationTokenSource _cts;

        private volatile bool _isRunning;

        // Flags UI
        public bool PassThroughEnabled { get; set; } = true;
        public bool GateEnabled { get; set; }
        public bool DuckerEnabled { get; set; }
        public bool HighPassEnabled { get; set; }
        public bool ClarityEnabled { get; set; }

        public int SampleRate { get; private set; } = 0; // 0 = Auto
        public int FrameMs { get; private set; } = 10;   // 2 / 5 / 10 ms

        // Dérivés
        private int _bytesPerFrame;
        private short _gateThreshold = 200; // gate simple
        private float _duckFactor = 0.5f;   // ducker simple

        public AudioEngine(ILogSink log)
        {
            _log = log ?? new NullSink();
        }

        public void Configure(
            int sampleRate,
            int frameMs,
            bool pass,
            bool gate,
            bool ducker,
            bool hp,
            bool clarity)
        {
            SampleRate = sampleRate; // 0, 44100, 48000
            FrameMs = frameMs;       // 2, 5, 10
            PassThroughEnabled = pass;
            GateEnabled = gate;
            DuckerEnabled = ducker;
            HighPassEnabled = hp;
            ClarityEnabled = clarity;
        }

        public bool IsRunning { get { return _isRunning; } }

        public async Task<bool> StartAsync()
        {
            if (_isRunning) return true;

            try
            {
                int chosenRate = SelectSampleRate(SampleRate);
                ChannelIn channelConfigIn = ChannelIn.Mono;
                ChannelOut channelConfigOut = ChannelOut.Mono;
                Encoding encoding = Encoding.Pcm16bit;

                int frameBytes = (chosenRate / 1000) * FrameMs * 2; // mono 16-bit
                _bytesPerFrame = Math.Max(64, frameBytes);

                int recMin = AudioRecord.GetMinBufferSize(chosenRate, channelConfigIn, encoding);
                if (recMin <= 0)
                {
                    _log.Log("GetMinBufferSize(rec) invalide: " + recMin);
                    return false;
                }
                int recBuf = RoundUpToPowerOfTwo(Math.Max(recMin, _bytesPerFrame * 4));

                _rec = new AudioRecord(
                    AudioSource.VoiceCommunication, // meilleur traitement natif
                    chosenRate,
                    channelConfigIn,
                    encoding,
                    recBuf);

                // IMPORTANT : comparer à Android.Media.State pour éviter les enums ambigus
                if (_rec.State != Android.Media.State.Initialized)
                {
                    _log.Log("AudioRecord non initialisé — abort");
                    ReleaseRecord();
                    return false;
                }

                int outMin = AudioTrack.GetMinBufferSize(chosenRate, channelConfigOut, encoding);
                if (outMin <= 0)
                {
                    _log.Log("GetMinBufferSize(track) invalide: " + outMin);
                    ReleaseRecord();
                    return false;
                }
                int outBuf = RoundUpToPowerOfTwo(Math.Max(outMin, _bytesPerFrame * 4));

                _track = new AudioTrack.Builder()
                    .SetAudioAttributes(new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.Media)
                        .SetContentType(AudioContentType.Speech)
                        .Build())
                    .SetAudioFormat(new AudioFormat.Builder()
                        .SetEncoding(encoding)
                        .SetSampleRate(chosenRate)
                        .SetChannelMask(channelConfigOut)
                        .Build())
                    .SetBufferSizeInBytes(outBuf)
                    .Build();

                if (_track.State != Android.Media.State.Initialized)
                {
                    _log.Log("AudioTrack non initialisé — abort");
                    ReleaseTrack();
                    ReleaseRecord();
                    return false;
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _rec.StartRecording();
                if (_rec.RecordingState != RecordState.Recording)
                {
                    _log.Log("AudioRecord n'a pas démarré");
                    await StopAsync();
                    return false;
                }

                _track.Play();
                if (_track.PlayState != PlayState.Playing)
                {
                    _log.Log("AudioTrack n'a pas démarré");
                    await StopAsync();
                    return false;
                }

                _log.Log("RUN ▶ SR=" + chosenRate + "Hz, frame=" + FrameMs + "ms, recBuf=" + recBuf + ", outBuf=" + outBuf);
                Task.Run(new Action(() => PumpLoop(_cts.Token)), _cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                _log.Log("StartAsync EX: " + ex);
                await StopAsync();
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;
            try
            {
                if (_cts != null) _cts.Cancel();
                await Task.Delay(20);
            }
            catch
            {
                // ignore
            }
            finally
            {
                _isRunning = false;
                ReleaseTrack();
                ReleaseRecord();
                if (_cts != null) _cts.Dispose();
                _cts = null;
                _log.Log("STOP ◼");
            }
        }

        private void PumpLoop(CancellationToken ct)
        {
            byte[] inBuf = new byte[_bytesPerFrame];
            short[] proc = new short[_bytesPerFrame / 2];
            byte[] outBuf = new byte[_bytesPerFrame];

            // HPF simple (1er ordre) si activé
            float hpPrev = 0f;
            float hpAlpha = 0.995f; // ~100 Hz approximatif pour 48/44.1k

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int n = _rec.Read(inBuf, 0, inBuf.Length);
                    if (n <= 0) continue;

                    // bytes -> shorts (Little Endian)
                    Buffer.BlockCopy(inBuf, 0, proc, 0, n);

                    // DSP pipeline léger
                    if (HighPassEnabled)
                    {
                        short last = 0;
                        for (int i = 0; i < n / 2; i++)
                        {
                            short x = proc[i];
                            short prev = (i > 0) ? proc[i - 1] : last;
                            float y = hpAlpha * (hpPrev + x - prev);
                            hpPrev = y;
                            int clamped = (int)y;
                            if (clamped > short.MaxValue) clamped = short.MaxValue;
                            else if (clamped < short.MinValue) clamped = short.MinValue;
                            proc[i] = (short)clamped;
                        }
                    }

                    if (GateEnabled)
                    {
                        bool below = true;
                        for (int i = 0; i < n / 2; i++)
                        {
                            int a = proc[i];
                            if (a < 0) a = -a;
                            if (a > _gateThreshold) { below = false; break; }
                        }
                        if (below)
                        {
                            Array.Clear(proc, 0, n / 2);
                        }
                    }

                    if (DuckerEnabled && !GateEnabled)
                    {
                        for (int i = 0; i < n / 2; i++)
                        {
                            int v = (int)(proc[i] * _duckFactor);
                            if (v > short.MaxValue) v = short.MaxValue;
                            else if (v < short.MinValue) v = short.MinValue;
                            proc[i] = (short)v;
                        }
                    }

                    if (ClarityEnabled)
                    {
                        // léger tilt EQ
                        for (int i = 0; i < n / 2; i++)
                        {
                            int v = (int)(proc[i] * 1.05f);
                            if (v > short.MaxValue) v = short.MaxValue;
                            else if (v < short.MinValue) v = short.MinValue;
                            proc[i] = (short)v;
                        }
                    }

                    if (!PassThroughEnabled)
                    {
                        Array.Clear(proc, 0, n / 2);
                    }

                    // shorts -> bytes
                    Buffer.BlockCopy(proc, 0, outBuf, 0, n);

                    int wrote = _track.Write(outBuf, 0, n);
                    if (wrote < 0)
                    {
                        _log.Log("AudioTrack.Write err: " + wrote);
                    }
                }
                catch (Exception ex)
                {
                    _log.Log("Pump EX: " + ex.Message);
                    break;
                }
            }
        }

        private static int SelectSampleRate(int requested)
        {
            if (requested == 44100 || requested == 48000) return requested;
            int ok48 = AudioRecord.GetMinBufferSize(48000, ChannelIn.Mono, Encoding.Pcm16bit);
            if (ok48 > 0) return 48000;
            return 44100;
        }

        private static int RoundUpToPowerOfTwo(int x)
        {
            if (x < 1) return 1;
            x--;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            x++;
            return x;
        }

        private void ReleaseRecord()
        {
            try
            {
                if (_rec == null) return;
                if (_rec.RecordingState == RecordState.Recording) _rec.Stop();
                _rec.Release();
                _rec.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _rec = null;
            }
        }

        private void ReleaseTrack()
        {
            try
            {
                if (_track == null) return;
                if (_track.PlayState == PlayState.Playing) _track.Stop();
                _track.Release();
                _track.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _track = null;
            }
        }

        private sealed class NullSink : ILogSink
        {
            public void Log(string msg) { /* no-op */ }
        }
    }
}
