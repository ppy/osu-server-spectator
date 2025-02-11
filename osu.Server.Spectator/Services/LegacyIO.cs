// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Services
{
    public class LegacyIO : ILegacyIO
    {
        private readonly HttpClient httpClient;
        private readonly ILogger logger;

        private readonly string interopDomain;
        private readonly string interopSecret;

        public LegacyIO(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            this.httpClient = httpClient;
            logger = loggerFactory.CreateLogger("LIO");

            interopDomain = AppSettings.LegacyIODomain
                            ?? throw new InvalidOperationException("LEGACY_IO_DOMAIN environment variable not set. "
                                                                   + "Please set the value of this variable to the root URL of the osu-web instance to which legacy IO call should be submitted.");
            interopSecret = AppSettings.SharedInteropSecret
                            ?? throw new InvalidOperationException("SHARED_INTEROP_SECRET environment variable not set. "
                                                                   + "Please set the value of this variable to the value of the same environment variable that the target osu-web instance specifies in `.env`.");
        }

        private async Task<string> runLegacyIO(HttpMethod method, string command, dynamic? postObject = null)
        {
            int retryCount = 3;

            retry:

            long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"{interopDomain}/_lio/{command}{(command.Contains('?') ? "&" : "?")}timestamp={time}";

            string? serialisedPostObject;

            switch (postObject)
            {
                case null:
                    serialisedPostObject = null;
                    break;

                case string:
                    serialisedPostObject = postObject;
                    break;

                default:
                    serialisedPostObject = JsonSerializer.Serialize(postObject);
                    break;
            }

            logger.LogDebug("Performing LIO request to {method} {url} (params: {params})", method, url, serialisedPostObject);

            try
            {
                string signature = hmacEncode(url, Encoding.UTF8.GetBytes(interopSecret));

                var httpRequestMessage = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = method,
                    Headers =
                    {
                        { "X-LIO-Signature", signature },
                        { "Accept", "application/json" },
                    },
                };

                if (serialisedPostObject != null)
                {
                    httpRequestMessage.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(serialisedPostObject));
                    httpRequestMessage.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                }

                var response = await httpClient.SendAsync(httpRequestMessage);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                throw await LegacyIORequestFailedException.Create(url, response);
            }
            catch (Exception e)
            {
                if (retryCount-- > 0)
                {
                    logger.LogError(e, "Legacy IO request to {url} failed, retrying ({retries} remaining)", url, retryCount);
                    Thread.Sleep(1000);
                    goto retry;
                }

                throw;
            }
        }

        private static string hmacEncode(string input, byte[] key)
        {
            byte[] byteArray = Encoding.ASCII.GetBytes(input);

            byte[] hashArray = HMACSHA1.HashData(key, byteArray);
            return hashArray.Aggregate(string.Empty, (s, e) => s + $"{e:x2}", s => s);
        }

        // Methods below purposefully async-await on `runLegacyIO()` calls rather than directly returning the underlying calls.
        // This is done for better readability of exception stacks. Directly returning the tasks elides the name of the proxying method.

        public async Task<long> CreateRoomAsync(int hostUserId, MultiplayerRoom room)
        {
            return long.Parse(await runLegacyIO(HttpMethod.Post, "multiplayer/rooms", Newtonsoft.Json.JsonConvert.SerializeObject(new RoomWithHostId(room)
            {
                HostUserId = hostUserId
            })));
        }

        public async Task AddUserToRoomAsync(long roomId, int userId)
        {
            await runLegacyIO(HttpMethod.Put, $"multiplayer/rooms/{roomId}/users/{userId}");
        }

        public async Task RemoveUserFromRoomAsync(long roomId, int userId)
        {
            await runLegacyIO(HttpMethod.Delete, $"multiplayer/rooms/{roomId}/users/{userId}");
        }

        private class RoomWithHostId : Room
        {
            [Newtonsoft.Json.JsonProperty("user_id")]
            public required int HostUserId { get; init; }

            /// <summary>
            /// Creates a <see cref="Room"/> from a <see cref="MultiplayerRoom"/>.
            /// </summary>
            public RoomWithHostId(MultiplayerRoom room)
            {
                RoomID = room.RoomID;
                Host = room.Host?.User;
                Name = room.Settings.Name;
                Password = room.Settings.Password;
                Type = room.Settings.MatchType;
                QueueMode = room.Settings.QueueMode;
                AutoStartDuration = room.Settings.AutoStartDuration;
                AutoSkip = room.Settings.AutoSkip;
                Playlist = room.Playlist.Select(item => new PlaylistItem(item)).ToArray();
            }
        }

        [Serializable]
        private class LegacyIORequestFailedException : HubException
        {
            private LegacyIORequestFailedException(string message, Exception innerException)
                : base(message, innerException)
            {
            }

            public static async Task<LegacyIORequestFailedException> Create(string url, HttpResponseMessage response)
            {
                string errorMessage = $"{(int)response.StatusCode}: {response.ReasonPhrase}";

                try
                {
                    APIErrorMessage? apiError = await JsonSerializer.DeserializeAsync<APIErrorMessage>(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
                    if (!string.IsNullOrEmpty(apiError?.Error))
                        errorMessage = apiError.Error;
                }
                catch
                {
                }

                // Outer exception message is serialised to clients, inner exception is logged to the server and NOT serialised to the client.
                return new LegacyIORequestFailedException(errorMessage, new Exception($"Legacy IO request to {url} failed with {response.StatusCode} ({response.ReasonPhrase})."));
            }

            [Serializable]
            private class APIErrorMessage
            {
                [JsonPropertyName("error")]
                public string Error { get; set; } = string.Empty;
            }
        }
    }
}
