// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.DependencyInjection;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Storage;

namespace osu.Server.Spectator.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHubEntities(this IServiceCollection serviceCollection)
        {
            return serviceCollection.AddSingleton<EntityStore<SpectatorClientState>>()
                                    .AddSingleton<EntityStore<MultiplayerClientState>>()
                                    .AddSingleton<EntityStore<ServerMultiplayerRoom>>()
                                    .AddSingleton<GracefulShutdownManager>()
                                    .AddSingleton<MetadataBroadcaster>()
                                    .AddSingleton<IScoreStorage, FileScoreStorage>()
                                    .AddSingleton<ScoreUploader>();
        }

        public static IServiceCollection AddDatabaseServices(this IServiceCollection serviceCollection)
        {
            return serviceCollection.AddSingleton<IDatabaseFactory, DatabaseFactory>();
        }
    }
}
