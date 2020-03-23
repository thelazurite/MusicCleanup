using ATL.Logging;
using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Monkey's Audio file manipulation (extension : .APE)
    /// </summary>
	class APE : IAudioDataIO
	{
		// Compression level codes
		public const Int32 MONKEY_COMPRESSION_FAST       = 1000;  // Fast (poor)
		public const Int32 MONKEY_COMPRESSION_NORMAL     = 2000;  // Normal (good)
		public const Int32 MONKEY_COMPRESSION_HIGH       = 3000;  // High (very good)	
		public const Int32 MONKEY_COMPRESSION_EXTRA_HIGH = 4000;  // Extra high (best)
		public const Int32 MONKEY_COMPRESSION_INSANE     = 5000;  // Insane
		public const Int32 MONKEY_COMPRESSION_BRAINDEAD  = 6000;  // BrainDead
	
		// Compression level names
		public static readonly String[] MONKEY_COMPRESSION = new String[7] { "Unknown", "Fast", "Normal", "High", "Extra High", "Insane", "BrainDead" };

		// Format flags, only for Monkey's Audio <= 3.97
		public const Byte MONKEY_FLAG_8_BIT          = 1;  // Audio 8-bit
		public const Byte MONKEY_FLAG_CRC            = 2;  // New CRC32 error detection
		public const Byte MONKEY_FLAG_PEAK_LEVEL     = 4;  // Peak level stored
		public const Byte MONKEY_FLAG_24_BIT         = 8;  // Audio 24-bit
		public const Byte MONKEY_FLAG_SEEK_ELEMENTS  = 16; // Number of seek elements stored
		public const Byte MONKEY_FLAG_WAV_NOT_STORED = 32; // WAV header not stored

		// Channel mode names
		public static readonly String[] MONKEY_MODE = new String[3]	{ "Unknown", "Mono", "Stereo" };
	

        ApeHeader header = new ApeHeader();				// common header

    	// Stuff loaded from the header:
		private Int32 version;
		private String versionStr;
		private Int32	channels;
		private Int32	sampleRate;
		private Int32	bits;
		private UInt32 peakLevel;
		private Double peakLevelRatio;
		private Int64 totalSamples;
		private Int32	compressionMode;
		private String compressionModeStr;
      
		// FormatFlags, only used with Monkey's <= 3.97
		private Int32 formatFlags;
		private Boolean hasPeakLevel;
		private Boolean hasSeekElements;
		private Boolean wavNotStored;

        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private String filePath;



        // Real structure of Monkey's Audio header
        // common header for all versions
        private class ApeHeader
        {
            public Char[] cID = new Char[4]; // should equal 'MAC '
            public UInt16 nVersion;          // version number * 1000 (3.81 = 3810)
        }

        // old header for <= 3.97
        private struct ApeHeaderOld
        {
            public UInt16 nCompressionLevel; // the compression level
            public UInt16 nFormatFlags;      // any format flags (for future use)
            public UInt16 nChannels;         // the number of channels (1 or 2)
            public UInt32 nSampleRate;         // the sample rate (typically 44100)
            public UInt32 nHeaderBytes;        // the bytes after the MAC header that compose the WAV header
            public UInt32 nTerminatingBytes;   // the bytes after that raw data (for extended info)
            public UInt32 nTotalFrames;        // the number of frames in the file
            public UInt32 nFinalFrameBlocks;   // the number of samples in the final frame
            public Int32 nInt;
        }
        // new header for >= 3.98
        private struct ApeHeaderNew
        {
            public UInt16 nCompressionLevel;  // the compression level (see defines I.E. COMPRESSION_LEVEL_FAST)
            public UInt16 nFormatFlags;     // any format flags (for future use) Note: NOT the same flags as the old header!
            public UInt32 nBlocksPerFrame;        // the number of audio blocks in one frame
            public UInt32 nFinalFrameBlocks;  // the number of audio blocks in the final frame
            public UInt32 nTotalFrames;           // the total number of frames
            public UInt16 nBitsPerSample;       // the bits per sample (typically 16)
            public UInt16 nChannels;            // the number of channels (1 or 2)
            public UInt32 nSampleRate;            // the sample rate (typically 44100)
        }
        // data descriptor for >= 3.98
        private class ApeDescriptor
        {
            public UInt16 padded;                   // padding/reserved (always empty)
            public UInt32 nDescriptorBytes;           // the number of descriptor bytes (allows later expansion of this header)
            public UInt32 nHeaderBytes;               // the number of header APE_HEADER bytes
            public UInt32 nSeekTableBytes;            // the number of bytes of the seek table
            public UInt32 nHeaderDataBytes;           // the number of header data bytes (from original file)
            public UInt32 nAPEFrameDataBytes;         // the number of bytes of APE frame data
            public UInt32 nAPEFrameDataBytesHigh;     // the high order number of APE frame data bytes
            public UInt32 nTerminatingDataBytes;      // the terminating data of the file (not including tag data)
            public Byte[] cFileMD5 = new Byte[16];  // the MD5 hash of the file (see notes for usage... it's a littly tricky)
        }


        public Int32 Version => version;

        public String VersionStr => versionStr;

        public Int32 Channels => channels;

        public Int32 Bits => bits;

        public UInt32	PeakLevel => peakLevel;

        public Double PeakLevelRatio => peakLevelRatio;

        public Int64	TotalSamples => totalSamples;

        public Int32 CompressionMode => compressionMode;

        public String CompressionModeStr => compressionModeStr;

        // FormatFlags, only used with Monkey's <= 3.97
		public Int32 FormatFlags => formatFlags;

		public Boolean	HasPeakLevel => hasPeakLevel;

		public Boolean	HasSeekElements => hasSeekElements;

		public Boolean	WavNotStored => wavNotStored;

		// ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES
        public Int32 SampleRate => sampleRate;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossless;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_APE) || (metaDataType == MetaDataIOFactory.TAG_ID3V1) || (metaDataType == MetaDataIOFactory.TAG_ID3V2);
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
		{
			// Reset data
			isValid				= false;
			version            = 0;
			versionStr         = "";
			channels  		    = 0;
			sampleRate		    = 0;
			bits      		    = 0;
			peakLevel          = 0;
			peakLevelRatio     = 0.0;
			totalSamples       = 0;
			compressionMode    = 0;
			compressionModeStr = "";
			formatFlags        = 0;
			hasPeakLevel       = false;
			hasSeekElements    = false;
			wavNotStored       = false;
            bitrate = 0;
            duration = 0;
		}

		public APE(String filePath)
		{
            this.filePath = filePath;
			resetData();
		}

        
        // ---------- SUPPORT METHODS

        private void readCommonHeader(BinaryReader source)
        {
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

			header.cID = source.ReadChars(4);
			header.nVersion = source.ReadUInt16();
        }

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            var APE_OLD = new ApeHeaderOld();	// old header   <= 3.97
			var APE_NEW = new ApeHeaderNew();	// new header   >= 3.98
			var APE_DESC = new ApeDescriptor(); // extra header >= 3.98

			Int32 BlocksPerFrame;
			Boolean LoadSuccess;
			var result = false;

            this.sizeInfo = sizeInfo;
            resetData();

            // reading data from file
            LoadSuccess = false;

            readCommonHeader(source);

			if ( StreamUtils.StringEqualsArr("MAC ",header.cID) )
			{
                isValid = true;
				version = header.nVersion;

				versionStr = ((Double)version / 1000).ToString().Substring(0,4); //Str(FVersion / 1000 : 4 : 2, FVersionStr);
            
				// Load New Monkey's Audio Header for version >= 3.98
				if (header.nVersion >= 3980) 
				{
					APE_DESC.padded = 0;
					APE_DESC.nDescriptorBytes = 0;
					APE_DESC.nHeaderBytes = 0;
					APE_DESC.nSeekTableBytes = 0;
					APE_DESC.nHeaderDataBytes = 0;
					APE_DESC.nAPEFrameDataBytes = 0;
					APE_DESC.nAPEFrameDataBytesHigh = 0;
					APE_DESC.nTerminatingDataBytes = 0;
					Array.Clear(APE_DESC.cFileMD5,0,APE_DESC.cFileMD5.Length);

					APE_DESC.padded = source.ReadUInt16();
					APE_DESC.nDescriptorBytes = source.ReadUInt32();
					APE_DESC.nHeaderBytes = source.ReadUInt32();
					APE_DESC.nSeekTableBytes = source.ReadUInt32();
					APE_DESC.nHeaderDataBytes = source.ReadUInt32();
					APE_DESC.nAPEFrameDataBytes = source.ReadUInt32();
					APE_DESC.nAPEFrameDataBytesHigh = source.ReadUInt32();
					APE_DESC.nTerminatingDataBytes = source.ReadUInt32();
					APE_DESC.cFileMD5 = source.ReadBytes(16);

					// seek past description header
					if (APE_DESC.nDescriptorBytes != 52) source.BaseStream.Seek(APE_DESC.nDescriptorBytes - 52, SeekOrigin.Current);
					// load new ape_header
					if (APE_DESC.nHeaderBytes > 24/*sizeof(APE_NEW)*/) APE_DESC.nHeaderBytes = 24/*sizeof(APE_NEW)*/;
                  				
					APE_NEW.nCompressionLevel = 0;
					APE_NEW.nFormatFlags = 0;
					APE_NEW.nBlocksPerFrame = 0;
					APE_NEW.nFinalFrameBlocks = 0;
					APE_NEW.nTotalFrames = 0;
					APE_NEW.nBitsPerSample = 0;
					APE_NEW.nChannels = 0;
					APE_NEW.nSampleRate = 0;

					APE_NEW.nCompressionLevel = source.ReadUInt16();
					APE_NEW.nFormatFlags = source.ReadUInt16();
					APE_NEW.nBlocksPerFrame = source.ReadUInt32();
					APE_NEW.nFinalFrameBlocks = source.ReadUInt32();
					APE_NEW.nTotalFrames = source.ReadUInt32();
					APE_NEW.nBitsPerSample = source.ReadUInt16();
					APE_NEW.nChannels = source.ReadUInt16();
					APE_NEW.nSampleRate = source.ReadUInt32();
				
					// based on MAC SDK 3.98a1 (APEinfo.h)
					sampleRate       = (Int32)APE_NEW.nSampleRate;
					channels         = APE_NEW.nChannels;
					formatFlags      = APE_NEW.nFormatFlags;
					bits             = APE_NEW.nBitsPerSample;
					compressionMode  = APE_NEW.nCompressionLevel;
					// calculate total uncompressed samples
					if (APE_NEW.nTotalFrames > 0)
					{
						totalSamples     = (Int64)(APE_NEW.nBlocksPerFrame) *
							(Int64)(APE_NEW.nTotalFrames-1) +
							(Int64)(APE_NEW.nFinalFrameBlocks);
					}
					LoadSuccess = true;
				}
				else 
				{
					// Old Monkey <= 3.97               

					APE_OLD.nCompressionLevel = 0;
					APE_OLD.nFormatFlags = 0;
					APE_OLD.nChannels = 0;
					APE_OLD.nSampleRate = 0;
					APE_OLD.nHeaderBytes = 0;
					APE_OLD.nTerminatingBytes = 0;
					APE_OLD.nTotalFrames = 0;
					APE_OLD.nFinalFrameBlocks = 0;
					APE_OLD.nInt = 0;

					APE_OLD.nCompressionLevel = source.ReadUInt16();
					APE_OLD.nFormatFlags = source.ReadUInt16();
					APE_OLD.nChannels = source.ReadUInt16();
					APE_OLD.nSampleRate = source.ReadUInt32();
					APE_OLD.nHeaderBytes = source.ReadUInt32();
					APE_OLD.nTerminatingBytes = source.ReadUInt32();
					APE_OLD.nTotalFrames = source.ReadUInt32();
					APE_OLD.nFinalFrameBlocks = source.ReadUInt32();
					APE_OLD.nInt = source.ReadInt32();				

					compressionMode  = APE_OLD.nCompressionLevel;
					sampleRate       = (Int32)APE_OLD.nSampleRate;
					channels         = APE_OLD.nChannels;
					formatFlags      = APE_OLD.nFormatFlags;
					bits = 16;
					if ( (APE_OLD.nFormatFlags & MONKEY_FLAG_8_BIT ) != 0) bits =  8;
					if ( (APE_OLD.nFormatFlags & MONKEY_FLAG_24_BIT) != 0) bits = 24;

					hasSeekElements  = ( (APE_OLD.nFormatFlags & MONKEY_FLAG_PEAK_LEVEL   )  != 0);
					wavNotStored     = ( (APE_OLD.nFormatFlags & MONKEY_FLAG_SEEK_ELEMENTS) != 0);
					hasPeakLevel     = ( (APE_OLD.nFormatFlags & MONKEY_FLAG_WAV_NOT_STORED) != 0);
                  
					if (hasPeakLevel)
					{
						peakLevel        = (UInt32)APE_OLD.nInt;
						peakLevelRatio   = (peakLevel / (1 << bits) / 2.0) * 100.0;
					}

					// based on MAC_SDK_397 (APEinfo.cpp)
					if (version >= 3950) 
						BlocksPerFrame = 73728 * 4;
					else if ( (version >= 3900) || ((version >= 3800) && (MONKEY_COMPRESSION_EXTRA_HIGH == APE_OLD.nCompressionLevel)) )
						BlocksPerFrame = 73728;
					else
						BlocksPerFrame = 9216;

					// calculate total uncompressed samples
					if (APE_OLD.nTotalFrames>0)
					{
						totalSamples =  (Int64)(APE_OLD.nTotalFrames-1) *
							(Int64)(BlocksPerFrame) +
							(Int64)(APE_OLD.nFinalFrameBlocks);
					}
					LoadSuccess = true;
               
				}
				if (LoadSuccess) 
				{
					// compression profile name
					if ( (0 == (compressionMode % 1000)) && (compressionMode<=6000) )
					{
						compressionModeStr = MONKEY_COMPRESSION[compressionMode / 1000]; // int division
					}
					else 
					{
						compressionModeStr = compressionMode.ToString();
					}
					// length
					if (sampleRate > 0) duration = ((Double)totalSamples * 1000.0 / sampleRate);
					// average bitrate
					if (duration > 0) bitrate = 8 * (sizeInfo.FileSize - sizeInfo.TotalTagSize) / (duration);
					// some extra sanity checks
					isValid   = ((bits>0) && (sampleRate>0) && (totalSamples>0) && (channels>0));
					result   = isValid;
				}
			}

			return result;
		}

	}
}