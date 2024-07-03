using Prowl.Icons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.Components.Audio
{
    [AddComponentMenu($"{FontAwesome6.Music}  Audio/{FontAwesome6.Message}  AudioManager")]
    public sealed class AudioManager : MonoBehaviour
    {
        #region Initiation

        private List<AudioSource> _sources = new List<AudioSource>();

        public override void Awake()
        {
            GameObject tempObject = new GameObject();
            for (int i = 0; i < 15; i++)
            {
                GameObject newAudioSource = GameObject.Instantiate(tempObject, GameObject);

                _sources.Add(newAudioSource.AddComponent<AudioSource>());
            }

            Destroy(tempObject);
        }

        private AudioSource FindAudioSource()
        {
            bool foundSource = false;

            int index = 0;

            while (foundSource == false && index < _sources.Count)
            {
                if (_sources[index].IsPlaying == false)
                {
                    foundSource = true;
                    return _sources[index];

                }
                else
                {
                    index++;
                }

            }

            GameObject newAudioSource = new GameObject();

            var tempAudioSource = GameObject.Instantiate(newAudioSource, GameObject);
            _sources.Add(tempAudioSource.AddComponent<AudioSource>());

            Destroy(newAudioSource);
            return _sources[_sources.Count - 1];
        }
        #endregion

        #region OneShots
        [SerializeField]
        private AudioSourceSettings _defaultAudioSourceSettings;

        public void PlayAudio(Vector3 sourcePosition, AudioPatch patchToPlay)
        {
            AudioSource chosenSource = FindAudioSource();
            chosenSource.GameObject.Transform.parent = GameObject.Transform;
            chosenSource.GameObject.Transform.position = sourcePosition;
            AudioPatch patch = patchToPlay;
            _defaultAudioSourceSettings.SetSourceSettings(chosenSource);
            patch.Play(chosenSource);
        }

        public void PlayAudio(Transform sourcePosition, AudioPatch patchToPlay)
        {
          PlayAudio(sourcePosition.position, patchToPlay);
        }

        public void PlayAudio(Vector3 sourcePosition, AudioPatch patchToPlay, AudioSourceSettings settings)
        {
            AudioSource chosenSource = FindAudioSource();
            chosenSource.GameObject.Transform.parent = GameObject.Transform;
            chosenSource.GameObject.Transform.position = sourcePosition;
            AudioPatch patch = patchToPlay;
            settings.SetSourceSettings(chosenSource);
            patch.Play(chosenSource);
        }

        public void PlayAudio(Transform sourcePosition, AudioPatch patchToPlay, AudioSourceSettings settings)
        {
           PlayAudio(sourcePosition.position, patchToPlay, settings);
        }

        public void PlayAudioAttached(Transform sourcePosition, AudioPatch patchToPlay)
        {
            AudioSource chosenSource = FindAudioSource();
            chosenSource.GameObject.Transform.parent = sourcePosition;
            chosenSource.GameObject.Transform.localPosition = new Vector3(0, 0, 0);
            AudioPatch patch = patchToPlay;
            _defaultAudioSourceSettings.SetSourceSettings(chosenSource);
            patch.Play(chosenSource);
        }

        public void PlayAudioAttached(Transform sourcePosition, AudioPatch patchToPlay, AudioSourceSettings settings)
        {
            AudioSource chosenSource = FindAudioSource();
            chosenSource.GameObject.Transform.parent = sourcePosition;
            chosenSource.GameObject.Transform.localPosition = new Vector3(0, 0, 0);
            AudioPatch patch = patchToPlay;
            settings.SetSourceSettings(chosenSource);
            patch.Play(chosenSource);
        }
        #endregion
    }
}
