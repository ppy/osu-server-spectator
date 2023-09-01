// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using osu.Framework.Utils;
using osu.Game.Online;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace SampleSpectatorClient
{
    internal static class Program
    {
        public static async Task Main()
        {
            // ReSharper disable once CollectionNeverQueried.Local
            var clients = new List<SpectatorClient>();

            for (int i = 0; i < 5; i++)
                clients.Add(getConnectedClient());

            var sendingClient = getConnectedClient();

            while (true)
            {
                await sendingClient.BeginPlaying(new ScoreToken(0, ScoreTokenType.Solo), new SpectatorState { BeatmapID = 88 });

                Thread.Sleep(1000);

                Console.WriteLine("Writer starting playing..");

                for (int i = 0; i < 50; i++)
                {
                    await sendingClient.SendFrames(new FrameDataBundle(
                        new FrameHeader(new ScoreInfo(), new ScoreProcessorStatistics()),
                        new[]
                        {
                            new LegacyReplayFrame(i, RNG.Next(0, 512), RNG.Next(0, 512), ReplayButtonState.None)
                        }));
                    Thread.Sleep(50);
                }

                Console.WriteLine("Writer ending playing..");

                await sendingClient.EndPlaying(new SpectatorState { BeatmapID = 88 });

                Thread.Sleep(1000);
            }

            // ReSharper disable once FunctionNeverReturns
        }

        private static SpectatorClient getConnectedClient()
        {
            var connection = new HubConnectionBuilder()
                             .AddMessagePackProtocol(options => { options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS; })
                             .WithUrl("http://localhost:80/spectator")
                             .ConfigureLogging(logging =>
                             {
                                 logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
                                 logging.AddConsole();
                             })
                             .Build();

            var client = new SpectatorClient(connection);

            connection.Closed += async error =>
            {
                Console.WriteLine($"Connection closed with error:{error}");

                await connection.StartAsync();
            };

            connection.Reconnected += id =>
            {
                Console.WriteLine($"Connected with id:{id}");
                return Task.CompletedTask;
            };

            while (true)
            {
                try
                {
                    connection.StartAsync().Wait();
                    break;
                }
                catch
                {
                    // try until connected
                }
            }

            Console.WriteLine($"client {connection.ConnectionId} connected!");

            return client;
        }
    }
}
