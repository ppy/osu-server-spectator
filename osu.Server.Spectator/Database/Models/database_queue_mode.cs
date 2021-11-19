// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Database.Models
{
    // ReSharper disable once InconsistentNaming
    [Serializable]
    public enum database_queue_mode
    {
        host_only,
        free_for_all,
        fair_rotate
    }

    public static class DatabaseQueueModeExtensions
    {
        public static QueueMode ToQueueMode(this database_queue_mode mode)
        {
            switch (mode)
            {
                case database_queue_mode.host_only:
                    return QueueMode.HostOnly;

                case database_queue_mode.free_for_all:
                    return QueueMode.FreeForAll;

                case database_queue_mode.fair_rotate:
                    return QueueMode.FairRotate;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public static database_queue_mode ToDatabaseQueueMode(this QueueMode mode)
        {
            switch (mode)
            {
                case QueueMode.HostOnly:
                    return database_queue_mode.host_only;

                case QueueMode.FreeForAll:
                    return database_queue_mode.free_for_all;

                case QueueMode.FairRotate:
                    return database_queue_mode.fair_rotate;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
