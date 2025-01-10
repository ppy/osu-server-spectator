// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace osu.Server.Spectator.Services
{
    public class LegacyIO : ILegacyIO
    {
        private readonly HttpClient httpClient;
        private readonly ILogger logger;

        public LegacyIO(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            this.httpClient = httpClient;
            logger = loggerFactory.CreateLogger("LIO");
        }

        private async Task<string> runLegacyIO(HttpMethod method, string command, dynamic? postObject = null)
        {
            int retryCount = 3;

            retry:

            long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"{AppSettings.LegacyIODomain}/_lio/{command}{(command.Contains('?') ? "&" : "?")}timestamp={time}";

            string? serialisedPostObject = postObject == null ? null : JsonSerializer.Serialize(postObject);
            logger.LogDebug("Performing LIO request to {method} {url} (params: {params})", method, url, serialisedPostObject);

            try
            {
                string signature = hmacEncode(url, Encoding.UTF8.GetBytes(AppSettings.SharedInteropSecret));

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

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Legacy IO request to {url} failed with {response.StatusCode} ({response.Content.ReadAsStringAsync().Result})");

                if ((int)response.StatusCode >= 300)
                    throw new Exception($"Legacy IO request to {url} returned unexpected response {response.StatusCode} ({response.ReasonPhrase})");

                return await response.Content.ReadAsStringAsync();
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

            using (var hmac = new HMACSHA1(key))
            {
                byte[] hashArray = hmac.ComputeHash(byteArray);
                return hashArray.Aggregate(string.Empty, (s, e) => s + $"{e:x2}", s => s);
            }
        }

        // Methods below purposefully async-await on `runLegacyIO()` calls rather than directly returning the underlying calls.
        // This is done for better readability of exception stacks. Directly returning the tasks elides the name of the proxying method.
    }
}
