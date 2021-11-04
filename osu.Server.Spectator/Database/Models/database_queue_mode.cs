// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.Multiplayer.Queueing;

namespace osu.Server.Spectator.Database.Models
{
    [Serializable]
    // ReSharper disable once InconsistentNaming
    public enum database_queue_mode
    {
        host_only,
        free_for_all,
        fair_rotate
    }

    public static class DatabaseQueueModeExtensions
    {
        public static QueueModes ToQueueMode(this database_queue_mode mode)
        {
            switch (mode)
            {
                default:
                case database_queue_mode.host_only:
                    return QueueModes.HostOnly;

                case database_queue_mode.free_for_all:
                    return QueueModes.FreeForAll;

                case database_queue_mode.fair_rotate:
                    return QueueModes.FairRotate;
            }
        }

        public static database_queue_mode ToDatabaseQueueModes(this QueueModes mode)
        {
            switch (mode)
            {
                default:
                case QueueModes.HostOnly:
                    return database_queue_mode.host_only;

                case QueueModes.FreeForAll:
                    return database_queue_mode.free_for_all;

                case QueueModes.FairRotate:
                    return database_queue_mode.fair_rotate;
            }
        }
    }
}
