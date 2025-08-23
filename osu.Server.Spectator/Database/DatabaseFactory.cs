// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.Extensions.Logging;
using osu.Server.Spectator.Services;

namespace osu.Server.Spectator.Database
{
    public class DatabaseFactory : IDatabaseFactory
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly ISharedInterop sharedInterop;

        public DatabaseFactory(ILoggerFactory loggerFactory, ISharedInterop sharedInterop)
        {
            this.loggerFactory = loggerFactory;
            this.sharedInterop = sharedInterop;
        }

        public IDatabaseAccess GetInstance() => new DatabaseAccess(loggerFactory, sharedInterop);
    }
}
