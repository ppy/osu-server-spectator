using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Framework.Utils;
using osu.Server.Spectator.Hubs;

namespace SampleSpectatorClient
{
    static class Program
    {
        private static HubConnection connection;

        static void Main()
        {
            var _ = getConnectedClient();
            Console.WriteLine("client 1 connected");

            var client2 = getConnectedClient();
            Console.WriteLine("client 2 connected");

            while (true)
            {
                client2.BeginPlaying(88);
                Thread.Sleep(1000);

                Console.WriteLine("Writer starting playing..");
                for (int i = 0; i < 100; i++)
                {
                    client2.SendFrames(new FrameDataBundle(RNG.Next(0, 100).ToString()));
                    Thread.Sleep(50);
                }

                Console.WriteLine("Writer ending playing..");

                client2.EndPlaying(88);
            }

            // ReSharper disable once FunctionNeverReturns
        }

        private static SpectatorClient getConnectedClient()
        {
            connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5009/spectator")
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
                }
            }

            return client;
        }
    }
}