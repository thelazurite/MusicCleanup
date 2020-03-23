using ATL.Logging;
using Commons;
using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for MPEG Audio Layer files manipulation (extensions : .MP1, .MP2, .MP3)
    /// </summary>
	class MPEGaudio : IAudioDataIO
	{
        // Limitation constants
        public const Int32 MAX_MPEG_FRAME_LENGTH = 8068;          // Max. MPEG frame length according to all extreme values
        public const Int32 MIN_MPEG_BIT_RATE = 8;                 // Min. bit rate value (KBit/s)
        public const Int32 MAX_MPEG_BIT_RATE = 448;               // Max. bit rate value (KBit/s)
        public const Double MIN_ALLOWED_DURATION = 0.1;         // Min. song duration value


        // VBR Vendor ID strings
        public const String VENDOR_ID_LAME = "LAME";                      // For LAME
        public const String VENDOR_ID_GOGO_NEW = "GOGO";            // For GoGo (New)
        public const String VENDOR_ID_GOGO_OLD = "MPGE";            // For GoGo (Old)

        /*
        public static readonly byte[] RIFF_HEADER = new byte[4] { 0x52, 0x49, 0x46, 0x46 }; // 'RIFF'
        public static readonly byte[] RIFF_MP3_ID = new byte[4] { 0x52, 0x4D, 0x50, 0x33 }; // 'RMP3'
        public static readonly byte[] RIFF_WAV_ID = new byte[4] { 0x57, 0x41, 0x56, 0x45 }; // 'WAVE'
        public static readonly byte[] RIFF_WAV_DATA_SUBCHUNK_ID = new byte[4] { 0x64, 0x61, 0x74, 0x61 }; // 'data'
        */

        // Table for bit rates (KBit/s)
        public static readonly UInt16[,,] MPEG_BIT_RATE = new UInt16[4,4,16]
        {
	       // For MPEG 2.5
		    {
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0},
			    {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0},
			    {0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0}
		    },
	       // Reserved
		    {
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
		    },
	       // For MPEG 2
		    {
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0},
			    {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0},
			    {0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0}
		    },
	       // For MPEG 1
		    {
			    {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
			    {0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0},
			    {0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0},
			    {0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0}
		    }
        };

		// Sample rate codes
		public const Byte MPEG_SAMPLE_RATE_LEVEL_3 = 0;                    // Level 3
		public const Byte MPEG_SAMPLE_RATE_LEVEL_2 = 1;                    // Level 2
		public const Byte MPEG_SAMPLE_RATE_LEVEL_1 = 2;                    // Level 1
		public const Byte MPEG_SAMPLE_RATE_UNKNOWN = 3;              // Unknown value

		// Table for sample rates
		public static readonly UInt16[,] MPEG_SAMPLE_RATE = new UInt16[4,4]
	    {
		    {11025, 12000, 8000, 0},                                   // For MPEG 2.5
		    {0, 0, 0, 0},                                                  // Reserved
		    {22050, 24000, 16000, 0},                                    // For MPEG 2
		    {44100, 48000, 32000, 0}                                     // For MPEG 1
    	};

		// VBR header ID for Xing/FhG
		public const String VBR_ID_XING = "Xing";                       // Xing VBR ID
		public const String VBR_ID_FHG = "VBRI";                         // FhG VBR ID

		// MPEG version codes
		public const Byte MPEG_VERSION_2_5 = 0;                            // MPEG 2.5
		public const Byte MPEG_VERSION_UNKNOWN = 1;                 // Unknown version
		public const Byte MPEG_VERSION_2 = 2;                                // MPEG 2
		public const Byte MPEG_VERSION_1 = 3;                                // MPEG 1

		// MPEG version names
		public static readonly String[] MPEG_VERSION = new String[4] {"MPEG 2.5", "MPEG ?", "MPEG 2", "MPEG 1"};

		// MPEG layer codes
		public const Byte MPEG_LAYER_UNKNOWN = 0;                     // Unknown layer
		public const Byte MPEG_LAYER_III = 1;                             // Layer III
		public const Byte MPEG_LAYER_II = 2;                               // Layer II
		public const Byte MPEG_LAYER_I = 3;                                 // Layer I

		// MPEG layer names
		public static readonly String[] MPEG_LAYER = new String[4]	{"Layer ?", "Layer III", "Layer II", "Layer I"};

		// Channel mode codes
		public const Byte MPEG_CM_STEREO = 0;                                // Stereo
		public const Byte MPEG_CM_JOINT_STEREO = 1;                    // Joint Stereo
		public const Byte MPEG_CM_DUAL_CHANNEL = 2;                    // Dual Channel
		public const Byte MPEG_CM_MONO = 3;                                    // Mono
		public const Byte MPEG_CM_UNKNOWN = 4;                         // Unknown mode

		// Channel mode names
		public static readonly String[] MPEG_CM_MODE = new String[5] {"Stereo", "Joint Stereo", "Dual Channel", "Mono", "Unknown"};

		// Extension mode codes (for Joint Stereo)
		public const Byte MPEG_CM_EXTENSION_OFF = 0;        // IS and MS modes set off
		public const Byte MPEG_CM_EXTENSION_IS = 1;             // Only IS mode set on
		public const Byte MPEG_CM_EXTENSION_MS = 2;             // Only MS mode set on
		public const Byte MPEG_CM_EXTENSION_ON = 3;          // IS and MS modes set on
		public const Byte MPEG_CM_EXTENSION_UNKNOWN = 4;     // Unknown extension mode

		// Emphasis mode codes
		public const Byte MPEG_EMPHASIS_NONE = 0;                              // None
		public const Byte MPEG_EMPHASIS_5015 = 1;                          // 50/15 ms
		public const Byte MPEG_EMPHASIS_UNKNOWN = 2;               // Unknown emphasis
		public const Byte MPEG_EMPHASIS_CCIT = 3;                         // CCIT J.17

		// Emphasis names
		public static readonly String[] MPEG_EMPHASIS = new String[4] {"None", "50/15 ms", "Unknown", "CCIT J.17"};

		// Encoder codes
		public const Byte MPEG_ENCODER_UNKNOWN = 0;                // Unknown encoder
		public const Byte MPEG_ENCODER_XING = 1;                              // Xing
		public const Byte MPEG_ENCODER_FHG = 2;                                // FhG
		public const Byte MPEG_ENCODER_LAME = 3;                              // LAME
		public const Byte MPEG_ENCODER_BLADE = 4;                            // Blade
		public const Byte MPEG_ENCODER_GOGO = 5;                              // GoGo
		public const Byte MPEG_ENCODER_SHINE = 6;                            // Shine
		public const Byte MPEG_ENCODER_QDESIGN = 7;                        // QDesign

		// Encoder names
		public static readonly String[] MPEG_ENCODER = new String[8] {"Unknown", "Xing", "FhG", "LAME", "Blade", "GoGo", "Shine", "QDesign"};

		// Xing/FhG VBR header data
		public class VBRData
		{
			public Boolean Found;                            // True if VBR header found
			public Char[] ID = new Char[4];            // Header ID: "Xing" or "VBRI"
			public Int32 Frames;                              // Total number of frames
			public Int32 Bytes;                                // Total number of bytes
			public Byte Scale;                                  // VBR scale (1..100)
			public String VendorID;                         // Vendor ID (if present)

            public void Reset()
            {
                Found = false;
                Array.Clear(ID, 0, ID.Length);
                Frames = 0;
                Bytes = 0;
                Scale = 0;
                VendorID = "";
            }
		}

		// MPEG frame header data
		public class FrameHeader
		{
			public Boolean Found;                                 // True if frame found
			public Int64 Position;                        // Frame position in the file
			public Int32 Size;                                 // Frame size (bytes)
			public Boolean Xing;                                 // True if Xing encoder
			public Byte VersionID;                                 // MPEG version ID
			public Byte LayerID;                                     // MPEG layer ID
			public Boolean ProtectionBit;                    // True if protected by CRC
			public UInt16 BitRateID;                                   // Bit rate ID
			public UInt16 SampleRateID;                             // Sample rate ID
			public Boolean PaddingBit;                           // True if frame padded
			public Boolean PrivateBit;                              // Extra information
			public Byte ModeID;                                    // Channel mode ID
			public Byte ModeExtensionID;      // Mode extension ID (for Joint Stereo)
			public Boolean CopyrightBit;                    // True if audio copyrighted
			public Boolean OriginalBit;                        // True if original media
			public Byte EmphasisID;                                    // Emphasis ID

            public void Reset()
            {
                Found = false;
                Position = 0;
                Size = 0;
                Xing = false;

                VersionID = MPEG_VERSION_UNKNOWN;
                LayerID = 0;
                ProtectionBit = false;
                BitRateID = 0;
                SampleRateID = MPEG_SAMPLE_RATE_UNKNOWN;
                PaddingBit = false;
                PrivateBit = false;
                ModeID = MPEG_CM_UNKNOWN;
                ModeExtensionID = MPEG_CM_EXTENSION_UNKNOWN;
                CopyrightBit = false;
                OriginalBit = false;
                EmphasisID = MPEG_EMPHASIS_UNKNOWN;
            }

            public void LoadFromByteArray(Byte[] data)
            {
                VersionID = (Byte)((data[1] >> 3) & 3);
                LayerID = (Byte)((data[1] >> 1) & 3);
                ProtectionBit = (data[1] & 1) != 1;
                BitRateID = (UInt16)(data[2] >> 4);
                SampleRateID = (UInt16)((data[2] >> 2) & 3);
                PaddingBit = (((data[2] >> 1) & 1) == 1);
                PrivateBit = ((data[2] & 1) == 1);
                ModeID = (Byte)((data[3] >> 6) & 3);
                ModeExtensionID = (Byte)((data[3] >> 4) & 3);
                CopyrightBit = (((data[3] >> 3) & 1) == 1);
                OriginalBit = (((data[3] >> 2) & 1) == 1);
                EmphasisID = (Byte)(data[3] & 3);
            }
        }  
      
