// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Spectator
{
    [Serializable]
    public class SpectatorClientState : ClientState
    {
        /// <summary>
        /// When a user is in gameplay, this is the state as conveyed at the start of the play session.
        /// </summary>
        public SpectatorState? State;

        /// <summary>
        /// When a user is in gameplay, this contains information about the beatmap the user is playing retrieved from the database.
        /// </summary>
        public database_beatmap? Beatmap;

        /// <summary>
        /// When a user is in gameplay, this is the imminent score. It will be updated throughout a play session.
        /// </summary>
        public Score? Score;

        /// <summary>
        /// The score token as conveyed by the client at the beginning of a play session.
        /// </summary>
        public long? ScoreToken;

        /// <summary>
        /// The list of IDs of users that this client is currently watching.
        /// </summary>
        public HashSet<int> WatchedUsers = new HashSet<int>();

        [JsonConstructor]
        public SpectatorClientState(in string connectionId, in int userId)
            : base(connectionId, userId)
        {
        }
    }
}
