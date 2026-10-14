using UnityEngine;

namespace LightsOut
{
    /// <summary>A small world-space sprite pool for the upward active skill. The game supplies paused time.</summary>
    public sealed class LightsOutRiseEffect : MonoBehaviour
    {
        const int StreakCount = 3, ParticleCount = 9;
        const float BurstDuration = .18f, FadeDuration = .25f, AfterimageInterval = .025f;
        static readonly Color Mint = new Color(.55f, .91f, .77f, 1);
        static readonly Color Gold = new Color(1, .76f, .31f, 1);

        struct Particle
        {
            public SpriteRenderer Renderer;
            public Transform Transform;
            public Vector2 Position, Velocity, Size;
            public Color Tint;
            public float Life, Duration;
            public bool Burst;
        }

        readonly SpriteRenderer[] streaks = new SpriteRenderer[StreakCount];
        readonly Particle[] particles = new Particle[ParticleCount];
        LightsOutMover mover;
        Transform pool;
        Vector2 spriteSize = Vector2.one, previousPosition, stopPosition;
        float emissionClock, animationClock, streakFade;
        bool rising;

        public int PoolSize { get { return StreakCount + ParticleCount; } }
        public int ActiveParticleCount
        {
            get
            {
                int active = 0;
                for (int i = 0; i < streaks.Length; i++) if (streaks[i] && streaks[i].enabled) active++;
                for (int i = 0; i < particles.Length; i++) if (particles[i].Life > 0) active++;
                return active;
            }
        }

        public void Initialize(LightsOutMover player, Sprite sprite)
        {
            Unsubscribe();
            mover = player;
            if (!pool)
            {
                pool = new GameObject("Vertical Rise Effects").transform;
                pool.SetParent(transform, false);
                for (int i = 0; i < StreakCount; i++) streaks[i] = CreateRenderer("Rise Streak " + i, 94 + i);
                for (int i = 0; i < ParticleCount; i++)
                {
                    SpriteRenderer renderer = CreateRenderer("Rise Particle " + i, 90 + i);
                    particles[i].Renderer = renderer;
                    particles[i].Transform = renderer.transform;
                }
            }
            SpriteRenderer body = mover ? mover.GetComponent<SpriteRenderer>() : null;
            spriteSize = sprite ? new Vector2(Mathf.Max(.001f, sprite.bounds.size.x), Mathf.Max(.001f, sprite.bounds.size.y)) : Vector2.one;
            for (int i = 0; i < StreakCount; i++)
            {
                streaks[i].sprite = sprite;
                if (body && body.sharedMaterial) streaks[i].sharedMaterial = body.sharedMaterial;
            }
            for (int i = 0; i < ParticleCount; i++)
            {
                particles[i].Renderer.sprite = sprite;
                if (body && body.sharedMaterial) particles[i].Renderer.sharedMaterial = body.sharedMaterial;
            }
            Clear();
            if (mover)
            {
                mover.RiseStarted += BeginRise;
                mover.RiseFinished += FinishRise;
            }
        }

        SpriteRenderer CreateRenderer(string name, int order)
        {
            GameObject item = new GameObject(name, typeof(SpriteRenderer));
            item.transform.SetParent(pool, false);
            SpriteRenderer renderer = item.GetComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        void BeginRise()
        {
            if (!isActiveAndEnabled || !mover || !pool) return;
            Clear();
            rising = true;
            previousPosition = stopPosition = mover.Position;
            animationClock = emissionClock = 0;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                ref Particle particle = ref particles[i];
                particle.Burst = true;
                particle.Duration = particle.Life = BurstDuration;
                particle.Position = previousPosition + direction * .22f;
                particle.Velocity = direction * 4.3f;
                particle.Size = new Vector2(.23f, .055f);
                particle.Tint = i % 2 == 0 ? Gold : Mint;
                particle.Renderer.sortingOrder = 105;
                particle.Transform.rotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg + 90);
                particle.Renderer.enabled = true;
                DrawParticle(ref particle);
            }
        }

        void FinishRise()
        {
            if (!rising) return;
            rising = false;
            if (mover) stopPosition = mover.Position;
            streakFade = FadeDuration;
        }

