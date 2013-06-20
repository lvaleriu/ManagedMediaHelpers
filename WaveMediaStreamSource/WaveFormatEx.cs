#region

using System;

#endregion

namespace WaveMSS
{
    /// <summary>
    ///   Class WAVEFORMATEX
    ///   Implementation of a standard WAVEFORMATEX structure
    /// </summary>
    public class WAVEFORMATEX
    {
        #region Data

        /// <summary>
        ///   The size of the basic structure
        /// </summary>
        public const uint SizeOf = 18;

        /// <summary>
        ///   The different formats allowable. For now PCM is the only one we support
        /// </summary>
        private const short FormatPCM = 1;

        /// <summary>
        ///   Gets or sets the FormatTag
        /// </summary>
        public short FormatTag { get; set; }

        /// <summary>
        ///   Gets or sets the number of Channels
        /// </summary>
        public short Channels { get; set; }

        /// <summary>
        ///   Gets or sets the number of samples per second
        /// </summary>
        public int SamplesPerSec { get; set; }

        /// <summary>
        ///   Gets or sets the average bytes per second
        /// </summary>
        public int AvgBytesPerSec { get; set; }

        /// <summary>
        ///   Gets or sets the alignment of the blocks
        /// </summary>
        public short BlockAlign { get; set; }

        /// <summary>
        ///   Gets or sets the number of bits per sample (8 or 16)
        /// </summary>
        public short BitsPerSample { get; set; }

        /// <summary>
        ///   Gets or sets the size of the structure
        /// </summary>
        public short Size { get; set; }

        /// <summary>
        ///   Gets or sets the extension buffer
        /// </summary>
        public byte[] Ext { get; set; }

        #endregion Data

        /// <summary>
        ///   Convert a BigEndian string to a LittleEndian string
        /// </summary>
        /// <param name="bigEndianString"> A big endian string </param>
        /// <returns> The little endian string </returns>
        public static string ToLittleEndianString(string bigEndianString)
        {
            if (bigEndianString == null)
            {
                return string.Empty;
            }

            char[] bigEndianChars = bigEndianString.ToCharArray();

            // Guard
            if (bigEndianChars.Length%2 != 0)
            {
                return string.Empty;
            }

            int i, ai, bi, ci, di;
            char a, b, c, d;
            for (i = 0; i < bigEndianChars.Length/2; i += 2)
            {
                // front byte
                ai = i;
                bi = i + 1;

                // back byte
                ci = bigEndianChars.Length - 2 - i;
                di = bigEndianChars.Length - 1 - i;

                a = bigEndianChars[ai];
                b = bigEndianChars[bi];
                c = bigEndianChars[ci];
                d = bigEndianChars[di];

                bigEndianChars[ci] = a;
                bigEndianChars[di] = b;
                bigEndianChars[ai] = c;
                bigEndianChars[bi] = d;
            }

            return new string(bigEndianChars);
        }

        /// <summary>
        ///   Convert the data to a hex string
        /// </summary>
        /// <returns> A string in hexadecimal </returns>
        public string ToHexString()
        {
            string s = string.Empty;

            s += ToLittleEndianString(string.Format("{0:X4}", FormatTag));
            s += ToLittleEndianString(string.Format("{0:X4}", Channels));
            s += ToLittleEndianString(string.Format("{0:X8}", SamplesPerSec));
            s += ToLittleEndianString(string.Format("{0:X8}", AvgBytesPerSec));
            s += ToLittleEndianString(string.Format("{0:X4}", BlockAlign));
            s += ToLittleEndianString(string.Format("{0:X4}", BitsPerSample));
            s += ToLittleEndianString(string.Format("{0:X4}", Size));

            return s;
        }

