using ATL.Logging;
using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;
using Commons;
using System.Collections.Generic;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for PCM (uncompressed audio) files manipulation (extension : .WAV)
    /// 
    /// Implementation notes
    /// 
    ///     1. BEXT metadata - UMID field
    ///     
    ///     UMID field is decoded "as is" using the hex notation. No additional interpretation has been done so far.
    ///     
    /// </summary>
	class WAV : MetaDataIO, IAudioDataIO, IMetaDataEmbedder
    {
        // Format type names
        public const String WAV_FORMAT_UNKNOWN = "Unknown";
        public const String WAV_FORMAT_PCM = "Windows PCM";
        public const String WAV_FORMAT_ADPCM = "Microsoft ADPCM";
        public const String WAV_FORMAT_ALAW = "A-LAW";
        public const String WAV_FORMAT_MULAW = "MU-LAW";
        public const String WAV_FORMAT_DVI_IMA_ADPCM = "DVI/IMA ADPCM";
        public const String WAV_FORMAT_MP3 = "MPEG Layer III";

        private const String HEADER_RIFF = "RIFF";
        private const String HEADER_RIFX = "RIFX";

        private const String FORMAT_WAVE = "WAVE";

        // Standard sub-chunks
        private const String CHUNK_FORMAT = "fmt ";
        private const String CHUNK_FACT = "fact";
        private const String CHUNK_DATA = "data";

        // Broadcast Wave metadata sub-chunk
        private const String CHUNK_BEXT = BextTag.CHUNK_BEXT;
        private const String CHUNK_INFO = InfoTag.CHUNK_LIST;
        private const String CHUNK_IXML = IXmlTag.CHUNK_IXML;
        private const String CHUNK_ID3 = "id3 ";


        // Used with ChannelModeID property
        public const Byte WAV_CM_MONO = 1;                     // Index for mono mode
        public const Byte WAV_CM_STEREO = 2;                 // Index for stereo mode

        // Channel mode names
        public String[] WAV_MODE = new String[3] { "Unknown", "Mono", "Stereo" };

        //		private ushort formatID;
        private UInt16 channelNumber;
        private UInt32 sampleRate;
        private UInt32 bytesPerSecond;
        //		private ushort blockAlign;
        private UInt16 bitsPerSample;
        private Int32 sampleNumber;
        private UInt32 headerSize;

        private Double bitrate;
        private Double duration;

        private SizeInfo sizeInfo;
        private readonly String filePath;

        private Boolean _isLittleEndian;

        private Int64 id3v2Offset;
        private FileStructureHelper id3v2StructureHelper = new FileStructureHelper(false);


        private static IDictionary<String, Byte> frameMapping; // Mapping between WAV frame codes and ModifiedAtl frame codes


        /* Unused for now
        public ushort FormatID // Format type code
		{
			get { return this.formatID; }
		}	
		public byte ChannelNumber // Number of channels
		{
			get { return this.channelNumber; }
		}		 
		public String ChannelMode // Channel mode name
		{
			get { return this.getChannelMode(); }
		}	
		public ushort BlockAlign // Block alignment
		{
			get { return this.blockAlign; }
		}
        */


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Int32 SampleRate => (Int32)sampleRate;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossless;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_ID3V1 || metaDataType == MetaDataIOFactory.TAG_ID3V2 || metaDataType == MetaDataIOFactory.TAG_NATIVE); // Native for bext, info and iXML chunks
        }


        // MetaDataIO
        protected override Int32 getDefaultTagOffset()
        {
            return TO_BUILTIN;
        }

        protected override Int32 getImplementedTagType()
        {
            return MetaDataIOFactory.TAG_NATIVE;
        }

        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier
            if (frameMapping.ContainsKey(ID)) supportedMetaId = frameMapping[ID];

            return supportedMetaId;
        }

        protected override Boolean isLittleEndian => _isLittleEndian;


        // IMetaDataEmbedder
        public Int64 HasEmbeddedID3v2 => id3v2Offset;

        public UInt32 ID3v2EmbeddingHeaderSize => 8;

        public FileStructureHelper.Zone Id3v2Zone => id3v2StructureHelper.GetZone(CHUNK_ID3);


        // ---------- CONSTRUCTORS & INITIALIZERS

        static WAV()
        {
            frameMapping = new Dictionary<String, Byte>
            {
                { "bext.description", TagData.TAG_FIELD_GENERAL_DESCRIPTION },
                { "info.INAM", TagData.TAG_FIELD_TITLE },
                { "info.TITL", TagData.TAG_FIELD_TITLE },
                { "info.IART", TagData.TAG_FIELD_ARTIST },
                { "info.ICOP", TagData.TAG_FIELD_COPYRIGHT },
                { "info.IGNR", TagData.TAG_FIELD_GENRE },
                { "info.IRTD", TagData.TAG_FIELD_RATING },
                { "info.YEAR", TagData.TAG_FIELD_RECORDING_YEAR },
                { "info.TRCK", TagData.TAG_FIELD_TRACK_NUMBER },
                { "info.ICMT", TagData.TAG_FIELD_COMMENT }
            };
        }

        protected void resetData()
        {
            duration = 0;
            bitrate = 0;

            //            formatID = 0;
            //            blockAlign = 0;
            channelNumber = 0;
            sampleRate = 0;
            bytesPerSecond = 0;
            bitsPerSample = 0;
            sampleNumber = 0;
            headerSize = 0;

            id3v2Offset = -1;
            id3v2StructureHelper.Clear();

            ResetData();
        }

        public WAV(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // ---------- SUPPORT METHODS

        private Boolean readWAV(Stream source, ReadTagParams readTagParams)
        {
            var result = true;
            UInt32 riffChunkSize;
            Int64 riffChunkSizePos;
            var data = new Byte[4];

            source.Seek(0, SeekOrigin.Begin);

            // Read header
            source.Read(data, 0, 4);
            var str = Utils.Latin1Encoding.GetString(data);
            if (str.Equals(HEADER_RIFF))
            {
                _isLittleEndian = true;
            }
            else if (str.Equals(HEADER_RIFX))
            {
                _isLittleEndian = false;
            }
            else
            {
                return false;
            }

            // Force creation of FileStructureHelper with detected endianness
            structureHelper = new FileStructureHelper(isLittleEndian);
            id3v2StructureHelper = new FileStructureHelper(isLittleEndian);

            riffChunkSizePos = source.Position;
            source.Read(data, 0, 4);
            if (isLittleEndian) riffChunkSize = StreamUtils.DecodeUInt32(data); else riffChunkSize = StreamUtils.DecodeBEUInt32(data);

            // Format code
            source.Read(data, 0, 4);
            str = Utils.Latin1Encoding.GetString(data);
            if (!str.Equals(FORMAT_WAVE)) return false;


            String subChunkId;
            UInt32 chunkSize;
            Int64 chunkDataPos;
            var foundBext = false;
            var foundInfo = false;
            var foundIXml = false;

            // Sub-chunks loop
            while (source.Position < riffChunkSize + 8)
            {
                // Chunk ID
                source.Read(data, 0, 4);
                if (0 == data[0]) // Sometimes data segment ends with a parasite null byte
                {
                    source.Seek(-3, SeekOrigin.Current);
                    source.Read(data, 0, 4);
                }

                subChunkId = Utils.Latin1Encoding.GetString(data);

                // Chunk size
                source.Read(data, 0, 4);
                if (isLittleEndian) chunkSize = StreamUtils.DecodeUInt32(data); else chunkSize = StreamUtils.DecodeBEUInt32(data);

                chunkDataPos = source.Position;

                if (subChunkId.Equals(CHUNK_FORMAT))
                {
                    source.Seek(2, SeekOrigin.Current); // FormatId

                    source.Read(data, 0, 2);
                    if (isLittleEndian) channelNumber = StreamUtils.DecodeUInt16(data); else channelNumber = StreamUtils.DecodeBEUInt16(data);
                    if (channelNumber != WAV_CM_MONO && channelNumber != WAV_CM_STEREO) return false;

                    source.Read(data, 0, 4);
                    if (isLittleEndian) sampleRate = StreamUtils.DecodeUInt32(data); else sampleRate = StreamUtils.DecodeBEUInt32(data);

                    source.Read(data, 0, 4);
                    if (isLittleEndian) bytesPerSecond = StreamUtils.DecodeUInt32(data); else bytesPerSecond = StreamUtils.DecodeBEUInt32(data);

                    source.Seek(2, SeekOrigin.Current); // BlockAlign

                    source.Read(data, 0, 2);
                    if (isLittleEndian) bitsPerSample = StreamUtils.DecodeUInt16(data); else bitsPerSample = StreamUtils.DecodeBEUInt16(data);
                }
                else if (subChunkId.Equals(CHUNK_DATA))
                {
                    headerSize = riffChunkSize - chunkSize;
                }
                else if (subChunkId.Equals(CHUNK_FACT))
                {
                    source.Read(data, 0, 4);
                    if (isLittleEndian) sampleNumber = StreamUtils.DecodeInt32(data); else sampleNumber = StreamUtils.DecodeBEInt32(data);
                }
                else if (subChunkId.Equals(CHUNK_BEXT))
                {
                    structureHelper.AddZone(source.Position - 8, (Int32)(chunkSize + 8), subChunkId);
                    structureHelper.AddSize(riffChunkSizePos, riffChunkSize, subChunkId);

                    foundBext = true;
                    tagExists = true;

                    BextTag.FromStream(source, this, readTagParams);
                }
                else if (subChunkId.Equals(CHUNK_INFO))
                {
                    // Purpose of the list should be INFO
                    source.Read(data, 0, 4);
                    var purpose = Utils.Latin1Encoding.GetString(data, 0, 4);
                    if (purpose.Equals(InfoTag.PURPOSE_INFO))
                    {
                        structureHelper.AddZone(source.Position - 12, (Int32)(chunkSize + 8), subChunkId);
                        structureHelper.AddSize(riffChunkSizePos, riffChunkSize, subChunkId);

                        foundInfo = true;
                        tagExists = true;

                        InfoTag.FromStream(source, this, readTagParams, chunkSize);
                    }
                }
                else if (subChunkId.Equals(CHUNK_IXML))
                {
                    structureHelper.AddZone(source.Position - 8, (Int32)(chunkSize + 8), subChunkId);
                    structureHelper.AddSize(riffChunkSizePos, riffChunkSize, subChunkId);

                    foundIXml = true;
                    tagExists = true;

                    IXmlTag.FromStream(source, this, readTagParams, chunkSize);
                }
                else if (subChunkId.Equals(CHUNK_ID3))
                {
                    id3v2Offset = source.Position;

                    // Zone is already added by Id3v2.Read
                    id3v2StructureHelper.AddZone(id3v2Offset - 8, (Int32)(chunkSize + 8), subChunkId);
                    id3v2StructureHelper.AddSize(riffChunkSizePos, riffChunkSize, subChunkId);
                }

                source.Seek(chunkDataPos + chunkSize, SeekOrigin.Begin);
            }

            if (-1 == id3v2Offset)
            {
                id3v2Offset = 0; // Switch status to "tried to read, but nothing found"

                if (readTagParams.PrepareForWriting)
                {
                    id3v2StructureHelper.AddZone(source.Position, 0, CHUNK_ID3);
                    id3v2StructureHelper.AddSize(riffChunkSizePos, riffChunkSize, CHUNK_ID3);
                }
            }

            // Add zone placeholders for future tag writing
            if (readTagParams.PrepareForWriting)
            {
                if (!foundBext)
                {
                    structureHelper.AddZone(source.Position, 0, CHUNK_BEXT);
                    structureHelper.AddSize(riffChunkSizePos, riffChunkSize, CHUNK_BEXT);
                }
                if (!foundInfo)
                {
                    structureHelper.AddZone(source.Position, 0, CHUNK_INFO);
                    structureHelper.AddSize(riffChunkSizePos, riffChunkSize, CHUNK_INFO);
                }
                if (!foundIXml)
                {
                    structureHelper.AddZone(source.Position, 0, CHUNK_IXML);
                    structureHelper.AddSize(riffChunkSizePos, riffChunkSize, CHUNK_IXML);
                }
            }

            return result;
        }

        /* Unused for now
		private String getFormat()
		{
			// Get format type name
			switch (formatID)
			{
				case 1: return WAV_FORMAT_PCM;
				case 2: return WAV_FORMAT_ADPCM;
				case 6: return WAV_FORMAT_ALAW;
				case 7: return WAV_FORMAT_MULAW;
				case 17: return WAV_FORMAT_DVI_IMA_ADPCM;
				case 85: return WAV_FORMAT_MP3;
				default : return "";  
			}
		}

		private String getChannelMode()
		{
			// Get channel mode name
			return WAV_MODE[channelNumber];
		}
        */

        private Double getDuration()
        {
            // Get duration
            Double result = 0;

            if ((sampleNumber == 0) && (bytesPerSecond > 0))
                result = (Double)(sizeInfo.FileSize - headerSize - sizeInfo.ID3v1Size) / bytesPerSecond;
            if ((sampleNumber > 0) && (sampleRate > 0))
                result = (Double)(sampleNumber / sampleRate);

            return result * 1000.0;
        }

        private Double getBitrate()
        {
            return Math.Round((Double)this.bitsPerSample / 1000.0 * this.sampleRate * this.channelNumber);
        }

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            resetData();

            var result = readWAV(source.BaseStream, readTagParams);

            // Process data if loaded and header valid
            if (result)
            {
                bitrate = getBitrate();
                duration = getDuration();
            }
            return result;
        }

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            if (zone.Equals(CHUNK_BEXT) && BextTag.IsDataEligible(this)) result += BextTag.ToStream(w, isLittleEndian, this);
            else if (zone.Equals(CHUNK_INFO) && InfoTag.IsDataEligible(this)) result += InfoTag.ToStream(w, isLittleEndian, this);
            else if (zone.Equals(CHUNK_IXML) && IXmlTag.IsDataEligible(this)) result += IXmlTag.ToStream(w, isLittleEndian, this);

            return result;
        }

        public void WriteID3v2EmbeddingHeader(BinaryWriter w, Int64 tagSize)
        {
            w.Write(Utils.Latin1Encoding.GetBytes(CHUNK_ID3));
            if (isLittleEndian)
            {
                w.Write((Int32)(tagSize));
            }
            else
            {
                w.Write(StreamUtils.EncodeBEInt32((Int32)(tagSize)));
            }
        }

    }
}