        public void Tick(float dt)
        {
            if (!isActiveAndEnabled || !mover || !pool || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            animationClock += dt;
            for (int i = 0; i < ParticleCount; i++)
            {
                ref Particle particle = ref particles[i];
                if (particle.Life <= 0) continue;
                particle.Life = Mathf.Max(0, particle.Life - dt);
                if (particle.Life <= 0) { particle.Renderer.enabled = false; continue; }
                particle.Position += particle.Velocity * dt;
                DrawParticle(ref particle);
            }
            // A reset or a building finishing can end the move without another working frame.
            if (rising && !mover.IsRising) FinishRise();
            Vector2 current = rising ? mover.Position : stopPosition;
            if (rising)
            {
                emissionClock += dt;
                int samples = Mathf.Min(8, Mathf.FloorToInt(emissionClock / AfterimageInterval));
                if (samples > 0)
                {
                    emissionClock %= AfterimageInterval;
                    for (int i = 0; i < samples; i++) EmitAfterimage(Vector2.Lerp(previousPosition, current, (i + 1f) / samples));
                }
                previousPosition = current;
                streakFade = FadeDuration;
            }
            else streakFade = Mathf.Max(0, streakFade - dt);

            float fade = rising ? 1 : streakFade / FadeDuration;
            float pulse = .88f + .12f * Mathf.Sin(animationClock * 48);
            for (int i = 0; i < StreakCount; i++)
            {
                SpriteRenderer renderer = streaks[i];
                renderer.enabled = fade > 0;
                if (!renderer.enabled) continue;
                float length = i == 1 ? 1.35f : .86f;
                renderer.transform.position = new Vector3(current.x + (i - 1) * .19f, current.y - .28f - length * .5f, -1);
                renderer.transform.rotation = Quaternion.identity;
                SetWorldSize(renderer.transform, new Vector2(i == 1 ? .09f : .055f, length * pulse));
                Color tint = i == 1 ? Gold : Mint;
                tint.a = fade * (i == 1 ? .75f : .58f);
                renderer.color = tint;
            }
        }

        void EmitAfterimage(Vector2 position)
        {
            int selected = -1;
            float oldest = float.MaxValue;
            for (int i = 0; i < ParticleCount; i++)
            {
                if (particles[i].Life <= 0) { selected = i; break; }
                if (!particles[i].Burst && particles[i].Life < oldest) { selected = i; oldest = particles[i].Life; }
            }
            if (selected < 0) return;
            ref Particle particle = ref particles[selected];
            particle.Burst = false;
            particle.Duration = particle.Life = FadeDuration;
            particle.Position = position;
            particle.Velocity = Vector2.down * .42f;
            particle.Size = new Vector2(.36f, .40f);
            particle.Tint = Mint;
            particle.Tint.a = .46f;
            particle.Renderer.sortingOrder = 90 + selected;
            particle.Transform.rotation = Quaternion.identity;
            particle.Renderer.enabled = true;
            DrawParticle(ref particle);
        }

        void DrawParticle(ref Particle particle)
        {
            float fraction = particle.Life / particle.Duration;
            particle.Transform.position = new Vector3(particle.Position.x, particle.Position.y, -1);
            float scale = particle.Burst ? .65f + .35f * fraction : .7f + .3f * fraction;
            SetWorldSize(particle.Transform, particle.Size * scale);
            Color tint = particle.Tint;
            tint.a *= fraction * fraction;
            particle.Renderer.color = tint;
        }

        void SetWorldSize(Transform target, Vector2 size)
        {
            Vector3 parentScale = pool.lossyScale;
            target.localScale = new Vector3(size.x / spriteSize.x / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                size.y / spriteSize.y / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), 1);
        }

        void Clear()
        {
            rising = false;
            emissionClock = animationClock = streakFade = 0;
            for (int i = 0; i < StreakCount; i++) if (streaks[i]) streaks[i].enabled = false;
            for (int i = 0; i < ParticleCount; i++)
            {
                particles[i].Life = 0;
                if (particles[i].Renderer) particles[i].Renderer.enabled = false;
            }
        }

        void Unsubscribe()
        {
            if (!mover) return;
            mover.RiseStarted -= BeginRise;
            mover.RiseFinished -= FinishRise;
        }
        void OnDisable() { Clear(); }
        void OnDestroy() { Unsubscribe(); if (pool) Destroy(pool.gameObject); }
    }
}
