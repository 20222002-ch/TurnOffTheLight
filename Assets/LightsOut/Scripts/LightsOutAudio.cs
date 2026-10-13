using UnityEngine;

namespace LightsOut
{
    /// <summary>Original game soundtrack and short 2D interaction cues.</summary>
    public sealed class LightsOutAudio : MonoBehaviour
    {
        private AudioClip[] effects;
        private AudioSource musicSource;
        private AudioSource effectSource;
        private float masterVolume = 0.7f;
        private float fadeIn;

        public void Initialize(AudioClip[] clips, AudioClip music)
        {
            effects = clips;
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.loop = true;
                musicSource.spatialBlend = 0f;
                musicSource.priority = 200;
            }
            if (effectSource == null)
            {
                effectSource = gameObject.AddComponent<AudioSource>();
                effectSource.playOnAwake = false;
                effectSource.spatialBlend = 0f;
                effectSource.priority = 64;
            }
            musicSource.Stop();
            musicSource.clip = music;
            musicSource.volume = 0f;
            fadeIn = 0f;
            effectSource.volume = masterVolume;
            if (music != null) musicSource.Play();
        }

        /// <param name="index">0 switch, 1 light off, 2 gold, 3 upgrade, 4 zone, 5 shift.</param>
        public void Play(int index, float pitch = 1f)
        {
            if (effectSource == null || effects == null || index < 0 || index >= effects.Length || effects[index] == null)
                return;
            effectSource.pitch = Mathf.Clamp(pitch, 0.65f, 1.5f);
            effectSource.PlayOneShot(effects[index]);
        }

        public void SetVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            if (effectSource != null) effectSource.volume = masterVolume;
            if (musicSource != null) musicSource.volume = masterVolume * 0.3f * fadeIn;
        }

        private void Update()
        {
            if (musicSource == null) return;
            fadeIn = Mathf.MoveTowards(fadeIn, 1f, Time.unscaledDeltaTime / 2f);
            musicSource.volume = masterVolume * 0.3f * fadeIn;
        }
    }
}
