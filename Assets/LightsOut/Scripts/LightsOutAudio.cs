using UnityEngine;

namespace LightsOut
{
    /// <summary>Original game soundtrack and short 2D interaction cues.</summary>
    public sealed class LightsOutAudio : MonoBehaviour
    {
        private AudioClip[] effects;
        private AudioSource musicSource;
        private AudioSource effectSource;
        private AudioSource rouletteSource;
        private AudioSource tensionSource;
        private AudioClip tensionPulse;
        private float masterVolume = 0.7f;
        private float fadeIn;
        private float rouletteTension;
        private float smoothedTension;
        private float pulseCountdown;

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
            if (rouletteSource == null) rouletteSource = CreateSource(48);
            if (tensionSource == null) tensionSource = CreateSource(128);
            ResetRouletteTension();
            musicSource.Stop();
            musicSource.clip = music;
            musicSource.volume = 0f;
            fadeIn = 0f;
            effectSource.volume = masterVolume;
            rouletteSource.volume = masterVolume;
            tensionSource.volume = masterVolume;
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

        /// <summary>Roulette cues have their own voice, so ticks cannot change gameplay cue pitch.</summary>
        /// <param name="index">0 tick, 1 prepare, 2 jackpot, 3 doubled reward, 5 resolution.</param>
        public void PlayRouletteCue(int index, float pitch = 1f, float gain = 1f)
        {
            if (rouletteSource == null || effects == null || index < 0 || index >= effects.Length || effects[index] == null)
                return;
            rouletteSource.pitch = Mathf.Clamp(pitch, 0.65f, 1.5f);
            rouletteSource.PlayOneShot(effects[index], Mathf.Clamp01(gain));
        }

        /// <summary>Uses a quiet original pulse and smoothly ducks the soundtrack during a spin.</summary>
        public void SetRouletteTension(float intensity)
        {
            rouletteTension = Mathf.Clamp01(intensity);
            if (rouletteTension <= 0f)
            {
                pulseCountdown = 0f;
                if (tensionSource != null) tensionSource.Stop();
            }
        }

        public void SetVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            if (effectSource != null) effectSource.volume = masterVolume;
            if (rouletteSource != null) rouletteSource.volume = masterVolume;
            if (tensionSource != null) tensionSource.volume = masterVolume;
            RefreshMusicVolume();
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            fadeIn = Mathf.MoveTowards(fadeIn, 1f, deltaTime / 2f);
            smoothedTension = Mathf.MoveTowards(smoothedTension, rouletteTension, deltaTime * 2.5f);
            RefreshMusicVolume();
            if (rouletteTension <= 0f || tensionSource == null) return;

            pulseCountdown -= deltaTime;
            if (pulseCountdown > 0f) return;
            pulseCountdown = Mathf.Lerp(1f, 0.65f, smoothedTension);
            if (masterVolume <= 0f || smoothedTension <= 0f) return;
            if (tensionPulse == null) tensionPulse = CreateTensionPulse();
            tensionSource.PlayOneShot(tensionPulse, Mathf.Lerp(0.12f, 0.24f, smoothedTension));
        }

        private AudioSource CreateSource(int priority)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = priority;
            return source;
        }

        private void RefreshMusicVolume()
        {
            if (musicSource != null)
                musicSource.volume = masterVolume * 0.3f * fadeIn * (1f - 0.45f * smoothedTension);
        }

        private static AudioClip CreateTensionPulse()
        {
            const int sampleRate = 48000;
            const float duration = 0.08f;
            float[] samples = new float[(int)(sampleRate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                float attack = Mathf.Clamp01(time / 0.004f);
                float release = Mathf.Clamp01((duration - time) / 0.012f);
                samples[i] = 0.28f * Mathf.Sin(time * 90f * 2f * Mathf.PI)
                    * attack * release * Mathf.Exp(-time * 38f);
            }
            AudioClip clip = AudioClip.Create("Lights Out - Original Roulette Pulse", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void ResetRouletteTension()
        {
            rouletteTension = 0f;
            smoothedTension = 0f;
            pulseCountdown = 0f;
            if (tensionSource != null) tensionSource.Stop();
            RefreshMusicVolume();
        }

        private void OnDisable()
        {
            ResetRouletteTension();
            if (rouletteSource != null) rouletteSource.Stop();
        }

        private void OnDestroy()
        {
            if (tensionPulse != null) Destroy(tensionPulse);
        }
    }
}
