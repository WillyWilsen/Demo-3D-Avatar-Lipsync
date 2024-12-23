using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections.Concurrent;

namespace LiveKit
{
    public class AudioStream
    {
        internal readonly FfiHandle Handle;
        private AudioSource _audioSource;
        private AudioSource _audioSource2;
        private AudioFilter _audioFilter;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private object _lock = new object();

        // Queue to pass audio data to main thread
        private ConcurrentQueue<float[]> _audioDataQueue = new ConcurrentQueue<float[]>();

        public AudioStream(IAudioTrack audioTrack, AudioSource source, AudioSource source2)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)audioTrack.TrackHandle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;
            
            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            UpdateSource(source, source2);
        }

        private void UpdateSource(AudioSource source, AudioSource source2)
        {
            _audioSource = source;
            _audioSource2 = source2;
            _audioFilter = source.gameObject.AddComponent<AudioFilter>();
            //_audioFilter.hideFlags = HideFlags.HideInInspector;
            _audioFilter.AudioRead += OnAudioRead;
            source.Play();
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * 0.2);
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(size * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // "Send" the data to Unity
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int read = _buffer.Read(temp);

                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = S16ToFloat(_tempBuffer[i]);
                }

                // Enqueue audio data for processing on the main thread
                _audioDataQueue.Enqueue((float[])data.Clone());
            }
        }

        // Called on the MainThread (See FfiClient)
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var frame = new AudioFrame(e.FrameReceived.Frame);

            lock (_lock)
            { 
                if (_numChannels == 0)
                    return;

                unsafe
                {
                    var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                    if(uFrame != null) {
                        var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                        _buffer?.Write(data);
                    }
                    
                }
            }
        }

        // Update method called from the main thread
        public void Update()
        {
            if (_audioDataQueue.TryDequeue(out var audioData))
            {
                // Create the AudioClip on the main thread
                AudioClip newClip = AudioClip.Create("GeneratedAudioClip", audioData.Length, (int)_numChannels, (int)_sampleRate, false);
                newClip.SetData(audioData, 0);

                // Assign the AudioClip to the AudioSource
                _audioSource2.clip = newClip;
                _audioSource2.loop = true;
                _audioSource2.volume = 0.1f;
                _audioSource2.Play();
            }
        }
    }
}
