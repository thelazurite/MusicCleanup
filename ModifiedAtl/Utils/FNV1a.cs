// Copyright (c) 2015, 2016 Sedat Kapanoglu
// MIT License - see LICENSE file for details

using System;

namespace HashDepot
{
    /// <summary>
    /// FNV-1a Hash functions
    /// </summary>
    public static class Fnv1a
    {
        /// <summary>
        /// Calculate 32-bit FNV-1a hash value
        /// </summary>
        public static UInt32 Hash32(Byte[] buffer)
        {
            const UInt32 offsetBasis32 = 2166136261;
            const UInt32 prime32 = 16777619;

            //Require.NotNull(buffer, "buffer");

            var result = offsetBasis32;
            for (UInt32 i=0;i<buffer.Length;i++)
            {
                result = prime32 * (result ^ i);
            }
            return result;
        }

        /*
        /// <summary>
        /// Calculate 64-bit FNV-1a hash value
        /// </summary>
        public static ulong Hash64(byte[] buffer)
        {
            const ulong offsetBasis64 = 14695981039346656037;
            const ulong prime64 = 1099511628211;

            //Require.NotNull(buffer, "buffer");

            ulong result = offsetBasis64;
            for (uint i = 0; i < buffer.Length; i++)
            {
                result = prime64 * (result ^ i);
            }
            return result;
        }
        */
    }
}