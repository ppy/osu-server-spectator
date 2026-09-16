// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator.Hubs.Spectator
{
    [Serializable]
    public class SpectatorClientState : ClientState
    {
        /// <summary>
        /// When a user is in gameplay, this is the state as conveyed at the start of the play session.
        /// </summary>
        /// <remarks>
        /// If <see cref="ScoreTokens"/> contains multiple scores, this property always contains data correct for the most recently started score.
        /// </remarks>
        public SpectatorState? State;

        /// <summary>
        /// When a user is in gameplay, this contains information about the beatmap the user is playing retrieved from the database.
        /// </summary>
        /// <remarks>
        /// If <see cref="ScoreTokens"/> contains multiple scores, this property always contains data correct for the most recently started score.
        /// </remarks>
        public database_beatmap? Beatmap;

        /// <summary>
        /// The score token as conveyed by the client at the beginning of a play session.
        /// </summary>
        [Obsolete]
        public long? ScoreToken;

        public const int MAX_STARTED_SCORES = 2;

        /// <summary>
        /// Tokens of started scores as conveyed by the client at the beginning of a play session.
        /// Only stores up to <see cref="MAX_STARTED_SCORES"/> tokens. On overflows, oldest items are dropped for immediate upload.
        /// Oldest items are at the start of the list; newest items are at the end.
        /// </summary>
        public List<long> ScoreTokens { get; } = new List<long>(MAX_STARTED_SCORES);

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
