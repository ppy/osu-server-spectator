using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace SampleSpectatorClient
{
    static class Program
    {
        private static HubConnection connection;

        static void Main()
        {
            var _ = getConnectedClient();
            
            var client2 = getConnectedClient();

            while (true)
            {
                Console.ReadLine();

                Console.WriteLine("Writer starting playing..");
                client2.BeginPlaying(88);
                Thread.Sleep(1000);
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

            connection.StartAsync();

            return client;
        }
    }
}