// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Metadata;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs
{
    public class MetadataHub : LoggingHub<IMetadataClient>, IMetadataServer
    {
        private readonly IDatabaseFactory databaseFactory;

        public MetadataHub(IDatabaseFactory databaseFactory)
        {
            this.databaseFactory = databaseFactory;
        }

        public async Task<BeatmapUpdates> GetChangesSince(uint queueId)
        {
            using (var db = databaseFactory.GetInstance())
                return await db.GetUpdatedBeatmapSets(queueId);
        }
    }
}
