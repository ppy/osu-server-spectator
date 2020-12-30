// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs
{
    [Serializable]
    public class ClientState
    {
        /// <summary>
        /// The connection ID of the owner of this state.
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// The user ID of the owner of this state.
        /// </summary>
        public int UserId { get; set; }

        public ClientState(in string connectionId, in int userId)
        {
            UserId = userId;
            ConnectionId = connectionId;
        }
    }
}
