#nullable enable
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Accounts;
using GameVerse.Net.Http;

namespace GameVerse.Platform.Session
{
    /// <summary>
    /// The signed-in player (ADR-0006). First run: one tap creates a device-bound guest account, and its id and device secret
    /// are kept in the <see cref="ISecureStore"/>. Later runs sign back in with the stored refresh token, or with the device
    /// secret when that has expired. The access token (15 minutes) lives in memory only. <see cref="Api"/> renews it on a 401.
    /// </summary>
    public sealed class PlayerSession : IAccessTokenSource
    {
        public const string GuestIdKey = "guest.id";
        public const string DeviceSecretKey = "guest.secret";
        public const string RefreshTokenKey = "refresh";

        private readonly ApiClient anonymous;
        private readonly ISecureStore store;
        private readonly SemaphoreSlim renewing = new SemaphoreSlim(1, 1);

        public PlayerSession(string baseUrl, IHttpTransport transport, ISecureStore store)
        {
            this.store = store;
            anonymous = new ApiClient(baseUrl, transport, null);
            Api = new ApiClient(baseUrl, transport, this);
        }

        /// <summary>Signed-in API access (bearer token, renewed on a 401).</summary>
        public ApiClient Api { get; }

        public string? AccessToken { get; private set; }

        /// <summary>The account id (also the guest id), once signed in.</summary>
        public string? UserId { get; private set; }

        public bool IsSignedIn => AccessToken != null;

        /// <summary>Whether this device already has an account to sign back into.</summary>
        public bool HasAccount => store.Get(GuestIdKey) != null && store.Get(DeviceSecretKey) != null;

        /// <summary>Signs in, creating a guest account on the very first run.</summary>
        public async UniTask SignInAsync(CancellationToken ct)
        {
            if (HasAccount)
            {
                if (!await RenewAsync(ct))
                {
                    throw ApiException.FromResponse(401, null);
                }

                return;
            }

            var guest = await anonymous.PostAsync<GuestCreatedResponse>("/auth/guest", null, ct);
            store.Set(GuestIdKey, guest.UserId);
            store.Set(DeviceSecretKey, guest.DeviceSecret);
            Accept(guest);
        }

        /// <summary>A new access token: the refresh token first, the device secret if that fails. False when both fail.</summary>
        public async UniTask<bool> RenewAsync(CancellationToken ct)
        {
            var tokenBefore = AccessToken;
            await renewing.WaitAsync(ct);
            try
            {
                if (AccessToken != tokenBefore)
                {
                    return true; // another caller renewed while this one waited
                }

                if (store.Get(RefreshTokenKey) is { } refresh)
                {
                    try
                    {
                        Accept(await anonymous.PostAsync<AuthTokensResponse>("/auth/refresh", new RefreshRequest { RefreshToken = refresh }, ct));
                        return true;
                    }
                    catch (ApiException ex) when (ex.Status == 401)
                    {
                        store.Delete(RefreshTokenKey); // expired or revoked: fall back to the device secret
                    }
                }

                if (store.Get(GuestIdKey) is { } guestId && store.Get(DeviceSecretKey) is { } secret)
                {
                    try
                    {
                        Accept(await anonymous.PostAsync<AuthTokensResponse>("/auth/guest/login", new GuestLoginRequest { GuestId = guestId, DeviceSecret = secret }, ct));
                        return true;
                    }
                    catch (ApiException ex) when (ex.Status == 401)
                    {
                        // The account was deleted or upgraded elsewhere: the caller shows sign-in again.
                    }
                }

                AccessToken = null;
                return false;
            }
            finally
            {
                renewing.Release();
            }
        }

        /// <summary>Forgets this device's account (account deletion, or "start over" in settings).</summary>
        public void Forget()
        {
            store.Delete(GuestIdKey);
            store.Delete(DeviceSecretKey);
            store.Delete(RefreshTokenKey);
            AccessToken = null;
            UserId = null;
        }

        private void Accept(AuthTokensResponse tokens)
        {
            AccessToken = tokens.AccessToken;
            UserId = tokens.UserId;
            if (tokens.RefreshToken is { } refresh)
            {
                store.Set(RefreshTokenKey, refresh); // rotated on every use
            }
        }
    }
}
