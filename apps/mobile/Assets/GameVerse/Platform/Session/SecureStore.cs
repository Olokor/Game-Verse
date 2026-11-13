#nullable enable
using System;
using UnityEngine;

namespace GameVerse.Platform.Session
{
    /// <summary>Small secrets that keep a player signed in: the guest device secret and the refresh token.</summary>
    public interface ISecureStore
    {
        string? Get(string key);

        void Set(string key, string value);

        void Delete(string key);
    }

    /// <summary>The store for this platform: Android Keystore encryption on devices, plain PlayerPrefs in the editor.</summary>
    public static class SecureStores
    {
        public static ISecureStore ForThisPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidKeystoreStore();
#else
            return new PlayerPrefsStore("gv.dev.");
#endif
        }
    }

    /// <summary>
    /// Unencrypted PlayerPrefs. Used in the editor and desktop development only, where the developer owns the machine;
    /// tests use it with their own prefix.
    /// </summary>
    public sealed class PlayerPrefsStore : ISecureStore
    {
        private readonly string prefix;

        public PlayerPrefsStore(string prefix) => this.prefix = prefix;

        public string? Get(string key) => PlayerPrefs.HasKey(prefix + key) ? PlayerPrefs.GetString(prefix + key) : null;

        public void Set(string key, string value)
        {
            PlayerPrefs.SetString(prefix + key, value);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(prefix + key);
            PlayerPrefs.Save();
        }
    }

#if UNITY_ANDROID
    /// <summary>
    /// AES-256-GCM with a key that never leaves the Android Keystore (hardware-backed where the phone has it). Values are
    /// stored in PlayerPrefs as base64(iv + ciphertext): another app, or a copy of the prefs file, cannot read them.
    /// If the key is lost (app data cleared, device restore), reads return null and the player starts as a new guest.
    /// </summary>
    public sealed class AndroidKeystoreStore : ISecureStore
    {
        private const string Alias = "gameverse.session";
        private const string Prefix = "gv.secure.";
        private const int EncryptMode = 1;
        private const int DecryptMode = 2;

        public string? Get(string key)
        {
            var stored = PlayerPrefs.GetString(Prefix + key, string.Empty);
            if (stored.Length == 0)
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(stored);
                var iv = new byte[12];
                var cipherText = new byte[bytes.Length - iv.Length];
                Buffer.BlockCopy(bytes, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(bytes, iv.Length, cipherText, 0, cipherText.Length);
                using var spec = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, ToJava(iv));
                using var cipher = Cipher();
                cipher.Call("init", DecryptMode, Key(), spec);
                return System.Text.Encoding.UTF8.GetString(FromJava(cipher.Call<sbyte[]>("doFinal", ToJava(cipherText))));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("GV_SECURESTORE could not read " + key + ": " + ex.GetType().Name);
                return null;
            }
        }

        public void Set(string key, string value)
        {
            using var cipher = Cipher();
            cipher.Call("init", EncryptMode, Key());
            var iv = FromJava(cipher.Call<sbyte[]>("getIV"));
            var cipherText = FromJava(cipher.Call<sbyte[]>("doFinal", ToJava(System.Text.Encoding.UTF8.GetBytes(value))));
            var bytes = new byte[iv.Length + cipherText.Length];
            Buffer.BlockCopy(iv, 0, bytes, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, bytes, iv.Length, cipherText.Length);
            PlayerPrefs.SetString(Prefix + key, Convert.ToBase64String(bytes));
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }

        private static AndroidJavaObject Cipher() =>
            new AndroidJavaClass("javax.crypto.Cipher").CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding");

        private static AndroidJavaObject Key()
        {
            using var keyStore = new AndroidJavaClass("java.security.KeyStore").CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
            keyStore.Call("load", (object?)null);
            if (!keyStore.Call<bool>("containsAlias", Alias))
            {
                // PURPOSE_ENCRYPT | PURPOSE_DECRYPT = 3. Varargs String[] parameters are passed wrapped in an object[].
                using var builder = new AndroidJavaObject("android.security.keystore.KeyGenParameterSpec$Builder", Alias, 3);
                builder.Call<AndroidJavaObject>("setBlockModes", new object[] { new[] { "GCM" } }).Dispose();
                builder.Call<AndroidJavaObject>("setEncryptionPaddings", new object[] { new[] { "NoPadding" } }).Dispose();
                builder.Call<AndroidJavaObject>("setKeySize", 256).Dispose();
                using var spec = builder.Call<AndroidJavaObject>("build");
                using var generator = new AndroidJavaClass("javax.crypto.KeyGenerator").CallStatic<AndroidJavaObject>("getInstance", "AES", "AndroidKeyStore");
                generator.Call("init", spec);
                generator.Call<AndroidJavaObject>("generateKey").Dispose();
            }

            return keyStore.Call<AndroidJavaObject>("getKey", Alias, null);
        }

        private static sbyte[] ToJava(byte[] bytes)
        {
            var result = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        private static byte[] FromJava(sbyte[] bytes)
        {
            var result = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
#endif
}
