using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.Components.Audio
{
    [CreateAssetMenu("Audio/AudioPatch")]
    public sealed class AudioPatch : ScriptableObject
    {
        [SerializeField] private List<AssetRef<AudioClip>> _clips;
        [SerializeField][Range(0, 1)] private float _minVolume = 1;
        [SerializeField][Range(0, 1)] private float _maxVolume = 1;
        [SerializeField][Range(-3, 3)] private float _minPitch = 1;
        [SerializeField][Range(-3, 3)] private float _maxPitch = 1;

        public override void OnValidate()
        {
            if (_minVolume > _maxVolume)
            {
                _minVolume = _maxVolume;
            }

            if (_minPitch > _maxPitch)
            {
                _minPitch = _maxPitch;
            }
        }

        public void Play(AudioSource source)
        {
            source.Volume = (float)Random.Range(_minVolume, _maxVolume);
            source.Pitch = (float)Random.Range(_minPitch, _maxPitch);
            source.Clip = _clips[Random.Range(0, _clips.Count)];
            source.Play();
        }

    }
}
