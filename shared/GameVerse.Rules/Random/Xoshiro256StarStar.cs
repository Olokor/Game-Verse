using System;
using System.Collections.Generic;
using GameVerse.Rules.Math;

namespace GameVerse.Rules.Random
{
    /// <summary>
    /// The project's deterministic PRNG: xoshiro256** (Blackman and Vigna), seeded through SplitMix64.
    /// Same seed, same sequence, on every platform. Use this instead of System.Random everywhere in rules.
    /// <para>Not thread-safe; each match owns its own instance.</para>
    /// </summary>
    public sealed class Xoshiro256StarStar
    {
        private ulong s0;
        private ulong s1;
        private ulong s2;
        private ulong s3;

        /// <summary>Initializes a new instance of the <see cref="Xoshiro256StarStar"/> class from a 64-bit seed, expanded with SplitMix64.</summary>
        /// <param name="seed">Any 64-bit seed, including 0.</param>
        public Xoshiro256StarStar(ulong seed)
        {
            ulong x = seed;
            s0 = SplitMix64(ref x);
            s1 = SplitMix64(ref x);
            s2 = SplitMix64(ref x);
            s3 = SplitMix64(ref x);
        }

        private Xoshiro256StarStar(ulong a, ulong b, ulong c, ulong d)
        {
            s0 = a;
            s1 = b;
            s2 = c;
            s3 = d;
        }

        /// <summary>Restores a generator from a state captured with <see cref="GetState"/>.</summary>
        /// <param name="state">Exactly four words, not all zero.</param>
        /// <returns>A generator that continues the captured sequence.</returns>
        public static Xoshiro256StarStar FromState(IReadOnlyList<ulong> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.Count != 4)
            {
                throw new ArgumentException("xoshiro256** state must have exactly 4 words.", nameof(state));
            }

            if ((state[0] | state[1] | state[2] | state[3]) == 0)
            {
                throw new ArgumentException("xoshiro256** state must not be all zero.", nameof(state));
            }

            return new Xoshiro256StarStar(state[0], state[1], state[2], state[3]);
        }

        /// <summary>Captures the full generator state, e.g. for a match snapshot.</summary>
        /// <returns>A copy of the four state words.</returns>
        public ulong[] GetState() => new[] { s0, s1, s2, s3 };

        /// <summary>Next raw 64-bit output.</summary>
        /// <returns>A uniformly distributed 64-bit value.</returns>
        public ulong NextULong()
        {
            ulong result = RotateLeft(unchecked(s1 * 5), 7) * 9;
            ulong t = s1 << 17;

            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = RotateLeft(s3, 45);

            return result;
        }

        /// <summary>Uniform integer in [0, maxExclusive), without modulo bias.</summary>
        /// <param name="maxExclusive">Upper bound, at least 1.</param>
        /// <returns>The random integer.</returns>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");
            }

            ulong bound = (ulong)maxExclusive;

            // Reject the lowest (2^64 mod bound) outputs so every residue is equally likely.
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong r = NextULong();
                if (r >= threshold)
                {
                    return (int)(r % bound);
                }
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        /// <param name="minInclusive">Lower bound.</param>
        /// <param name="maxExclusive">Upper bound, greater than minInclusive.</param>
        /// <returns>The random integer.</returns>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be greater than minInclusive.");
            }

            long range = (long)maxExclusive - minInclusive;
            ulong bound = (ulong)range;
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong r = NextULong();
                if (r >= threshold)
                {
                    return (int)(minInclusive + (long)(r % bound));
                }
            }
        }

        /// <summary>Uniform fixed-point value in [0, 1), using the top 32 bits of the next output.</summary>
        /// <returns>The random fraction.</returns>
        public Fixed NextFixed() => Fixed.FromRaw((long)(NextULong() >> 32));

        /// <summary>In-place Fisher-Yates shuffle.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="items">List to shuffle.</param>
        public void Shuffle<T>(IList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
