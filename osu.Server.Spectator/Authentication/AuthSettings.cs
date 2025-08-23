// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.Spectator.Authentication
{
    public class AuthSettings
    {
        /// <summary>
        /// JWT secret key for HS256 algorithm (compatible with Python g0v0-server)
        /// </summary>
        public string JwtSecretKey { get; set; } = "your_jwt_secret_here";

        /// <summary>
        /// JWT algorithm to use (HS256 for compatibility with Python)
        /// </summary>
        public string Algorithm { get; set; } = "HS256";

        /// <summary>
        /// Access token expiration time in minutes
        /// </summary>
        public int AccessTokenExpireMinutes { get; set; } = 1440;

        /// <summary>
        /// OAuth client ID
        /// </summary>
        public int OsuClientId { get; set; } = 5;

        /// <summary>
        /// Whether to use legacy RSA authentication (false for g0v0-server compatibility)
        /// </summary>
        public bool UseLegacyRsaAuth { get; set; } = false;
    }
}
