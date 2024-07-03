using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.Components.Audio
{
    [CreateAssetMenu("Audio/AudioSourceSettings")]
    public sealed class AudioSourceSettings : ScriptableObject
    {
        [SerializeField]
        private float _maxDistance = 10.0f;

        [SerializeField]
        [Range(-1f, 1f)]
        private float _pitch = 0.0f;

        [SerializeField]
        private bool _loop = false;

        public void SetSourceSettings(AudioSource source)
        {
            source.MaxDistance = _maxDistance;
            source.Pitch = _pitch;
            source.Looping = _loop;
        }
    }
}
