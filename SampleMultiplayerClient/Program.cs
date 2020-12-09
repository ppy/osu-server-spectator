// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using osu.Framework.Utils;
using osu.Game.Online.RealtimeMultiplayer;

namespace SampleMultiplayerClient
{
    internal static class Program
    {
        private const long room_id = 1234;

        public static async Task Main()
        {
            // ReSharper disable once CollectionNeverQueried.Local
            var clients = new List<MultiplayerClient>();

            for (int i = 1; i < 6; i++)
                clients.Add(getConnectedClient(i));

            while (true)
            {
                Console.WriteLine();

                foreach (var c in clients)
                {
                    Console.WriteLine($"Client {c.UserID}  state: {c.State} room: {c.Room}");
                }

                Console.WriteLine("Usage: <client_id> <command> [params]");
                Console.WriteLine("Valid commands [ JoinRoom LeaveRoom TransferHost ChangeSettings ChangeState StartMatch ]");

                Console.Write(">");

                string input = Console.ReadLine();

                try
                {
                    var pieces = input.Split(' ');

                    if (pieces.Length < 2)
                        continue;

                    var args = pieces.Skip(2).ToArray();

                    MultiplayerClient targetClient = clients.First(c => c.UserID == int.Parse(pieces[0]));

                    switch (pieces[1].ToLower())
                    {
                        case "joinroom":
                            await targetClient.JoinRoom(long.Parse(args[0]));
                            break;

                        case "leaveroom":
                            await targetClient.LeaveRoom();
                            break;

                        case "transferhost":
                            await targetClient.TransferHost(long.Parse(args[0]));
                            break;

                        case "changesettings":
                            await targetClient.ChangeSettings(new MultiplayerRoomSettings { BeatmapID = RNG.Next(0, 65536) });
                            break;

                        case "changestate":
                            await targetClient.ChangeState(Enum.Parse<MultiplayerUserState>(args[0]));
                            break;

                        case "startmatch":
                            await targetClient.StartMatch();
                            break;

                        default:
                            Console.WriteLine("Unknown command");
                            break;
                    }
                }
                catch (HubException e)
                {
                    Console.WriteLine($"Server returned error: {e.Message}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error performing action: {e}");
                }

                Thread.Sleep(50);
                Console.WriteLine("Success!");
            }

            // ReSharper disable once FunctionNeverReturns
        }

        private static MultiplayerClient getConnectedClient(int userId)
        {
            var connection = new HubConnectionBuilder()
                             .AddNewtonsoftJsonProtocol(options => { options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore; })
                             .WithUrl("http://localhost:80/multiplayer", http => http.Headers.Add("user_id", userId.ToString()))
                             .ConfigureLogging(logging =>
                             {
                                 // logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
                                 // logging.AddConsole();
                             })
                             .Build();

            var client = new MultiplayerClient(connection, userId);

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
