using System;
using System.IO;
using System.Text;

namespace ATL
{
	/// <summary>
	/// Misc. utilities used by binary readers
	/// </summary>
	public static class StreamUtils
	{	
		// Size of the buffer used by memory stream copy methods
		private const Int32 BUFFERSIZE = 512;

        /// <summary>
        /// Handler signature to be used when needing to process a MemoryStream
        /// </summary>
        public delegate void StreamHandlerDelegate(ref MemoryStream stream);


		/// <summary>
		/// Determines if the contents of a string (character by character) is the same
		/// as the contents of a char array
		/// </summary>
		/// <param name="a">String to be tested</param>
		/// <param name="b">Char array to be tested</param>
		/// <returns>True if both contain the same character sequence; false if not</returns>
		public static Boolean StringEqualsArr(String a, Char[] b)
		{
            return ArrEqualsArr(a.ToCharArray(), b);
		}


		/// <summary>
		/// Determines if two char arrays have the same contents
		/// </summary>
		/// <param name="a">First array to be tested</param>
		/// <param name="b">Second array to be tested</param>
		/// <returns>True if both arrays have the same contents; false if not</returns>
		private static Boolean ArrEqualsArr(Char[] a, Char[] b)
		{			
			if (b.Length != a.Length) return false;
			for (var i=0; i<b.Length; i++)
			{
				if (a[i] != b[i]) return false;
			}
			return true;
		}