//		private String vendorID;
		private VBRData vbrData = new VBRData();
		private FrameHeader HeaderFrame = new FrameHeader();
        private SizeInfo sizeInfo;
        private readonly String filePath;


        /* Unused for now

public FrameHeader Frame // Frame header data
{
    get { return this.HeaderFrame; }
}
public String Version // MPEG version name
{
    get { return this.getVersion(); }
}
public String Layer // MPEG layer name
{
    get { return this.getLayer(); }
}
public String ChannelMode // Channel mode name
{
    get { return this.getChannelMode(); }
}
public String Emphasis // Emphasis name
{
    get { return this.getEmphasis(); }
}
public long Frames // Total number of frames
{
    get { return this.getFrames(); }
}

public byte EncoderID // Guessed encoder ID
{
    get { return this.getEncoderID(); }
}
public String Encoder // Guessed encoder name
{
    get { return this.getEncoder(); }
}
*/


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public VBRData VBR // VBR header data
	        =>
		        vbrData;

        public Boolean IsVBR => vbrData.Found;

        public Double BitRate => getBitRate();

        public Double Duration => getDuration();

        public Int32 SampleRate => getSampleRate();

        public String FileName => filePath;


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            vbrData.Reset();
            HeaderFrame.Reset();
        }

        public MPEGaudio(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }

        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_ID3V1) || (metaDataType == MetaDataIOFactory.TAG_ID3V2) || (metaDataType == MetaDataIOFactory.TAG_APE);
        }

        // ********************* Auxiliary functions & voids ********************

        private static Boolean isValidFrameHeader(Byte[] HeaderData)
		{
            if (HeaderData.Length != 4) return false;

			// Check for valid frame header
            return !(
                ((HeaderData[0] & 0xFF) != 0xFF) ||                         // First 11 bits are set
                ((HeaderData[1] & 0xE0) != 0xE0) ||                         // First 11 bits are set
                (((HeaderData[1] >> 3) & 3) == MPEG_VERSION_UNKNOWN) ||     // MPEG version > 1
                (((HeaderData[1] >> 1) & 3) == MPEG_LAYER_UNKNOWN) ||       // Layer I, II or III
                ((HeaderData[2] & 0xF0) == 0xF0) ||                         // Bitrate index is not 'bad'
                ((HeaderData[2] & 0xF0) == 0) ||                            // Bitrate index is not 'free'
                (((HeaderData[2] >> 2) & 3) == MPEG_SAMPLE_RATE_UNKNOWN) || // Sampling rate is not 'reserved'
                ((HeaderData[3] & 3) == MPEG_EMPHASIS_UNKNOWN)              // Emphasis is not 'reserved'
                );
		}

		private static Byte getCoefficient(FrameHeader Frame)
		{
			// Get frame size coefficient
			if (MPEG_VERSION_1 == Frame.VersionID)
				if (MPEG_LAYER_I == Frame.LayerID) return 48;
				else return 144;
			else
				if (MPEG_LAYER_I == Frame.LayerID) return 24;
			else if (MPEG_LAYER_II == Frame.LayerID) return 144;
			else return 72;
		}

		private static UInt16 getBitRate(FrameHeader Frame)
		{
			// Get bit rate
			return MPEG_BIT_RATE[Frame.VersionID, Frame.LayerID, Frame.BitRateID];
		}

		private static UInt16 getSampleRate(FrameHeader Frame)
		{
			// Get sample rate
			return MPEG_SAMPLE_RATE[Frame.VersionID, Frame.SampleRateID];
		}

		private static Byte getPadding(FrameHeader Frame)
		{
			// Get frame padding
			if (Frame.PaddingBit)
				if (MPEG_LAYER_I == Frame.LayerID) return 4;
				else return 1;
			else return 0;
		}

        private Double getBitRate()
        {
            // Get bit rate, calculate average bit rate if VBR header found
            if ((vbrData.Found) && (vbrData.Frames > 0))
                return Math.Round(
                        (
                            ((Double)vbrData.Bytes / vbrData.Frames - getPadding(HeaderFrame)) *
                            (Double)getSampleRate(HeaderFrame) / getCoefficient(HeaderFrame)
                            ) / 1000.0
                    );
            else
                return getBitRate(HeaderFrame);
        }

        private UInt16 getSampleRate()
        {
            // Get sample rate
            return getSampleRate(HeaderFrame);
        }

        private static Int32 getFrameSize(FrameHeader Frame)
		{
			UInt16 Coefficient;
			UInt16 BitRate;
			UInt16 SampleRate;
			UInt16 Padding;

			// Calculate MPEG frame length
			Coefficient = getCoefficient(Frame);
			BitRate = getBitRate(Frame);
			SampleRate = getSampleRate(Frame);
			Padding = getPadding(Frame);
		
            // This formula only works for Layers II and III
			return (Int32)Math.Floor(Coefficient * BitRate * 1000.0 / SampleRate) + Padding; 
		}

		private static Boolean isXing(Int32 Index, Byte[] Data)
		{
			// Get true if Xing encoder
			return ( (Data[Index] == 0) &&
				(Data[Index + 1] == 0) &&
				(Data[Index + 2] == 0) &&
				(Data[Index + 3] == 0) &&
				(Data[Index + 4] == 0) &&
				(Data[Index + 5] == 0) );
		}

		private static VBRData getXingInfo(BufferedBinaryReader source, Int64 position)
		{
			var result = new VBRData();
            var data = new Byte[8];

            result.Found = true;
			result.ID = VBR_ID_XING.ToCharArray();
            source.Seek(4, SeekOrigin.Current);
            source.Read(data, 0, 8);

            result.Frames =
                data[0] * 0x1000000 +
                data[1] * 0x10000 +
                data[2] * 0x100 +
                data[3];
            result.Bytes =
                data[4] * 0x1000000 +
                data[5] * 0x10000 +
                data[6] * 0x100 +
                data[7];

            source.Seek(103, SeekOrigin.Current);

            result.Scale = (Byte)source.ReadByte();
            source.Read(data, 0, 8);
            result.VendorID = Utils.Latin1Encoding.GetString(data, 0, 8);

            return result;
		}

		private static VBRData getFhGInfo(BufferedBinaryReader source, Int64 position)
		{
			var result = new VBRData();
            var data = new Byte[9];

			// Extract FhG VBR info at given position
			result.Found = true;
			result.ID = VBR_ID_FHG.ToCharArray();
            source.Seek(5, SeekOrigin.Current);
            source.Read(data, 0, 9);

            result.Scale = data[0];
            result.Bytes =
                data[1] * 0x1000000 +
                data[2] * 0x10000 +
                data[3] * 0x100 +
                data[4];
            result.Frames =
                data[5] * 0x1000000 +
                data[6] * 0x10000 +
                data[7] * 0x100 +
                data[8];

            result.VendorID = "";
	
			return result;
		}

		private static VBRData findVBR(BufferedBinaryReader source, Int64 position) 
		{
			VBRData result;
            var data = new Byte[4];

            // Check for VBR header at given position
            source.Seek(position, SeekOrigin.Begin);

            source.Read(data, 0, 4);
            var vbrId = Utils.Latin1Encoding.GetString(data);

			if ( VBR_ID_XING.Equals(vbrId) ) result = getXingInfo(source, position);
            else if (VBR_ID_FHG.Equals(vbrId)) result = getFhGInfo(source, position);
            else
            {
                result = new VBRData();
                result.Reset();
            }

			return result;
		}

		private static Byte getVBRDeviation(FrameHeader Frame)
		{
			// Calculate VBR deviation
			if (MPEG_VERSION_1 == Frame.VersionID)
				if (Frame.ModeID != MPEG_CM_MONO) return 36;
				else return 21;
			else
				if (Frame.ModeID != MPEG_CM_MONO) return 21;
			else return 13;
		}

        private Double getDuration()
        {
            // Calculate song duration
            if (HeaderFrame.Found)
                if ((vbrData.Found) && (vbrData.Frames > 0))
                    return vbrData.Frames * getCoefficient(HeaderFrame) * 8.0 * 1000.0 / getSampleRate(HeaderFrame);
                else
                {
                    var MPEGSize = sizeInfo.FileSize - sizeInfo.TotalTagSize;
                    return (MPEGSize/* - HeaderFrame.Position*/) / getBitRate(HeaderFrame) * 8;
                }
            else
                return 0;
        }

        private static FrameHeader findFrame(BufferedBinaryReader source, ref VBRData oVBR, SizeInfo sizeInfo)
		{
			var headerData = new Byte[4];  
			var result = new FrameHeader();

            source.Read(headerData, 0, 4);
            result.Found = isValidFrameHeader(headerData);

            /*
             * Many things can actually be found before a proper MP3 header :
             *    - Padding with 0x55, 0xAA and even 0xFF bytes
             *    - RIFF header declaring either MP3 or WAVE data
             *    - Xing encoder-specific frame
             *    - One of the above with a few "parasite" bytes before their own header
             * 
             * The most solid way to deal with all of them is to "scan" the file until proper MP3 header is found.
             * This method may not the be fastest, but ensures audio data is actually detected, whatever garbage lies before
             */

            if (!result.Found)
            {
                // "Quick win" for files starting with padding bytes
                // 4 identical bytes => MP3 starts with padding bytes => Skip padding
                if ((headerData[0] == headerData[1]) && (headerData[1] == headerData[2]) && (headerData[2] == headerData[3]) ) 
                {
                    // Scan the whole padding until it stops
                    while (headerData[0] == source.ReadByte());

                    source.Seek(-1, SeekOrigin.Current);

                    // If padding uses 0xFF bytes, take one step back in case MP3 header lies there
                    if (0xFF == headerData[0]) source.Seek(-1, SeekOrigin.Current);

                    source.Read(headerData, 0, 4);
                    result.Found = isValidFrameHeader(headerData);
                }

                // Blindly look for the MP3 header
                if (!result.Found)
                {
                    source.Seek(-4, SeekOrigin.Current);
                    var limit = sizeInfo.ID3v2Size + (Int64)Math.Round((source.Length-sizeInfo.ID3v2Size) * 0.3);

                    // Look for the beginning of the MP3 header (2nd byte is variable, so it cannot be searched that way)
                    while (!result.Found && source.Position < limit)
                    {
                        while (0xFF != source.ReadByte() && source.Position < limit) ;
                        
                        source.Seek(-1, SeekOrigin.Current);
                        source.Read(headerData, 0, 4);
                        result.Found = isValidFrameHeader(headerData);

                        // Valid header candidate found
                        // => let's see if it is a legit MP3 header by using its Size descriptor to find the next header
                        if (result.Found)
                        {
                            result.LoadFromByteArray(headerData);

                            result.Position = source.Position - 4;
                            result.Size = getFrameSize(result);

                            var nextHeaderData = new Byte[4];
                            source.Seek(result.Position + result.Size, SeekOrigin.Begin);
                            source.Read(nextHeaderData, 0, 4);
                            result.Found = isValidFrameHeader(nextHeaderData);

                            if (result.Found)
                            {
                                source.Seek(result.Position + 4, SeekOrigin.Begin); // Go back to header candidate position
                                break;
                            }
                            else
                            {
                                // Restart looking for a candidate
                                source.Seek(result.Position + 1, SeekOrigin.Begin);
                            }
                        } else
                        {
                            source.Seek(-3, SeekOrigin.Current);
                        }
                    }
                }
            }

            if (result.Found)
            {
                result.LoadFromByteArray(headerData);

                result.Position = source.Position - 4;

                // result.Xing = isXing(i + 4, Data); // Will look into it when encoder ID is needed by upper interfaces

                // Look for VBR signature
                oVBR = findVBR(source, result.Position + getVBRDeviation(result));
            }

			return result;
		}

        /* Nightmarish implementation to be redone when Vendor ID is really useful
		private static String findVendorID(byte[] Data, int Size)
		{
			String VendorID;
			String result = "";

			// Search for vendor ID
			if ( (Data.Length - Size - 8) < 0 ) Size = Data.Length - 8;
			for (int i=0; i <= Size; i++)
			{
                VendorID = Utils.Latin1Encoding.GetString(Data, Data.Length - i - 8, 4);
				if (VENDOR_ID_LAME == VendorID)
				{
                    result = VendorID + Utils.Latin1Encoding.GetString(Data, Data.Length - i - 4, 4);
					break;
				}
				else if (VENDOR_ID_GOGO_NEW == VendorID)
				{
					result = VendorID;
					break;
				}
			}
			return result;
		}
        */

