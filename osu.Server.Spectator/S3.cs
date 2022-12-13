// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace osu.Server.Spectator
{
    public static class S3
    {
        public static byte[]? Retrieve(string bucket, string key, RegionEndpoint? endpoint = null)
        {
            using (var client = getClient(endpoint))
            {
                try
                {
                    var obj = client.GetObjectAsync(bucket, key).Result;

                    using (var memory = new MemoryStream())
                    {
                        obj.ResponseStream.CopyTo(memory);
                        return memory.ToArray();
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static bool CheckExists(string bucket, string key)
        {
            using (var client = getClient())
            {
                try
                {
                    var obj = client.GetObjectAsync(bucket, key).Result;
                    return obj?.ContentLength > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Delete(string bucket, string key)
        {
            using (var client = getClient())
            {
                client.DeleteObjectAsync(bucket, key).Wait();
            }
        }

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

        public static List<string> List(string bucket, string prefix)
        {
            using (var client = getClient())
            {
                return client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = prefix
                }).Result.S3Objects.Select(o => o.Key).ToList();
            }
        }

        public static void Upload(string bucket, string key, Stream stream, long contentLength, string? contentType = null)
        {
            using (var client = getClient())
            {
                client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    CannedACL = S3CannedACL.PublicRead,
                    Headers =
                    {
                        ContentLength = contentLength,
                        ContentType = contentType,
                    },
                    InputStream = stream
                }).Wait();
            }
        }
    }
}
