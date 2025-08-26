// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Authentication
{
    public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly IDatabaseFactory databaseFactory;
        private readonly ILoggerFactory loggerFactory;

        public ConfigureJwtBearerOptions(IDatabaseFactory databaseFactory, ILoggerFactory loggerFactory)
        {
            this.databaseFactory = databaseFactory;
            this.loggerFactory = loggerFactory;
        }

        public void Configure(JwtBearerOptions options)
        {
            SecurityKey signingKey;

            if (AppSettings.UseLegacyRsaAuth)
            {
                // 使用原有的 RSA 公钥验证方式
                var rsa = getKeyProvider();
                signingKey = new RsaSecurityKey(rsa);
            }
            else
            {
                // 使用 HS256 对称密钥验证（与 Python g0v0-server 兼容）
                var secretKey = AppSettings.JwtSecretKey;

                if (string.IsNullOrEmpty(secretKey) || secretKey == "your_jwt_secret_here")
                {
                    throw new InvalidOperationException("JWT Secret Key is required for HS256 authentication. Please set JWT_SECRET_KEY environment variable.");
                }

                var keyBytes = Encoding.UTF8.GetBytes(secretKey);
                signingKey = new SymmetricSecurityKey(keyBytes);
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = signingKey,
                ValidateIssuerSigningKey = true,
                ValidAudience = AppSettings.OsuClientId.ToString(),
                ValidateAudience = true,
                ValidateIssuer = false, // Python 版本不验证 issuer
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5), // 允许 5 分钟的时钟偏差
                RequireExpirationTime = true
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var jwtToken = (JsonWebToken)context.SecurityToken;

                    // 从 sub claim 中获取用户 ID
                    if (!int.TryParse(jwtToken.Subject, out int tokenUserId))
                    {
                        context.Fail("Invalid token format");
                        return;
                    }

                    using (var db = databaseFactory.GetInstance())
                    {
                        // 验证 token 在数据库中是否仍然有效
                        var userId = await db.GetUserIdFromTokenAsync(jwtToken);

                        if (userId != tokenUserId)
                        {
                            context.Fail("Token has expired or been revoked");
                            return;
                        }

                        // 检查用户是否被限制
                        if (await db.IsUserRestrictedAsync(tokenUserId))
                        {
                            context.Fail("User account is restricted");
                            return;
                        }
                    }
                }
            };
        }

        public void Configure(string? name, JwtBearerOptions options)
            => Configure(options);

        /// <summary>
        /// borrowed from https://stackoverflow.com/a/54323524
        /// </summary>
        private static RSACryptoServiceProvider getKeyProvider()
        {
            string key = File.ReadAllText("oauth-public.key");

            key = key.Replace("-----BEGIN PUBLIC KEY-----", "");
            key = key.Replace("-----END PUBLIC KEY-----", "");
            key = key.Replace("\n", "");

            var keyBytes = Convert.FromBase64String(key);

            var asymmetricKeyParameter = PublicKeyFactory.CreateKey(keyBytes);
            var rsaKeyParameters = (RsaKeyParameters)asymmetricKeyParameter;
            var rsaParameters = new RSAParameters { Modulus = rsaKeyParameters.Modulus.ToByteArrayUnsigned(), Exponent = rsaKeyParameters.Exponent.ToByteArrayUnsigned() };

            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(rsaParameters);

            return rsa;
        }
    }
}