        /// <summary>
        ///   Set the data from a byte array (usually read from a file)
        /// </summary>
        /// <param name="byteArray"> The array used as input to the stucture </param>
        public void SetFromByteArray(byte[] byteArray)
        {
            if ((byteArray.Length + 2) < SizeOf)
            {
                throw new ArgumentException("Byte array is too small");
            }

            FormatTag = BitConverter.ToInt16(byteArray, 0);
            Channels = BitConverter.ToInt16(byteArray, 2);
            SamplesPerSec = BitConverter.ToInt32(byteArray, 4);
            AvgBytesPerSec = BitConverter.ToInt32(byteArray, 8);
            BlockAlign = BitConverter.ToInt16(byteArray, 12);
            BitsPerSample = BitConverter.ToInt16(byteArray, 14);
            if (byteArray.Length >= SizeOf)
            {
                Size = BitConverter.ToInt16(byteArray, 16);
            }
            else
            {
                Size = 0;
            }

            if (byteArray.Length > SizeOf)
            {
                Ext = new byte[byteArray.Length - SizeOf];
                Array.Copy(byteArray, (int) SizeOf, Ext, 0, Ext.Length);
            }
            else
            {
                Ext = null;
            }
        }

        /// <summary>
        ///   Ouput the data into a string.
        /// </summary>
        /// <returns> A string representing the WAVEFORMATEX </returns>
        public override string ToString()
        {
            var rawData = new char[18];
            BitConverter.GetBytes(FormatTag).CopyTo(rawData, 0);
            BitConverter.GetBytes(Channels).CopyTo(rawData, 2);
            BitConverter.GetBytes(SamplesPerSec).CopyTo(rawData, 4);
            BitConverter.GetBytes(AvgBytesPerSec).CopyTo(rawData, 8);
            BitConverter.GetBytes(BlockAlign).CopyTo(rawData, 12);
            BitConverter.GetBytes(BitsPerSample).CopyTo(rawData, 14);
            BitConverter.GetBytes(Size).CopyTo(rawData, 16);
            return new string(rawData);
        }

        /// <summary>
        ///   Calculate the duration of audio based on the size of the buffer
        /// </summary>
        /// <param name="audioDataSize"> the buffer size in bytes </param>
        /// <returns> The duration of that buffer </returns>
        public long AudioDurationFromBufferSize(uint audioDataSize)
        {
            if (AvgBytesPerSec == 0)
            {
                return 0;
            }

            return (long) audioDataSize*10000000/AvgBytesPerSec;
        }

        /// <summary>
        ///   Calculate the buffer size necessary for a duration of audio
        /// </summary>
        /// <param name="duration"> the duration </param>
        /// <returns> the size of the buffer necessary </returns>
        public long BufferSizeFromAudioDuration(long duration)
        {
            long size = duration*AvgBytesPerSec/10000000;
            var remainder = (uint) (size%BlockAlign);
            if (remainder != 0)
            {
                size += BlockAlign - remainder;
            }

            return size;
        }

        /// <summary>
        ///   Validate that the Wave format is consistent.
        /// </summary>
        public void ValidateWaveFormat()
        {
            if (FormatTag != FormatPCM)
            {
                throw new InvalidOperationException("Only PCM format is supported");
            }

            if (Channels != 1 && Channels != 2)
            {
                throw new InvalidOperationException("Only 1 or 2 channels are supported");
            }

            if (BitsPerSample != 8 && BitsPerSample != 16)
            {
                throw new InvalidOperationException("Only 8 or 16 bit samples are supported");
            }

            if (Size != 0)
            {
                throw new InvalidOperationException("Size must be 0");
            }

            if (BlockAlign != Channels*(BitsPerSample/8))
            {
                throw new InvalidOperationException("Block Alignment is incorrect");
            }

            if (SamplesPerSec > (uint.MaxValue/BlockAlign))
            {
                throw new InvalidOperationException("SamplesPerSec overflows");
            }

            if (AvgBytesPerSec != SamplesPerSec*BlockAlign)
            {
                throw new InvalidOperationException("AvgBytesPerSec is wrong");
            }
        }
    }
}