// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Helpers
{
    public static class BlobHelper
    {
        public static int[] ParseBlobToIntArray(byte[] blob)
        {
            int[] result = new int[blob.Length / 4];

            for (int i = 0; i < blob.Length; i += 4)
            {
                result.SetValue(BitConverter.ToInt32(blob, i), i / 4);
            }

            return result;
        }

        public static byte[] IntArrayToBlob(int[] array)
        {
            byte[] result = new byte[array.Length * 4];

            for (int i = 0; i < array.Length; i++)
            {
                byte[] intBytes = BitConverter.GetBytes(array[i]);
                Array.Copy(intBytes, 0, result, i * 4, 4);
            }

            return result;
        }
    }
}