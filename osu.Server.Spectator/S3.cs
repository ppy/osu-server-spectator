// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace osu.Server.Spectator
{
    public static class S3
    {
        private static AmazonS3Client getClient(RegionEndpoint? endpoint = null)
        {
            return new AmazonS3Client(new BasicAWSCredentials(AppSettings.S3Key, AppSettings.S3Secret), new AmazonS3Config
            {
                CacheHttpClient = true,
                HttpClientCacheSize = 32,
                RegionEndpoint = endpoint ?? RegionEndpoint.USWest1,
                UseHttp = true,
                ForcePathStyle = true
            });
        }

        public static async Task Upload(string bucket, string key, Stream stream, long contentLength, string? contentType = null)
        {
            using (var client = getClient())
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    Headers =
                    {
                        ContentLength = contentLength,
                        ContentType = contentType,
                    },
                    InputStream = stream
                });
            }
        }
    }
}
