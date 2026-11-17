#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GameVerse.Net.Http
{
    /// <summary>
    /// A failed API call. The server answers errors as RFC 7807 problem+json (title, detail, and field errors for 400s);
    /// a status of 0 means the request never got an answer (offline, timeout, server down).
    /// </summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int status, string title, string? detail, IReadOnlyDictionary<string, string[]> fieldErrors)
            : base(status == 0 ? title + ": " + detail : status + " " + title + (detail is null ? string.Empty : ": " + detail))
        {
            Status = status;
            Title = title;
            Detail = detail;
            FieldErrors = fieldErrors;
        }

        /// <summary>HTTP status, or 0 for a network failure.</summary>
        public int Status { get; }

        public string Title { get; }

        public string? Detail { get; }

        /// <summary>Field name to messages, from a 400 validation problem.</summary>
        public IReadOnlyDictionary<string, string[]> FieldErrors { get; }

        public bool IsNetworkError => Status == 0;

        /// <summary>The best single sentence to show a player.</summary>
        public string PlayerMessage
        {
            get
            {
                foreach (var messages in FieldErrors.Values)
                {
                    if (messages.Length > 0)
                    {
                        return messages[0];
                    }
                }

                return IsNetworkError ? "Can't reach GameVerse. Check your connection and try again." : Detail ?? Title;
            }
        }

        /// <summary>Builds the exception from a response body (problem+json when the server wrote one).</summary>
        public static ApiException FromResponse(int status, string? body)
        {
            var title = status switch
            {
                401 => "Signed out",
                403 => "Not allowed",
                404 => "Not found",
                409 => "Conflict",
                410 => "Gone",
                429 => "Too many requests",
                _ => status >= 500 ? "Server error" : "Request failed",
            };
            string? detail = null;
            var fields = new Dictionary<string, string[]>();
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var problem = JObject.Parse(body!);
                    title = (string?)problem["title"] ?? title;
                    detail = (string?)problem["detail"];
                    if (problem["errors"] is JObject errors)
                    {
                        foreach (var property in errors.Properties())
                        {
                            fields[property.Name] = property.Value.ToObject<string[]>() ?? Array.Empty<string>();
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Not problem+json (a proxy page, say): keep the status-based title.
                }
            }

            return new ApiException(status, title, detail, fields);
        }

        public static ApiException Network(string reason) =>
            new ApiException(0, "Network error", reason, new Dictionary<string, string[]>());
    }
}
