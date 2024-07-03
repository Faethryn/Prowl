using Prowl.Icons;
using Prowl.Runtime.Audio;
using System;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.Music}  Audio/{FontAwesome6.Message}  Audio Source")]
    public sealed class AudioSource : MonoBehaviour
    {
        public AssetRef<AudioClip> Clip;
       
        public bool PlayOnAwake = true;

        public bool Looping
        {
            get { return _looping; }
            set
            {
                if (_looping != value)
                {
                    _looping = value;
                    if (_source != null)
                    {
                        _source.Looping = _looping;
                    }
                }
            }
        }

        public float Volume
        {
            get { return _gain; }
            set
            {
                if ( _gain != value )
                {
                    _gain = value;
                    if (_source != null)
                    {
                        _source.Gain = _gain;
                    }
                }
            }
        }

        public float Pitch
        {
            get { return _pitch; }
            set
            {
                if (_pitch != value)
                {
                    _pitch = value;
                    if (_source != null)
                    {
                        _source.Pitch = _pitch;
                    }
                }
            }
        }

        public float MaxDistance
        {
            get { return _maxDistance; }
            set
            {
                if (_maxDistance != value)
                {
                    _maxDistance = value;
                    if (_source != null)
                    {
                        _source.MaxDistance = _maxDistance;
                    }
                }
            }
        }

        public bool IsPlaying = false;

        private ActiveAudio _source;
        private AudioBuffer _buffer;
        private uint _lastVersion;
        private bool _looping = false;
        private float _gain = 1f;
        private float _pitch = 0f;
        private float _maxDistance = 32f;

        public void Play()
        {
            if (Clip.IsAvailable)
            {
                _source.Play(_buffer);
                IsPlaying = true;
            }
        }

        public void Stop()
        {
            if (Clip.IsAvailable)
            {
                _source?.Stop();
                IsPlaying = false;
            }
        }

        public override void Awake()
        {
            _source = AudioSystem.Engine.CreateAudioSource();
            _source.PositionKind = AudioPositionKind.ListenerRelative;
            // position relative to listener
            var listener = AudioSystem.Listener.GameObject.Transform;
            var thisPos = GameObject.Transform.position;
            _source.Position = listener.InverseTransformPoint(thisPos);
            _source.Direction = GameObject.Transform.forward;
            _source.Gain = Volume;
            _source.Looping = Looping;
            _source.MaxDistance = MaxDistance;
            if (Clip.IsAvailable)
                _buffer = AudioSystem.GetAudioBuffer(Clip.Res!);
            if (PlayOnAwake)
                Play();
        }

        public override void Update()
        {
            //if (_lastVersion != GameObject.transform.version)
            {
                var listener = AudioSystem.Listener.GameObject.Transform;
                var thisPos = GameObject.Transform.position;
                _source.Position = listener.InverseTransformPoint(thisPos);
                _source.Direction = GameObject.Transform.forward;
                //_lastVersion = GameObject.transform.version;
            }

            if (Clip.IsAvailable)
                _buffer = AudioSystem.GetAudioBuffer(Clip.Res!);
        }

        public override void OnDisable() => _source.Stop();

        public override void OnDestroy()
        {
            _source.Dispose();
        }
    }
}
