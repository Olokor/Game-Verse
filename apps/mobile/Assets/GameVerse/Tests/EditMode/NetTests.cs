#nullable enable
using System.Collections.Generic;
using System.Linq;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Catalog;
using GameVerse.Net;
using GameVerse.Net.Http;
using GameVerse.Platform.Session;
using NUnit.Framework;
using UnityEngine;

namespace GameVerse.Tests
{
    public class ApiClientTests
    {
        [Test]
        public void A_successful_call_sends_the_bearer_token_and_reads_the_server_json()
        {
            var http = new FakeHttp(_ => new HttpResponse(200, "{\"id\":\"ludo\",\"title\":\"Ludo\",\"modes\":[{\"key\":\"ranked\",\"kind\":\"online_ranked\",\"label\":\"Ranked\",\"playerCounts\":[2,4]}]}"));
            var api = new ApiClient("http://api.test/", http, new FixedToken("abc"));

            var game = api.GetAsync<GameInfo>("/catalog/games/ludo").GetAwaiter().GetResult();

            Assert.That(game.Modes[0].PlayerCounts, Is.EqualTo(new[] { 2, 4 }));
            Assert.That(http.Requests[0].Url, Is.EqualTo("http://api.test/catalog/games/ludo"));
            Assert.That(http.Requests[0].Headers["Authorization"], Is.EqualTo("Bearer abc"));
        }

        [Test]
        public void A_validation_problem_becomes_an_ApiException_with_its_field_errors()
        {
            var http = new FakeHttp(_ => new HttpResponse(400, "{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"Room\":[\"Team Ludo needs exactly 4 players.\"]}}"));
            var api = new ApiClient("http://api.test", http, null);

            var error = Assert.Throws<ApiException>(() => api.PostAsync("/rooms", new { }).GetAwaiter().GetResult());

            Assert.That(error!.Status, Is.EqualTo(400));
            Assert.That(error.FieldErrors["Room"][0], Is.EqualTo("Team Ludo needs exactly 4 players."));
            Assert.That(error.PlayerMessage, Is.EqualTo("Team Ludo needs exactly 4 players."));
        }

        [Test]
        public void A_401_renews_the_token_once_and_retries()
        {
            var token = new RenewingToken();
            var http = new FakeHttp(request => request.Headers["Authorization"] == "Bearer fresh" ? new HttpResponse(200, "{}") : new HttpResponse(401, string.Empty));
            var api = new ApiClient("http://api.test", http, token);

            api.PostAsync("/rooms/ABC234/ready", new { ready = true }).GetAwaiter().GetResult();

            Assert.That(http.Requests.Count, Is.EqualTo(2));
            Assert.That(token.Renewals, Is.EqualTo(1));
        }

        [Test]
        public void No_answer_at_all_is_a_network_error_with_a_friendly_message()
        {
            var api = new ApiClient("http://api.test", new FakeHttp(_ => new HttpResponse(0, "Cannot resolve destination host")), null);

            var error = Assert.Throws<ApiException>(() => api.GetAsync<GameInfo>("/catalog/games/ludo").GetAwaiter().GetResult());

            Assert.That(error!.IsNetworkError, Is.True);
            StringAssert.Contains("connection", error.PlayerMessage);
        }

        [Test]
        public void The_api_url_defaults_to_this_pc_when_the_build_did_not_set_one()
        {
            Assert.That(AppConfig.Resolve(null), Is.EqualTo("http://127.0.0.1:8080"));
            var config = ScriptableObject.CreateInstance<AppConfig>();
            config.ApiBaseUrl = " https://api.gameverse.app/ ";
            Assert.That(AppConfig.Resolve(config), Is.EqualTo("https://api.gameverse.app"));
            Object.DestroyImmediate(config);
        }

        private sealed class FixedToken : IAccessTokenSource
        {
            public FixedToken(string token) => AccessToken = token;

            public string? AccessToken { get; }

            public Cysharp.Threading.Tasks.UniTask<bool> RenewAsync(System.Threading.CancellationToken ct) => Cysharp.Threading.Tasks.UniTask.FromResult(false);
        }

        private sealed class RenewingToken : IAccessTokenSource
        {
            public string? AccessToken { get; private set; } = "stale";

            public int Renewals { get; private set; }

            public Cysharp.Threading.Tasks.UniTask<bool> RenewAsync(System.Threading.CancellationToken ct)
            {
                Renewals++;
                AccessToken = "fresh";
                return Cysharp.Threading.Tasks.UniTask.FromResult(true);
            }
        }
    }

    public class PlayerSessionTests
    {
        [Test]
        public void The_first_run_creates_a_guest_and_keeps_its_secret_and_refresh_token()
        {
            var store = new MemoryStore();
            var http = new FakeHttp(request => request.Url.EndsWith("/auth/guest")
                ? Json(new GuestCreatedResponse { UserId = "u1", AccessToken = "a1", RefreshToken = "r1", DeviceSecret = "s1", IsGuest = true })
                : new HttpResponse(500, string.Empty));
            var session = new PlayerSession("http://api.test", http, store);

            session.SignInAsync(default).GetAwaiter().GetResult();

            Assert.That(session.UserId, Is.EqualTo("u1"));
            Assert.That(session.AccessToken, Is.EqualTo("a1"));
            Assert.That(store.Values[PlayerSession.DeviceSecretKey], Is.EqualTo("s1"));
            Assert.That(store.Values[PlayerSession.RefreshTokenKey], Is.EqualTo("r1"));
        }

        [Test]
        public void A_later_run_uses_the_refresh_token_and_falls_back_to_the_device_secret()
        {
            var store = new MemoryStore();
            store.Set(PlayerSession.GuestIdKey, "u1");
            store.Set(PlayerSession.DeviceSecretKey, "s1");
            store.Set(PlayerSession.RefreshTokenKey, "expired");
            var http = new FakeHttp(request =>
                request.Url.EndsWith("/auth/refresh") ? new HttpResponse(401, string.Empty)
                : request.Url.EndsWith("/auth/guest/login") && request.Body!.Contains("\"deviceSecret\":\"s1\"")
                    ? Json(new AuthTokensResponse { UserId = "u1", AccessToken = "a2", RefreshToken = "r2", IsGuest = true })
                    : new HttpResponse(500, string.Empty));
            var session = new PlayerSession("http://api.test", http, store);

            session.SignInAsync(default).GetAwaiter().GetResult();

            Assert.That(session.AccessToken, Is.EqualTo("a2"));
            Assert.That(store.Values[PlayerSession.RefreshTokenKey], Is.EqualTo("r2"), "the rotated refresh token replaces the dead one");
            Assert.That(http.Requests.Select(r => r.Url.Substring("http://api.test".Length)), Is.EqualTo(new[] { "/auth/refresh", "/auth/guest/login" }));
            Assert.That(http.Requests.All(r => !r.Headers.ContainsKey(AuthHeaders.Client)), Is.True, "mobile clients get the refresh token in the body");
        }

        [Test]
        public void The_editor_store_round_trips_values()
        {
            var store = new PlayerPrefsStore("gv.test." + System.Guid.NewGuid().ToString("N") + ".");
            store.Set("k", "v");
            Assert.That(store.Get("k"), Is.EqualTo("v"));
            store.Delete("k");
            Assert.That(store.Get("k"), Is.Null);
        }

        private static HttpResponse Json(object value) => new HttpResponse(200, GameVerseJson.Serialize(value));
    }
}
