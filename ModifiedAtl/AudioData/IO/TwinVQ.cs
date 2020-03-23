using ATL.Logging;
using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;
using Commons;
using System.Collections.Generic;
using System.Text;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for TwinVQ files manipulation (extension : .VQF)
    /// </summary>
	class TwinVQ : MetaDataIO, IAudioDataIO
	{
	 
		// Used with ChannelModeID property
		public const Byte TWIN_CM_MONO = 1;               // Index for mono mode
		public const Byte TWIN_CM_STEREO = 2;           // Index for stereo mode

		// Channel mode names
		public static readonly String[] TWIN_MODE = new String[3] {"Unknown", "Mono", "Stereo"};

        // Twin VQ header ID
        private const String TWIN_ID = "TWIN";

        private static IDictionary<String, Byte> frameMapping; // Mapping between TwinVQ frame codes and ModifiedAtl frame codes


        // Private declarations
        private Byte channelModeID;
		private Int32 sampleRate;

        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private readonly String filePath;


        public Byte ChannelModeID // Channel mode code
	        =>
		        channelModeID;

        public String ChannelMode // Channel mode name
	        =>
		        getChannelMode();

        public Boolean Corrupted // True if file corrupted
	        =>
		        isCorrupted();

        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier according to the ID3v2 version
            if (frameMapping.ContainsKey(ID)) supportedMetaId = frameMapping[ID];

            return supportedMetaId;
        }


        // TwinVQ chunk header
        private class ChunkHeader
		{
            public String ID;
			public UInt32 Size;                                            // Chunk size
			public void Reset()
			{
				Size = 0;
			}
		}

		// File header data - for internal use
		private class HeaderInfo
		{
			// Real structure of TwinVQ file header
			public Char[] ID = new Char[4];                           // Always "TWIN"
			public Char[] Version = new Char[8];                         // Version ID
			public UInt32 Size;                                           // Header size
			public ChunkHeader Common = new ChunkHeader();      // Common chunk header
			public UInt32 ChannelMode;             // Channel mode: 0 - mono, 1 - stereo
			public UInt32 BitRate;                                     // Total bit rate
			public UInt32 SampleRate;                               // Sample rate (khz)
			public UInt32 SecurityLevel;                                     // Always 0
		}


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Int32 SampleRate // Sample rate (hz)
	        =>
		        sampleRate;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE) || (metaDataType == MetaDataIOFactory.TAG_ID3V1);
        }

        // IMetaDataIO
        protected override Int32 getDefaultTagOffset()
        {
            return TO_BUILTIN;
        }
        protected override Int32 getImplementedTagType()
        {
            return MetaDataIOFactory.TAG_NATIVE;
        }
        public override Byte FieldCodeFixedLength => 4;

        protected override Boolean isLittleEndian => false;


        // ---------- CONSTRUCTORS & INITIALIZERS

        static TwinVQ()
        {
            frameMapping = new Dictionary<String, Byte>
            {
                { "NAME", TagData.TAG_FIELD_TITLE },
                { "ALBM", TagData.TAG_FIELD_ALBUM },
                { "AUTH", TagData.TAG_FIELD_ARTIST },
                { "(c) ", TagData.TAG_FIELD_COPYRIGHT },
                { "MUSC", TagData.TAG_FIELD_COMPOSER },
                { "CDCT", TagData.TAG_FIELD_CONDUCTOR },
                { "TRCK", TagData.TAG_FIELD_TRACK_NUMBER },
                { "DATE", TagData.TAG_FIELD_RECORDING_DATE },
                { "GENR", TagData.TAG_FIELD_GENRE },
                { "COMT", TagData.TAG_FIELD_COMMENT }
                // TODO - handle integer extension sub-chunks : YEAR, TRAC
            };
        }

        private void resetData()
        {
            duration = 0;
            bitrate = 0;
            isValid = false;

            channelModeID = 0;
            sampleRate = 0;

            ResetData();
        }

        public TwinVQ(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }

        
        // ---------- SUPPORT METHODS

        private static Boolean readHeader(BinaryReader source, ref HeaderInfo Header)
		{
			var result = true;

			// Read header and get file size
			Header.ID = source.ReadChars(4);
			Header.Version = source.ReadChars(8);
			Header.Size = StreamUtils.ReverseUInt32( source.ReadUInt32() );
            Header.Common.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
			Header.Common.Size = StreamUtils.ReverseUInt32( source.ReadUInt32() );
			Header.ChannelMode = StreamUtils.ReverseUInt32( source.ReadUInt32() );
			Header.BitRate = StreamUtils.ReverseUInt32( source.ReadUInt32() );
			Header.SampleRate = StreamUtils.ReverseUInt32( source.ReadUInt32() );
			Header.SecurityLevel = StreamUtils.ReverseUInt32( source.ReadUInt32() );

			return result;
		}

        // Get channel mode from header
        private static Byte getChannelModeID(HeaderInfo Header)
		{
            switch(Header.ChannelMode)
			{
				case 0: return TWIN_CM_MONO;
				case 1: return TWIN_CM_STEREO;
				default: return 0;
			}
		}

        // Get bit rate from header
        private static UInt32 getBitRate(HeaderInfo Header)
		{
            return Header.BitRate;
		}

        // Get real sample rate from header  
        private Int32 GetSampleRate(HeaderInfo Header)
		{
            var result = (Int32)Header.SampleRate;
			switch(result)
			{
				case 11: result = 11025; break;
				case 22: result = 22050; break;
				case 44: result = 44100; break;
				default: result = (UInt16)(result * 1000); break;
			}
			return result;
		}

        // Get duration from header
        private Double getDuration(HeaderInfo Header)
		{
            return Math.Abs(sizeInfo.FileSize - Header.Size - 20) * 1000.0 / 125.0 / (Double)Header.BitRate;
		}

		private static Boolean headerEndReached(ChunkHeader Chunk)
		{
			// Check for header end
			return ( ((Byte)(Chunk.ID[0]) < 32) ||
				((Byte)(Chunk.ID[1]) < 32) ||
				((Byte)(Chunk.ID[2]) < 32) ||
				((Byte)(Chunk.ID[3]) < 32) ||
				"DSIZ".Equals(Chunk.ID) );
		}

        private Boolean readTag(BinaryReader source, HeaderInfo Header, ReadTagParams readTagParams)
		{ 
			var chunk = new ChunkHeader();
            String data;
            var result = false;
            var first = true;
            Int64 tagStart = -1;

			source.BaseStream.Seek(40, SeekOrigin.Begin);
			do
			{
                // Read chunk header (length : 8 bytes)
                chunk.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
                chunk.Size = StreamUtils.ReverseUInt32(source.ReadUInt32());

				// Read chunk data and set tag item if chunk header valid
				if ( headerEndReached(chunk) ) break;

                if (first)
                {
                    tagStart = source.BaseStream.Position - 8;
                    first = false;
                }
                tagExists = true; // If something else than mandatory info is stored, we can consider metadata is present
                data = Encoding.UTF8.GetString(source.ReadBytes((Int32)chunk.Size)).Trim();

                SetMetaField(chunk.ID, data, readTagParams.ReadAllMetaFrames);

                result = true;
            }
			while (source.BaseStream.Position < source.BaseStream.Length);

            if (readTagParams.PrepareForWriting)
            {
                // Zone goes from the first field after COMM to the last field before DSIZ
                if (-1 == tagStart) structureHelper.AddZone(source.BaseStream.Position - 8, 0);
                else structureHelper.AddZone(tagStart, (Int32)(source.BaseStream.Position - tagStart - 8) );
                structureHelper.AddSize(12, (UInt32)Header.Size);
            }

            return result;
		}

        private String getChannelMode()
		{
			return TWIN_MODE[channelModeID];
		}

		private Boolean isCorrupted()
		{
			// Check for file corruption
			return ( (isValid) &&
				((0 == channelModeID) ||
                (bitrate < 8000) || (bitrate > 192000) ||
				(sampleRate < 8000) || (sampleRate > 44100) ||
				(duration < 0.1) || (duration > 10000)) );
		}

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            var Header = new HeaderInfo();

            resetData();
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            var result = readHeader(source, ref Header);
			// Process data if loaded and header valid
			if ( (result) && StreamUtils.StringEqualsArr(TWIN_ID,Header.ID) )
			{
				isValid = true;
				// Fill properties with header data
				channelModeID = getChannelModeID(Header);
				bitrate = getBitRate(Header);
				sampleRate = GetSampleRate(Header);
				duration = getDuration(Header);
				// Get tag information and fill properties
				readTag(source, Header, readTagParams);
			}
			return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            var map = tag.ToMap();

            // Supported textual fields
            foreach (var frameType in map.Keys)
            {
                foreach (var s in frameMapping.Keys)
                {
                    if (frameType == frameMapping[s])
                    {
                        if (map[frameType].Length > 0) // No frame with empty value
                        {
                            writeTextFrame(w, s, map[frameType]);
                            result++;
                        }
                        break;
                    }
                }
            }

            // Other textual fields
            foreach (var fieldInfo in tag.AdditionalFields)
            {
                if ((fieldInfo.TagType.Equals(MetaDataIOFactory.TAG_ANY) || fieldInfo.TagType.Equals(getImplementedTagType())) && !fieldInfo.MarkedForDeletion && fieldInfo.NativeFieldCode.Length > 0)
                {
                    writeTextFrame(w, fieldInfo.NativeFieldCode, fieldInfo.Value);
                    result++;
                }
            }

            return result;
        }

        private void writeTextFrame(BinaryWriter writer, String frameCode, String text)
        {
            writer.Write(Utils.Latin1Encoding.GetBytes(frameCode));
            var textBytes = Encoding.UTF8.GetBytes(text);
            writer.Write(StreamUtils.ReverseUInt32((UInt32)textBytes.Length));
            writer.Write(textBytes);
        }

        // Specific implementation for conservation of fields that are required for playback
        public override Boolean Remove(BinaryWriter w)
        {
            var tag = new TagData();

            foreach (var b in frameMapping.Values)
            {
                tag.IntegrateValue(b, "");
            }

            String fieldCode;
            foreach (var fieldInfo in GetAdditionalFields())
            {
                fieldCode = fieldInfo.NativeFieldCode.ToLower();
                if (!fieldCode.StartsWith("_") && !fieldCode.Equals("DSIZ") && !fieldCode.Equals("COMM"))
                {
                    var emptyFieldInfo = new MetaFieldInfo(fieldInfo);
                    emptyFieldInfo.MarkedForDeletion = true;
                    tag.AdditionalFields.Add(emptyFieldInfo);
                }
            }

            var r = new BinaryReader(w.BaseStream);
            return Write(r, w, tag);
        }

    }
}