/* Unused for now

		private String getVersion()
		{
			// Get MPEG version name
			return MPEG_VERSION[HeaderFrame.VersionID];
		}

		private String getLayer()
		{
			// Get MPEG layer name
			return MPEG_LAYER[HeaderFrame.LayerID];
		}

		private String getChannelMode()
		{
			// Get channel mode name
			return MPEG_CM_MODE[HeaderFrame.ModeID];
		}

		private String getEmphasis()
		{
			// Get emphasis name
			return MPEG_EMPHASIS[HeaderFrame.EmphasisID];
		}

		private long getFrames()
		{
 			// Get total number of frames, calculate if VBR header not found
			if (vbrData.Found) return vbrData.Frames;
			else
			{
                long MPEGSize = sizeInfo.FileSize - sizeInfo.ID3v2Size - sizeInfo.ID3v1Size - sizeInfo.APESize;
    
				return (long)Math.Floor(1.0*(MPEGSize - HeaderFrame.Position) / getFrameSize(HeaderFrame));
			}
		}

		private byte getVBREncoderID()
		{
			// Guess VBR encoder and get ID
			byte result = 0;

			if (VENDOR_ID_LAME == vbrData.VendorID.Substring(0, 4))
				result = MPEG_ENCODER_LAME;
			if (VENDOR_ID_GOGO_NEW == vbrData.VendorID.Substring(0, 4))
				result = MPEG_ENCODER_GOGO;
			if (VENDOR_ID_GOGO_OLD == vbrData.VendorID.Substring(0, 4))
				result = MPEG_ENCODER_GOGO;
			if ( StreamUtils.StringEqualsArr(VBR_ID_XING,vbrData.ID) &&
				(vbrData.VendorID.Substring(0, 4) != VENDOR_ID_LAME) &&
				(vbrData.VendorID.Substring(0, 4) != VENDOR_ID_GOGO_NEW) &&
				(vbrData.VendorID.Substring(0, 4) != VENDOR_ID_GOGO_OLD) )
				result = MPEG_ENCODER_XING;
			if ( StreamUtils.StringEqualsArr(VBR_ID_FHG,vbrData.ID))
				result = MPEG_ENCODER_FHG;

			return result;
		}

		private byte getCBREncoderID()
		{
			// Guess CBR encoder and get ID
			byte result = MPEG_ENCODER_FHG;

			if ( (HeaderFrame.OriginalBit) &&
				(HeaderFrame.ProtectionBit) )
				result = MPEG_ENCODER_LAME;
			if ( (getBitRate(HeaderFrame) <= 160) &&
				(MPEG_CM_STEREO == HeaderFrame.ModeID)) 
				result = MPEG_ENCODER_BLADE;
			if ((HeaderFrame.CopyrightBit) &&
				(HeaderFrame.OriginalBit) &&
				(! HeaderFrame.ProtectionBit) )
				result = MPEG_ENCODER_XING;
			if ((HeaderFrame.Xing) &&
				(HeaderFrame.OriginalBit) )
				result = MPEG_ENCODER_XING;
			if (MPEG_LAYER_II == HeaderFrame.LayerID)
				result = MPEG_ENCODER_QDESIGN;
			if ((MPEG_CM_DUAL_CHANNEL == HeaderFrame.ModeID) &&
				(HeaderFrame.ProtectionBit) )
				result = MPEG_ENCODER_SHINE;
			if (VENDOR_ID_LAME == vendorID.Substring(0, 4))
				result = MPEG_ENCODER_LAME;
			if (VENDOR_ID_GOGO_NEW == vendorID.Substring(0, 4))
				result = MPEG_ENCODER_GOGO;
		
			return result;
		}

		private byte getEncoderID()
		{
			// Get guessed encoder ID
			if (HeaderFrame.Found)
				if (vbrData.Found) return getVBREncoderID();
				else return getCBREncoderID();
			else
				return 0;
		}

		private String getEncoder()
		{
			String VendorID = "";
			String result;

			// Get guessed encoder name and encoder version for LAME
			result = MPEG_ENCODER[getEncoderID()];
			if (vbrData.VendorID != "") VendorID = vbrData.VendorID;
			if (vendorID != "") VendorID = vendorID;
			if ( (MPEG_ENCODER_LAME == getEncoderID()) &&
				(VendorID.Length >= 8) &&
				Char.IsDigit(VendorID[4]) &&
				(VendorID[5] == '.') &&
				(Char.IsDigit(VendorID[6]) &&
				Char.IsDigit(VendorID[7]) ))
				result =
					result + (char)32 +
					VendorID[4] +
					VendorID[5] +
					VendorID[6] +
					VendorID[7];
			return result;
		}

		private bool getValid()
		{
			// Check for right MPEG file data
			return
				((HeaderFrame.Found) &&
				(getBitRate() >= MIN_MPEG_BIT_RATE) &&
				(getBitRate() <= MAX_MPEG_BIT_RATE) &&
				(getDuration() >= MIN_ALLOWED_DURATION));
		}*/

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;
            resetData();

			var result = false;

            var reader = new BufferedBinaryReader(source.BaseStream);

            reader.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);
			HeaderFrame = findFrame(reader, ref vbrData, sizeInfo);

            // Search for vendor ID at the end if CBR encoded
/*
 *  This is a nightmarish implementation; to be redone when vendor ID is required by upper interfaces
 *  
			if ( (HeaderFrame.Found) && (! FVBR.Found) )
			{
                fs.Seek(sizeInfo.FileSize - Data.Length - sizeInfo.ID3v1Size - sizeInfo.APESize, SeekOrigin.Begin);
                fs.Read(Data, 0, Data.Length);
                vendorID = findVendorID(Data, HeaderFrame.Size * 5);
			}
*/
			result = HeaderFrame.Found;

            if (!result)
            {
                resetData();
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Could not detect MPEG Audio header starting @ "+sizeInfo.ID3v2Size);
            }
	
			return result;
		}

    }
}