        /// <summary>
        /// Determines if two byte arrays have the same contents
        /// </summary>
        /// <param name="a">First array to be tested</param>
        /// <param name="b">Second array to be tested</param>
        /// <returns>True if both arrays have the same contents; false if not</returns>
        public static Boolean ArrEqualsArr(Byte[] a, Byte[] b)
        {
            if (b.Length != a.Length) return false;
            for (var i = 0; i < b.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Copies a given number of bytes from a given stream to another, starting at current stream positions
        /// i.e. first byte will be read at from.Position and written at to.Position
        /// NB : This method cannot be used to move data within one single stream; use CopySameStream instead
        /// </summary>
        /// <param name="from">Stream to start copy from</param>
        /// <param name="to">Stream to copy to</param>
        /// <param name="length">Number of bytes to copy (optional; default = 0 = all bytes until the end of the stream)</param>
        public static void CopyStream(Stream from, Stream to, Int32 length = 0)
        {
            var data = new Byte[BUFFERSIZE];
            Int32 bytesToRead;
            var totalBytesRead = 0;

            while (true)
            {
                if (length > 0)
                {
                    if (totalBytesRead + BUFFERSIZE < length) bytesToRead = BUFFERSIZE; else bytesToRead = length - totalBytesRead;
                }
                else
                {
                    bytesToRead = BUFFERSIZE;
                }
                var bytesRead = from.Read(data, 0, bytesToRead);
                if (bytesRead == 0)
                {
                    break;
                }
                to.Write(data, 0, bytesRead);
                totalBytesRead += bytesRead;
            }
        }

        public static void CopySameStream(Stream s, Int64 offsetFrom, Int64 offsetTo, Int32 length, Int32 bufferSize = BUFFERSIZE)
        {
            if (offsetFrom == offsetTo) return;

            var data = new Byte[bufferSize];
            Int32 bufSize;
            var written = 0;
            var forward = (offsetTo > offsetFrom);

            while (written < length)
            {
                bufSize = Math.Min(bufferSize, length - written);
                if (forward)
                {
                    s.Seek(offsetFrom + length - written - bufSize, SeekOrigin.Begin);
                    s.Read(data, 0, bufSize);
                    s.Seek(offsetTo + length - written -bufSize, SeekOrigin.Begin);
                } else
                {
                    s.Seek(offsetFrom + written, SeekOrigin.Begin);
                    s.Read(data, 0, bufSize);
                    s.Seek(offsetTo + written, SeekOrigin.Begin);
                }
                s.Write(data, 0, bufSize);
                written += bufSize;
            }
        }

        /// <summary>
        /// Remove a portion of bytes within the given stream
        /// </summary>
        /// <param name="s">Stream to process; must be accessible for reading and writing</param>
        /// <param name="endOffset">End offset of the portion of bytes to remove</param>
        /// <param name="delta">Number of bytes to remove</param>
        public static void ShortenStream(Stream s, Int64 endOffset, UInt32 delta) 
        {
            Int32 bufSize;
            var data = new Byte[BUFFERSIZE];
            var newIndex = endOffset - delta;
            var initialLength = s.Length;
            Int64 i = 0;

            while (i < initialLength - endOffset) // Forward loop
            {
                bufSize = (Int32)Math.Min(BUFFERSIZE, initialLength - endOffset - i);
                s.Seek(endOffset + i, SeekOrigin.Begin);
                s.Read(data, 0, bufSize);
                s.Seek(newIndex + i, SeekOrigin.Begin);
                s.Write(data, 0, bufSize);
                i += bufSize;
            }

            s.SetLength(initialLength - delta);
        }

        /// <summary>
        /// Add bytes within the given stream
        /// </summary>
        /// <param name="s">Stream to process; must be accessible for reading and writing</param>
        /// <param name="oldIndex">Offset where to add new bytes</param>
        /// <param name="delta">Number of bytes to add</param>
        /// <param name="fillZeroes">If true, new bytes will all be zeroes (optional; default = false)</param>
        public static void LengthenStream(Stream s, Int64 oldIndex, UInt32 delta, Boolean fillZeroes = false)
        {
            var data = new Byte[BUFFERSIZE];
            var newIndex = oldIndex + delta;
            var oldLength = s.Length;
            var newLength = s.Length + delta;
            s.SetLength(newLength);

            Int64 i = 0;
            Int32 bufSize;

            while (newLength - i > newIndex) // Backward loop
            {
                bufSize = (Int32)Math.Min(BUFFERSIZE, newLength - newIndex - i);
                s.Seek(-i - bufSize - delta, SeekOrigin.End); // Seeking is done from the "modified" end (new length) => substract delta
                s.Read(data, 0, bufSize);
                s.Seek(-i - bufSize, SeekOrigin.End);
                s.Write(data, 0, bufSize);
                i += bufSize;
            }

            if (fillZeroes)
            {
                // Fill the location of old copied data with zeroes
                s.Seek(oldIndex, SeekOrigin.Begin);
                for (i = oldIndex; i < newIndex; i++) s.WriteByte(0);
            }
        }

        /// <summary>
        /// Decodes an unsigned Big-Endian 16-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static UInt16 DecodeBEUInt16(Byte[] data)
        {
            if (data.Length < 2) throw new InvalidDataException("Data should be at least 2 bytes long; found " + data.Length + " bytes");
            return (UInt16)((data[0] << 8) | (data[1] << 0));
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian 16-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEUInt16(UInt16 value)
        {
            // Output has to be big-endian
            return new Byte[2] { (Byte)((value & 0xFF00) >> 8), (Byte)(value & 0x00FF) };
        }

        /// <summary>
        /// Decodes an unsigned Little-Endian 16-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static UInt16 DecodeUInt16(Byte[] data)
        {
            if (data.Length < 2) throw new InvalidDataException("Data should be at least 2 bytes long; found " + data.Length + " bytes");
            return (UInt16)((data[0]) | (data[1] << 8));
        }

        /// <summary>
        /// Decodes an signed Little-Endian 16-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int16 DecodeInt16(Byte[] data)
        {
            if (data.Length < 2) throw new InvalidDataException("Data should be at least 2 bytes long; found " + data.Length + " bytes");
            return (Int16)((data[0]) | (data[1] << 8));
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian 16-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEInt16(Int16 value)
        {
            // Output has to be big-endian
            return new Byte[2] { (Byte)((value & 0xFF00) >> 8), (Byte)(value & 0x00FF) };
        }

        /// <summary>
        /// Decodes a signed Big-Endian 16-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int16 DecodeBEInt16(Byte[] data)
        {
            if (data.Length < 2) throw new InvalidDataException("Data should be at least 2 bytes long; found " + data.Length + " bytes");
            return (Int16)((data[0] << 8) | (data[1] << 0));
        }

        /// <summary>
        /// Decodes a signed Big-Endian 24-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int32 DecodeBEInt24(Byte[] data)
        {
            if (data.Length < 3) throw new InvalidDataException("Data should be at least 3 bytes long; found " + data.Length + " bytes");
            return (data[0] << 16) | (data[1] << 8) | (data[2] << 0);
        }

        /// <summary>
        /// Decodes an unsigned Big-Endian 24-bit integer from the given array of bytes, starting from the given offset
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <param name="offset">Offset to read value from (default : 0)</param>
        /// <returns>Decoded value</returns>
        public static UInt32 DecodeBEUInt24(Byte[] value, Int32 offset = 0)
        {
            if (value.Length - offset < 3) throw new InvalidDataException("Value should at least contain 3 bytes after offset; actual size=" + (value.Length - offset) + " bytes");
            return (UInt32)(value[offset] << 16 | value[offset + 1] << 8 | value[offset + 2]);
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian 24-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEUInt24(UInt32 value)
        {
            if (value > 0x00FFFFFF) throw new InvalidDataException("Value should not be higher than " + 0x00FFFFFF + "; actual value=" + value);

            // Output has to be big-endian
            return new Byte[3] { (Byte)((value & 0x00FF0000) >> 16), (Byte)((value & 0x0000FF00) >> 8), (Byte)(value & 0x000000FF) };
        }

        /// <summary>
        /// Decodes an unsigned Big-Endian 32-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static UInt32 DecodeBEUInt32(Byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("Data should be at least 4 bytes long; found " + data.Length + " bytes");
            return (UInt32)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | (data[3] << 0));
        }

        /// <summary>
        /// Decodes an unsigned Little-Endian 32-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static UInt32 DecodeUInt32(Byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("Data should be at least 4 bytes long; found " + data.Length + " bytes");
            return (UInt32)((data[0]) | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian unsigned 32-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEUInt32(UInt32 value)
        {
            // Output has to be big-endian
            return new Byte[4] { (Byte)((value & 0xFF000000) >> 24), (Byte)((value & 0x00FF0000) >> 16), (Byte)((value & 0x0000FF00) >> 8), (Byte)(value & 0x000000FF) };
        }

        /// <summary>
        /// Decodes a signed Big-Endian 32-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int32 DecodeBEInt32(Byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("Data should be at least 4 bytes long; found " + data.Length + " bytes");
            return (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | (data[3] << 0);
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian 32-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEInt32(Int32 value)
        {
            // Output has to be big-endian
            return new Byte[4] { (Byte)((value & 0xFF000000) >> 24), (Byte)((value & 0x00FF0000) >> 16), (Byte)((value & 0x0000FF00) >> 8), (Byte)(value & 0x000000FF) };
        }

        /// <summary>
        /// Decodes a signed Little-Endian 32-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int32 DecodeInt32(Byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("data should be at least 4 bytes long; found " + data.Length + " bytes");
            return (Int32)((data[0]) | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        }

        /// <summary>
        /// Decodes an unsigned Little-Endian 64-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static UInt64 DecodeUInt64(Byte[] data)
        {
            if (data.Length < 8) throw new InvalidDataException("Data should be at least 8 bytes long; found " + data.Length + " bytes");
            return (UInt64)((data[0]) | (data[1] << 8) | (data[2] << 16) | (data[3] << 24) | (data[4] << 32) | (data[5] << 40) | (data[6] << 48) | (data[7] << 56));
        }

        /// <summary>
        /// Decodes a signed Big-Endian 64-bit integer from the given array of bytes
        /// </summary>
        /// <param name="value">Array of bytes to read value from</param>
        /// <returns>Decoded value</returns>
        public static Int64 DecodeBEInt64(Byte[] data)
        {
            if (data.Length < 8) throw new InvalidDataException("Data should be at least 8 bytes long; found " + data.Length + " bytes");
            return (Int64)((data[0] << 56) | (data[1] << 48) | (data[2] << 40) | (data[3] << 32) | (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | (data[7] << 0));
        }

        /// <summary>
        /// Encodes the given value into an array of bytes as a Big-Endian unsigned 64-bits integer
        /// </summary>
        /// <param name="value">Value to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeBEUInt64(UInt64 value)
        {
            // Output has to be big-endian
            return new Byte[8] { (Byte)((value & 0xFF00000000000000) >> 56), (Byte)((value & 0x00FF000000000000) >> 48), (Byte)((value & 0x0000FF0000000000) >> 40), (Byte)((value & 0x000000FF00000000) >> 32), (Byte)((value & 0x00000000FF000000) >> 24), (Byte)((value & 0x0000000000FF0000) >> 16), (Byte)((value & 0x000000000000FF00) >> 8), (Byte)(value & 0x00000000000000FF) };
        }

        /// <summary>
        /// Switches the format of an unsigned Int32 between big endian and little endian
        /// </summary>
        /// <param name="n">value to convert</param>
        /// <returns>converted value</returns>
        public static UInt32 ReverseUInt32(UInt32 n)
        {
            Byte b0;
            Byte b1;
            Byte b2;
            Byte b3;

            b0 = (Byte)((n & 0x000000FF) >> 0);
            b1 = (Byte)((n & 0x0000FF00) >> 8);
            b2 = (Byte)((n & 0x00FF0000) >> 16);
            b3 = (Byte)((n & 0xFF000000) >> 24);

            return (UInt32)((b0 << 24) | (b1 << 16) | (b2 << 8) | (b3 << 0));
        }

        /// <summary>
        /// Switches the format of a signed Int32 between big endian and little endian
        /// </summary>
        /// <param name="n">value to convert</param>
        /// <returns>converted value</returns>
        public static Int32 ReverseInt32(Int32 n)
		{
			Byte b0;
			Byte b1;
			Byte b2;
			Byte b3;

			b0 = (Byte)((n & 0x000000FF) >> 0); 
			b1 = (Byte)((n & 0x0000FF00) >> 8); 
			b2 = (Byte)((n & 0x00FF0000) >> 16); 
			b3 = (Byte)((n & 0xFF000000) >> 24); 
			
			return (b0 << 24) | (b1 << 16) | (b2 << 8) | (b3 << 0);
        }

        /// <summary>
        /// Switches the format of an unsigned Int16 between big endian and little endian
        /// </summary>
        /// <param name="n">value to convert</param>
        /// <returns>converted value</returns>
        public static UInt16 ReverseUInt16(UInt16 n)
        {
            Byte b0;
            Byte b1;

            b0 = (Byte)((n & 0x00FF) >> 0);
            b1 = (Byte)((n & 0xFF00) >> 8);

            return (UInt16)((b0 << 8) | (b1 << 0));
        }

        /// <summary>
        /// Guesses the encoding from the file Byte Order Mark (BOM)
        /// http://en.wikipedia.org/wiki/Byte_order_mark 
        /// NB : This obviously only works for files that actually start with a BOM
        /// </summary>
        /// <param name="file">FileStream to read from</param>
        /// <returns>Detected encoding; system Default if detection failed</returns>
        public static Encoding GetEncodingFromFileBOM(FileStream file)
        {
            Encoding result;
            var bom = new Byte[4]; // Get the byte-order mark, if there is one
            file.Read(bom, 0, 4);
            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) // utf-8
            {
                result = Encoding.UTF8;
            }
            else if (bom[0] == 0xfe && bom[1] == 0xff) // utf-16 and ucs-2
            {
                result = Encoding.BigEndianUnicode;
            }
            else if (bom[0] == 0xff && bom[1] == 0xfe) // ucs-2le, ucs-4le, and ucs-16le
            {
                result = Encoding.Unicode;
            }
            else if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) // ucs-4 / UTF-32
            {
                result = Encoding.UTF32;
            }
            else
            {
                // There might be some cases where the Default encoding reads illegal characters
                // e.g. "ß" encoded in Windows-1250 gives an illegal character when read with Chinese-simplified (gb2312)
                result = Encoding.Default;
            }

            // Now reposition the file cursor back to the start of the file
            file.Seek(0, SeekOrigin.Begin);
            return result;
        }

        /// <summary>
        /// Reads a null-terminated String from the given BinaryReader, according to the given Encoding
        /// Returns with the BinaryReader positioned after the last null-character(s)
        /// </summary>
        /// <param name="r">BinaryReader positioned at the beginning of the String to be read</param>
        /// <param name="encoding">Encoding to use for reading the stream</param>
        /// <returns>Read value</returns>
        public static String ReadNullTerminatedString(BinaryReader r, Encoding encoding)
        {
            return readNullTerminatedString(r.BaseStream, encoding, 0, false);
        }
        public static String ReadNullTerminatedString(Stream s, Encoding encoding)
        {
            return readNullTerminatedString(s, encoding, 0, false);
        }

        /// <summary>
        /// Reads a null-terminated String from the given BinaryReader, according to the given Encoding, within a given limit of bytes
        /// Returns with the BinaryReader positioned at (start+limit)
        /// </summary>
        /// <param name="r">BinaryReader positioned at the beginning of the String to be read</param>
        /// <param name="encoding">Encoding to use for reading the stream</param>
        /// <param name="limit">Maximum number of bytes to read</param>
        /// <returns>Read value</returns>
        public static String ReadNullTerminatedStringFixed(BinaryReader r, Encoding encoding, Int32 limit)
        {
            return readNullTerminatedString(r.BaseStream, encoding, limit, true);
        }
        public static String ReadNullTerminatedStringFixed(BufferedBinaryReader r, Encoding encoding, Int32 limit)
        {
            return readNullTerminatedString(r, encoding, limit, true);
        }

        /// <summary>
        /// Reads a null-terminated string using the giver BinaryReader
        /// </summary>
        /// <param name="r">Stream reader to read the string from</param>
        /// <param name="encoding">Encoding to use to parse the read bytes into the resulting String</param>
        /// <param name="limit">Limit (in bytes) of read data (0=unlimited)</param>
        /// <param name="moveStreamToLimit">Indicates if the stream has to advance to the limit before returning</param>
        /// <returns>The string read, without the zeroes at its end</returns>
        private static String readNullTerminatedString(Stream r, Encoding encoding, Int32 limit, Boolean moveStreamToLimit)
        {
            var nbChars = (encoding.Equals(Encoding.BigEndianUnicode) || encoding.Equals(Encoding.Unicode)) ? 2 : 1;
            var readBytes = new Byte[limit > 0 ? limit : 100];
            var buffer = new Byte[2];
            var nbRead = 0;
            var streamLength = r.Length;
            var initialPos = r.Position;
            var streamPos = initialPos;

            while (streamPos < streamLength && ( (0 == limit) || (nbRead < limit) ) )
            {
                // Read the size of a character
                r.Read(buffer, 0, nbChars);

                if ( (1 == nbChars) && (0 == buffer[0]) ) // Null character read for single-char encodings
                {
                    break;
                }
                else if ( (2 == nbChars) && (0 == buffer[0]) && (0 == buffer[1]) ) // Null character read for two-char encodings
                {
                    break;
                }
                else // All clear; store the read char in the byte array
                {
                    if (readBytes.Length < nbRead + nbChars) Array.Resize<Byte>(ref readBytes, readBytes.Length + 100);

                    readBytes[nbRead] = buffer[0];
                    if (2 == nbChars) readBytes[nbRead+1] = buffer[1];
                    nbRead += nbChars;
                    streamPos += nbChars;
                }
            }

            if (moveStreamToLimit) r.Seek(initialPos + limit, SeekOrigin.Begin);

            return encoding.GetString(readBytes,0,nbRead);
        }

        /// <summary>
        /// Extracts a signed 32-bit integer from a byte array using the "synch-safe" convention
        /// as to ID3v2 definition (§6.2)
        /// </summary>
        /// <param name="bytes">Byte array containing data
        /// NB : Array size can vary from 1 to 5 bytes, as only 7 bits of each is actually used
        /// </param>
        /// <returns>Decoded Int32</returns>
        public static Int32 DecodeSynchSafeInt(Byte[] bytes)
        {
            if (bytes.Length > 5) throw new Exception("Array too long : has to be 1 to 5 bytes; found : " + bytes.Length + " bytes");
            var result = 0;

            for (var i = 0; i < bytes.Length; i++)
            {
                result += bytes[i] * (Int32)Math.Floor(Math.Pow(2, (7 * (bytes.Length - 1 - i))));
            }
            return result;
        }

        /// <summary>
        /// Decodes a signed 32-bit integer from a 4-byte array using the "synch-safe" convention
        /// as to ID3v2 definition (§6.2)
        /// NB : The actual capacity of the integer thus reaches 28 bits
        /// </summary>
        /// <param name="bytes">4-byte array containing to convert</param>
        /// <returns>Decoded Int32</returns>
        public static Int32 DecodeSynchSafeInt32(Byte[] bytes)
        {
            if (bytes.Length != 4) throw new Exception("Array length has to be 4 bytes; found : "+bytes.Length+" bytes");

            return                 
                bytes[0] * 0x200000 +   //2^21
                bytes[1] * 0x4000 +     //2^14
                bytes[2] * 0x80 +       //2^7
                bytes[3];
        }

        /// <summary>
        /// Encodes the given values as a (nbBytes*8)-bit integer to a (nbBytes)-byte array using the "synch-safe" convention
        /// as to ID3v2 definition (§6.2)
        /// </summary>
        /// <param name="value">Value to encode</param>
        /// <param name="nbBytes">Number of bytes to encode to (can be 1 to 5)</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeSynchSafeInt(Int32 value, Int32 nbBytes)
        {
            if ((nbBytes < 1) || (nbBytes > 5)) throw new Exception("nbBytes has to be 1 to 5; found : " + nbBytes);
            var result = new Byte[nbBytes];
            Int32 range;

            for (var i = 0; i < nbBytes; i++)
            {
                range = (7 * (nbBytes - 1 - i));
                result[i] = (Byte)( (value & (0x7F << range)) >> range);
            }

            return result;
        }

        /// <summary>
        /// Encodes the given value as a 32-bit integer to a 4-byte array using the "synch-safe" convention
        /// as to ID3v2 definition (§6.2)
        /// </summary>
        /// <param name="value">Integer to be encoded</param>
        /// <returns>Encoded array of bytes</returns>
        public static Byte[] EncodeSynchSafeInt32(Int32 value)
        {
            var result = new Byte[4];
            result[0] = (Byte)((value & 0xFE00000) >> 21);
            result[1] = (Byte)((value & 0x01FC000) >> 14);
            result[2] = (Byte)((value & 0x0003F80) >> 7);
            result[3] = (Byte)((value & 0x000007F));

            return result;
        }


        /// <summary>
        /// Finds a byte sequence within a stream
        /// </summary>
        /// <param name="stream">Stream to search into</param>
        /// <param name="sequence">Sequence to find</param>
        /// <param name="limit">Maximum distance (in bytes) of the sequence to find.
        /// Put 0 for an infinite distance</param>
        /// <returns>
        ///     true if the sequence has been found; the stream will be positioned on the 1st byte following the sequence
        ///     false if the sequence has not been found; the stream will keep its initial position
        /// </returns>
        public static Boolean FindSequence(Stream stream, Byte[] sequence, Int64 limit = 0)
        {
            var BUFFER_SIZE = 512;
            var readBuffer = new Byte[BUFFER_SIZE];

            Int32 remainingBytes, bytesToRead;
            var iSequence = 0;
            var readBytes = 0;
            var initialPos = stream.Position;

            remainingBytes = (Int32)((limit > 0) ? Math.Min(stream.Length - stream.Position, limit) : stream.Length - stream.Position);

            while (remainingBytes > 0)
            {
                bytesToRead = Math.Min(remainingBytes, BUFFER_SIZE);

                stream.Read(readBuffer, 0, bytesToRead);

                for (var i = 0; i < bytesToRead; i++)
                {
                    if (sequence[iSequence] == readBuffer[i]) iSequence++;
                    else if (iSequence > 0) iSequence = 0;

                    if (sequence.Length == iSequence)
                    {
                        stream.Position = initialPos + readBytes + i + 1;
                        return true;
                    }
                }

                remainingBytes -= bytesToRead;
                readBytes += bytesToRead;
            }

            // If we're here, the sequence hasn't been found
            stream.Position = initialPos;
            return false;
        }

        /// <summary>
        /// Reads the given number of bits from the given position and converts it to an unsigned int32
        /// according to big-endian convention
        /// 
        /// NB : reader position _always_ progresses by 4, no matter how many bits are needed
        /// </summary>
        /// <param name="source">BinaryReader to read the data from</param>
        /// <param name="bitPosition">Position of the first _bit_ to read (scale is x8 compared to classic byte positioning) </param>
        /// <param name="bitCount">Number of bits to read</param>
        /// <returns>Unsigned int32 formed from read bits, according to big-endian convention</returns>
        public static UInt32 ReadBits(BinaryReader source, Int32 bitPosition, Int32 bitCount)
        {
            if (bitCount < 1 || bitCount > 32) throw new NotSupportedException("Bit count must be between 1 and 32");
            var buffer = new Byte[4];

            // Read a number of bits from file at the given position
            source.BaseStream.Seek(bitPosition / 8, SeekOrigin.Begin); // integer division =^ div
            buffer = source.ReadBytes(4);
            var result = (UInt32)((buffer[0] << 24) + (buffer[1] << 16) + (buffer[2] << 8) + buffer[3]);
            result = (result << (bitPosition % 8)) >> (32 - bitCount);

            return result;
        }

        /// <summary>Converts the given extended-format byte array (which
        /// is assumed to be in little-endian form) to a .NET Double,
        /// as closely as possible. Values which are too small to be 
        /// represented are returned as an appropriately signed 0. Values 
        /// which are too large
        /// to be represented (but not infinite) are returned as   
        /// Double.NaN,
        /// as are unsupported values and actual NaN values.</summary>
        /// 
        /// Credits : Jon Skeet (http://groups.google.com/groups?selm=MPG.19a6985d4683f5d398a313%40news.microsoft.com)
        public static Double ExtendedToDouble(Byte[] extended)
        {
            // Read information from the extended form - variable names as 
            // used in http://cch.loria.fr/documentation/
            // IEEE754/numerical_comp_guide/ncg_math.doc.html
            var s = extended[9] >> 7;

            var e = (((extended[9] & 0x7f) << 8) | (extended[8]));

            var j = extended[7] >> 7;
            Int64 f = extended[7] & 0x7f;
            for (var i = 6; i >= 0; i--)
            {
                f = (f << 8) | extended[i];
            }

            // Now go through each possibility
            if (j == 1)
            {
                if (e == 0)
                {
                    // Value = (-1)^s * 2^16382*1.f 
                    // (Pseudo-denormal numbers)
                    // Anything pseudo-denormal in extended form is 
                    // definitely 0 in double form.
                    return FromComponents(s, 0, 0);
                }
                else if (e != 32767)
                {
                    // Value = (-1)^s * 2^(e-16383)*1.f  (Normal numbers)

                    // Lose the last 11 bits of the fractional part
                    f = f >> 11;

                    // Convert exponent to the appropriate one
                    e += 1023 - 16383;

                    // Out of range - too large
                    if (e > 2047)
                        return Double.NaN;

                    // Out of range - too small
                    if (e < 0)
                    {
                        // See if we can get a subnormal version
                        if (e >= -51)
                        {
                            // Put a 1 at the front of f
                            f = f | (1 << 52);
                            // Now shift it appropriately
                            f = f >> (1 - e);
                            // Return a subnormal version
                            return FromComponents(s, 0, f);
                        }
                        else // Return an appropriate 0
                        {
                            return FromComponents(s, 0, 0);
                        }
                    }

                    return FromComponents(s, e, f);
                }
                else
                {
                    if (f == 0)
                    {
                        // Value = positive/negative infinity
                        return FromComponents(s, 2047, 0);
                    }
                    else
                    {
                        // Don't really understand the document about the 
                        // remaining two values, but they're both NaN...
                        return Double.NaN;
                    }
                }
            }
            else // Okay, j==0
            {
                if (e == 0)
                {
                    // Either 0 or a subnormal number, which will 
                    // still be 0 in double form
                    return FromComponents(s, 0, 0);
                }
                else
                {
                    // Unsupported
                    return Double.NaN;
                }
            }
        }

        /// <summary>Returns a double from the IEEE sign/exponent/fraction
        /// components.</summary>
        /// 
        /// Credits : Jon Skeet (http://groups.google.com/groups?selm=MPG.19a6985d4683f5d398a313%40news.microsoft.com)
        private static Double FromComponents(Int32 s, Int32 e, Int64 f)
        {
            var data = new Byte[8];

            // Put the data into appropriate slots based on endianness.
            if (BitConverter.IsLittleEndian)
            {
                data[7] = (Byte)((s << 7) | (e >> 4));
                data[6] = (Byte)(((e & 0xf) << 4) | (Int32)(f >> 48));
                data[5] = (Byte)((f & 0xff0000000000L) >> 40);
                data[4] = (Byte)((f & 0xff00000000L) >> 32);
                data[3] = (Byte)((f & 0xff000000L) >> 24);
                data[2] = (Byte)((f & 0xff0000L) >> 16);
                data[1] = (Byte)((f & 0xff00L) >> 8);
                data[0] = (Byte)(f & 0xff);
            }
            else
            {
                data[0] = (Byte)((s << 7) | (e >> 4));
                data[1] = (Byte)(((e & 0xf) << 4) | (Int32)(f >> 48));
                data[2] = (Byte)((f & 0xff0000000000L) >> 40);
                data[3] = (Byte)((f & 0xff00000000L) >> 32);
                data[4] = (Byte)((f & 0xff000000L) >> 24);
                data[5] = (Byte)((f & 0xff0000L) >> 16);
                data[6] = (Byte)((f & 0xff00L) >> 8);
                data[7] = (Byte)(f & 0xff);
            }

            return BitConverter.ToDouble(data, 0);
        }
        
        public static Stream ToStream(this String s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            
                writer.Write(s);
                writer.Flush();
                stream.Position = 0;
            
            return stream;
        }
    }
}
