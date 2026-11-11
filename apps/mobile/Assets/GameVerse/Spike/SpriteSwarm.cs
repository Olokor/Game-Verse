using UnityEngine;

namespace GameVerse.Spike
{
    /// <summary>
    /// Prompt 0.7 web performance spike (throwaway, deleted with ADR-0002): spawns N flipbook-animated sprites that drift
    /// and bounce inside the camera view. Each sprite has its own MonoBehaviour on purpose, as a realistic worst case.
    /// </summary>
    public sealed class SpriteSwarm : MonoBehaviour
    {
        [SerializeField] private Texture2D sheet;
        [SerializeField] private int frameCount = 4;
        [SerializeField] private int count = 200;
        [SerializeField] private Color[] tints = { Color.white };

        private void Awake()
        {
            var frameWidth = sheet.width / frameCount;
            var frames = new Sprite[frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                // All frames share one texture, so the sprites batch like a real atlas would.
                frames[i] = Sprite.Create(sheet, new Rect(i * frameWidth, 0, frameWidth, sheet.height), new Vector2(0.5f, 0.5f), frameWidth);
            }

            var cam = Camera.main;
            var halfHeight = cam.orthographicSize;
            var halfWidth = halfHeight * cam.aspect;
            var random = new System.Random(7);
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject("Sprite " + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(Range(random, -halfWidth, halfWidth), Range(random, -halfHeight, halfHeight), 0f);
                go.transform.localScale = Vector3.one * Range(random, 0.35f, 0.8f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = frames[0];
                renderer.color = tints[i % tints.Length];
                var mover = go.AddComponent<SwarmSprite>();
                mover.Init(frames, new Vector2(Range(random, -1.5f, 1.5f), Range(random, -1.5f, 1.5f)), Range(random, 8f, 16f), random.Next(frameCount));
            }
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }

    /// <summary>One swarm sprite: flips frames at its own rate, rotates, and bounces off the screen edges.</summary>
    public sealed class SwarmSprite : MonoBehaviour
    {
        private Sprite[] frames;
        private SpriteRenderer spriteRenderer;
        private Vector2 velocity;
        private float fps;
        private float clock;
        private Camera cam;

        public void Init(Sprite[] sheetFrames, Vector2 startVelocity, float framesPerSecond, int startFrame)
        {
            frames = sheetFrames;
            velocity = startVelocity;
            fps = framesPerSecond;
            clock = startFrame / fps;
            spriteRenderer = GetComponent<SpriteRenderer>();
            cam = Camera.main;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            clock += dt;
            spriteRenderer.sprite = frames[(int)(clock * fps) % frames.Length];

            var halfHeight = cam.orthographicSize;
            var halfWidth = halfHeight * cam.aspect;
            var p = (Vector2)transform.localPosition + velocity * dt;
            if (p.x < -halfWidth || p.x > halfWidth)
            {
                velocity.x = -velocity.x;
            }

            if (p.y < -halfHeight || p.y > halfHeight)
            {
                velocity.y = -velocity.y;
            }

            transform.localPosition = p;
            transform.Rotate(0f, 0f, velocity.x * 40f * dt);
        }
    }
}
