// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.Logging;

namespace osu.Server.Spectator.Database
{
    public class DatabaseFactory : IDatabaseFactory
    {
        private readonly ILoggerFactory loggerFactory;

        public DatabaseFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public IDatabaseAccess GetInstance() => new DatabaseAccess(loggerFactory);
    }
}
