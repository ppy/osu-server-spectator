using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using osu.Framework.Utils;
using osu.Server.Spectator.Hubs;

namespace SampleSpectatorClient
{
    static class Program
    {
        private static HubConnection connection;

        static void Main()
        {
            for (int i = 0; i < 5; i++)
                getConnectedClient();

            var client2 = getConnectedClient();

            while (true)
            {
                client2.BeginPlaying(88);
                Thread.Sleep(1000);

                Console.WriteLine("Writer starting playing..");

                for (int i = 0; i < 50; i++)
                {
                    client2.SendFrames(new FrameDataBundle(RNG.Next(0, 100).ToString()));
                    Thread.Sleep(50);
                }

                Console.WriteLine("Writer ending playing..");

                client2.EndPlaying(88);

                Thread.Sleep(1000);
            }

            // ReSharper disable once FunctionNeverReturns
        }

        private static SpectatorClient getConnectedClient()
        {
            connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5009/spectator")
                .AddMessagePackProtocol()
                .ConfigureLogging(logging => { logging.AddConsole(); })
                .Build();

            var client = new SpectatorClient(connection);

            connection.Closed += async (error) =>
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