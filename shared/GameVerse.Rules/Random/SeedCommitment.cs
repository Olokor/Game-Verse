using System;
using System.Security.Cryptography;
using System.Text;

namespace GameVerse.Rules.Random
{
    /// <summary>
    /// Commit-reveal for provably fair randomness (Ludo dice, Whot shuffles).
    /// <list type="number">
    /// <item>The server generates a 32-byte secret with a cryptographic RNG (server side, never in this library).</item>
    /// <item>Before the match it publishes <see cref="Commit"/>(secret).</item>
    /// <item>The match PRNG is seeded with <see cref="DeriveSeed"/>(secret).</item>
    /// <item>After the match it reveals the secret; anyone can run <see cref="Verify"/> and replay the dice.</item>
    /// </list>
    /// </summary>
    public static class SeedCommitment
    {
        /// <summary>Required secret length in bytes.</summary>
        public const int SecretLength = 32;

        private static readonly byte[] SeedDomain = Encoding.ASCII.GetBytes("gameverse-seed-v1");

        /// <summary>Commitment published before the match: lowercase hex SHA-256 of the secret.</summary>
        /// <param name="secret">32-byte secret.</param>
        /// <returns>64-character lowercase hex string.</returns>
        public static string Commit(byte[] secret)
        {
            CheckSecret(secret);
            return ToHex(Sha256(secret));
        }

        /// <summary>Seed for <see cref="Xoshiro256StarStar"/>: first 8 bytes, little-endian, of SHA-256("gameverse-seed-v1" || secret).</summary>
        /// <param name="secret">32-byte secret.</param>
        /// <returns>The 64-bit seed.</returns>
        public static ulong DeriveSeed(byte[] secret)
        {
            CheckSecret(secret);
            byte[] input = new byte[SeedDomain.Length + secret.Length];
            Buffer.BlockCopy(SeedDomain, 0, input, 0, SeedDomain.Length);
            Buffer.BlockCopy(secret, 0, input, SeedDomain.Length, secret.Length);
            byte[] hash = Sha256(input);

            ulong seed = 0;
            for (int i = 7; i >= 0; i--)
            {
                seed = (seed << 8) | hash[i];
            }

            return seed;
        }

        /// <summary>Checks a revealed secret against the commitment published before the match.</summary>
        /// <param name="commitment">Hex commitment (case-insensitive).</param>
        /// <param name="secret">Revealed secret.</param>
        /// <returns>True when the secret matches.</returns>
        public static bool Verify(string commitment, byte[] secret)
        {
            if (commitment == null || secret == null || secret.Length != SecretLength)
            {
                return false;
            }

            return string.Equals(commitment, Commit(secret), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>SHA-256 of arbitrary bytes, exposed for test vectors.</summary>
        /// <param name="data">Input bytes.</param>
        /// <returns>The 32-byte hash.</returns>
        public static byte[] Sha256(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        /// <summary>Lowercase hex encoding.</summary>
        /// <param name="bytes">Bytes to encode.</param>
        /// <returns>Lowercase hex string.</returns>
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            const string digits = "0123456789abcdef";
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[(i * 2) + 1] = digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }

        private static void CheckSecret(byte[] secret)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }

            if (secret.Length != SecretLength)
            {
                throw new ArgumentException("Secret must be exactly " + SecretLength + " bytes.", nameof(secret));
            }
        }
    }
}
