using System;
using System.IO;
using ATL.Logging;
using System.Collections.Generic;
using System.Text;
using Commons;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Advanced Audio Coding files manipulation (extensions : .AAC, .MP4, .M4A, .M4V)
    /// 
    /// Implementation notes
    /// 
    ///     1. Tag edition optimization through the use of padding frames
    /// 
    ///     Current implementation doesn't use the extra space allocated by 'free' padding frames, and pulls/pushes the 'mdat' frame regardless of the size of the edited data.
    ///     A faster, more optimized way of doing things would be to use padding space as far as edited data size fits into it, thus preventing the entire file to be rewritten.
    ///     
    ///     2. LATM and LOAS/LATM support is missing
    ///     
    ///     3. MP4 files with their 'mdat' atom located before their 'moov' atom have not been tested
    ///     
    ///     
    /// 
    /// </summary>
	class AAC : MetaDataIO, IAudioDataIO
    {

        // Header type codes
        public const Byte AAC_HEADER_TYPE_UNKNOWN = 0;                       // Unknown
        public const Byte AAC_HEADER_TYPE_ADIF = 1;                          // ADIF
        public const Byte AAC_HEADER_TYPE_ADTS = 2;                          // ADTS
        public const Byte AAC_HEADER_TYPE_MP4 = 3;                           // MP4

        // Header type names
        public static readonly String[] AAC_HEADER_TYPE = { "Unknown", "ADIF", "ADTS" };

        // MPEG version codes
        public const Byte AAC_MPEG_VERSION_UNKNOWN = 0;                      // Unknown
        public const Byte AAC_MPEG_VERSION_2 = 1;                            // MPEG-2
        public const Byte AAC_MPEG_VERSION_4 = 2;                            // MPEG-4

        // MPEG version names
        public static readonly String[] AAC_MPEG_VERSION = { "Unknown", "MPEG-2", "MPEG-4" };

        // Profile codes
        public const Byte AAC_PROFILE_UNKNOWN = 0;                           // Unknown
        public const Byte AAC_PROFILE_MAIN = 1;                              // Main
        public const Byte AAC_PROFILE_LC = 2;                                // LC
        public const Byte AAC_PROFILE_SSR = 3;                               // SSR
        public const Byte AAC_PROFILE_LTP = 4;                               // LTP

        // Profile names
        public static readonly String[] AAC_PROFILE = { "Unknown", "AAC Main", "AAC LC", "AAC SSR", "AAC LTP" };

        // Bit rate type codes
        public const Byte AAC_BITRATE_TYPE_UNKNOWN = 0;                      // Unknown
        public const Byte AAC_BITRATE_TYPE_CBR = 1;                          // CBR
        public const Byte AAC_BITRATE_TYPE_VBR = 2;                          // VBR

        // Bit rate type names
        public static readonly String[] AAC_BITRATE_TYPE = { "Unknown", "CBR", "VBR" };

        // Sample rate values
        private static readonly Int32[] SAMPLE_RATE = {   96000, 88200, 64000, 48000, 44100, 32000,
                                                        24000, 22050, 16000, 12000, 11025, 8000,
                                                        0, 0, 0, 0 };

        private static readonly Byte[] CORE_SIGNATURE = { 0, 0, 0, 8, 105, 108, 115, 116 }; // (int32)8 followed by "ilst" field code

        private const String ZONE_MP4_NEROCHAPTERS = "neroChapters";


        private class MP4Sample
        {
            public Double Duration;
            public UInt32 Size;
            public UInt32 ChunkIndex;                     // 1-based index
            public Int64 ChunkOffset;
            public Int64 RelativeOffset;
        }


        private static Dictionary<String, Byte> frameMapping_mp4; // Mapping between MP4 frame codes and ModifiedAtl frame codes
        private static Dictionary<String, Byte> frameClasses_mp4; // Mapping between MP4 frame codes and frame classes that aren't class 1 (UTF-8 text)

        /* Useless for now
        private int FTotalFrames;
        private byte FMPEGVersionID;
        private byte FProfileID;
        */

        private Byte channels;
        private Byte headerTypeID;
        private Byte bitrateTypeID;
        private Double bitrate;
        private Double duration;
        private Int32 sampleRate;

        private AudioDataManager.SizeInfo sizeInfo;
        private String fileName;

        /* Useless for now
        public byte HeaderTypeID // Header type code
        {
            get { return this.FHeaderTypeID; }
        }
        public String HeaderType // Header type name
        {
            get { return this.getHeaderType(); }
        }
        public byte MPEGVersionID // MPEG version code
        {
            get { return this.FMPEGVersionID; }
        }
        public String MPEGVersion // MPEG version name
        {
            get { return this.getMPEGVersion(); }
        }
        public byte ProfileID // Profile code
        {
            get { return this.FProfileID; }
        }
        public String Profile // Profile name
        {
            get { return this.getProfile(); }
        }
        public byte Channels // Number of channels
        {
            get { return this.FChannels; }
        }
        public byte BitRateTypeID // Bit rate type code
        {
            get { return this.FBitrateTypeID; }
        }
        public String BitRateType // Bit rate type name
        {
            get { return this.getBitRateType(); }
        }
        */


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Boolean IsVBR => (AAC_BITRATE_TYPE_VBR == bitrateTypeID);

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public Double BitRate => bitrate/1000.0;

        public Double Duration => getDuration();

        public Int32 SampleRate => sampleRate;

        public String FileName => fileName;

        public Boolean IsMetaSupported(Int32 metaType)
        {
            return (metaType == MetaDataIOFactory.TAG_ID3V1) || (metaType == MetaDataIOFactory.TAG_ID3V2) || (metaType == MetaDataIOFactory.TAG_APE) || (metaType == MetaDataIOFactory.TAG_NATIVE);
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

        protected override Byte ratingConvention => RC_APE;

        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier according to the ID3v2 version
            if (frameMapping_mp4.ContainsKey(ID)) supportedMetaId = frameMapping_mp4[ID];

            return supportedMetaId;
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            /* useless for now
            FMPEGVersionID = AAC_MPEG_VERSION_UNKNOWN;
            FProfileID = AAC_PROFILE_UNKNOWN;
            FChannels = 0;
            FTotalFrames = 0;
            */

            headerTypeID = AAC_HEADER_TYPE_UNKNOWN;
            bitrateTypeID = AAC_BITRATE_TYPE_UNKNOWN;

            bitrate = 0;
            sampleRate = 0;
            duration = 0;
        }

        public AAC(String fileName)
        {
            this.fileName = fileName;
            resetData();
        }

        static AAC()
        {
            frameMapping_mp4 = new Dictionary<String, Byte>
            {
                { "©nam", TagData.TAG_FIELD_TITLE },
                { "titl", TagData.TAG_FIELD_TITLE },
                { "©alb", TagData.TAG_FIELD_ALBUM },
                { "©art", TagData.TAG_FIELD_ARTIST },
                { "©ART", TagData.TAG_FIELD_ARTIST },
                { "©cmt", TagData.TAG_FIELD_COMMENT },
                { "©day", TagData.TAG_FIELD_RECORDING_YEAR },
                { "©gen", TagData.TAG_FIELD_GENRE },
                { "gnre", TagData.TAG_FIELD_GENRE },
                { "trkn", TagData.TAG_FIELD_TRACK_NUMBER },
                { "disk", TagData.TAG_FIELD_DISC_NUMBER },
                { "rtng", TagData.TAG_FIELD_RATING },
                { "rate", TagData.TAG_FIELD_RATING },
                { "©wrt", TagData.TAG_FIELD_COMPOSER },
                { "desc", TagData.TAG_FIELD_GENERAL_DESCRIPTION },
                { "cprt", TagData.TAG_FIELD_COPYRIGHT },
                { "aART", TagData.TAG_FIELD_ALBUM_ARTIST },
                { "----:com.apple.iTunes:CONDUCTOR", TagData.TAG_FIELD_CONDUCTOR }
            };

            frameClasses_mp4 = new Dictionary<String, Byte>
            {
                { "gnre", 0 },
                { "trkn", 0 },
                { "disk", 0 },
                { "rtng", 21 },
                { "tmpo", 21 },
                { "cpil", 21 },
                { "stik", 21 },
                { "pcst", 21 },
                { "purl", 0 },
                { "egid", 0 },
                { "tvsn", 21 },
                { "tves", 21 },
                { "pgap", 21 }
            };
        }


        // ********************** Private functions & procedures *********************

        private static void addFrameClass(String frameCode, Byte frameClass)
        {
            if (!frameClasses_mp4.ContainsKey(frameCode)) frameClasses_mp4.Add(frameCode, frameClass);
        }

        
        /* Useless for now

        // Get header type name
        private string getHeaderType()
        {
            return AAC_HEADER_TYPE[FHeaderTypeID];
        }

        // Get MPEG version name
        private string getMPEGVersion()
        {
            return AAC_MPEG_VERSION[FMPEGVersionID];
        }

        // Get profile name
        private string getProfile()
        {
            return AAC_PROFILE[FProfileID];
        }

        // Get bit rate type name
        private string getBitRateType()
        {
            return AAC_BITRATE_TYPE[bitrateTypeID];
        }
        */

        // Calculate duration time
        private Double getDuration()
        {
            if (headerTypeID == AAC_HEADER_TYPE_MP4)
            {
                return duration;
            }
            else
            {
                if (0 == bitrate)
                    return 0;
                else
                    return 8.0 * (sizeInfo.FileSize - sizeInfo.TotalTagSize) * 1000 / bitrate;
            }
        }

        // Get header type of the file
        private Byte recognizeHeaderType(BinaryReader Source)
        {
            Byte result;
            Byte[] header;
            String headerStr;

            result = AAC_HEADER_TYPE_UNKNOWN;
            Source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);
            header = Source.ReadBytes(4);
            headerStr = Utils.Latin1Encoding.GetString(header);

            if ("ADIF".Equals(headerStr))
            {
                result = AAC_HEADER_TYPE_ADIF;
            }
            else if ((0xFF == header[0]) && (0xF0 == ((header[0]) & 0xF0)))
            {
                result = AAC_HEADER_TYPE_ADTS;
            }
            else
            {
                headerStr = Utils.Latin1Encoding.GetString(Source.ReadBytes(4)); // bytes 4 to 8
                if ("ftyp".Equals(headerStr))
                {
                    result = AAC_HEADER_TYPE_MP4;
                }
            }
            return result;
        }

        // Read ADIF header data
        private void readADIF(BinaryReader Source)
        {
            Int32 Position;

            Position = (Int32)(sizeInfo.ID3v2Size * 8 + 32);
            if (0 == StreamUtils.ReadBits(Source, Position, 1)) Position += 3;
            else Position += 75;
            if (0 == StreamUtils.ReadBits(Source, Position, 1)) bitrateTypeID = AAC_BITRATE_TYPE_CBR;
            else bitrateTypeID = AAC_BITRATE_TYPE_VBR;

            Position++;

            bitrate = (Int32)StreamUtils.ReadBits(Source, Position, 23);

            if (AAC_BITRATE_TYPE_CBR == bitrateTypeID) Position += 51;
            else Position += 31;

            /* Useless for now
            FMPEGVersionID = AAC_MPEG_VERSION_4;
            FProfileID = (byte)(StreamUtils.ReadBits(Source, Position, 2) + 1);
            */
            Position += 2;

            sampleRate = SAMPLE_RATE[StreamUtils.ReadBits(Source, Position, 4)];
            Position += 4;
            channels += (Byte)StreamUtils.ReadBits(Source, Position, 4);
            Position += 4;
            channels += (Byte)StreamUtils.ReadBits(Source, Position, 4);
            Position += 4;
            channels += (Byte)StreamUtils.ReadBits(Source, Position, 4);
            Position += 4;
            channels += (Byte)StreamUtils.ReadBits(Source, Position, 2);
        }

        // Read ADTS header data
        private void readADTS(BinaryReader Source)
        {
            var frames = 0;
            var totalSize = 0;
            Int32 position;

            do
            {
                frames++;
                position = (Int32)(sizeInfo.ID3v2Size + totalSize) * 8;

                if (StreamUtils.ReadBits(Source, position, 12) != 0xFFF) break;

                position += 12;

                /* Useless for now
                if (0 == StreamUtils.ReadBits(Source, Position, 1))
                    FMPEGVersionID = AAC_MPEG_VERSION_4;
                else
                    FMPEGVersionID = AAC_MPEG_VERSION_2;

                Position += 4;
                FProfileID = (byte)(StreamUtils.ReadBits(Source, Position, 2) + 1);
                Position += 2;
                */
                position += 6; // <-- this line to be deleted when the block above is decommented

                sampleRate = SAMPLE_RATE[StreamUtils.ReadBits(Source, position, 4)];
                position += 5;

                channels = (Byte)StreamUtils.ReadBits(Source, position, 3);

//                if (AAC_MPEG_VERSION_4 == FMPEGVersionID)
//                    Position += 9;
//                else
                    position += 7;

                totalSize += (Int32)StreamUtils.ReadBits(Source, position, 13);
                position += 13;

                if (0x7FF == StreamUtils.ReadBits(Source, position, 11))
                    bitrateTypeID = AAC_BITRATE_TYPE_VBR;
                else
                    bitrateTypeID = AAC_BITRATE_TYPE_CBR;

                if (AAC_BITRATE_TYPE_CBR == bitrateTypeID) break;
            }
            while (Source.BaseStream.Length > sizeInfo.ID3v2Size + totalSize);
            bitrate = (Int32)Math.Round(8 * totalSize / 1024.0 / frames * sampleRate);
        }

        private void readQTChapters(BinaryReader source, IList<MP4Sample> chapterTrackSamples)
        {
            tagExists = true;
            if (null == tagData.Chapters) tagData.Chapters = new List<ChapterInfo>(); else tagData.Chapters.Clear();
            Double cumulatedDuration = 0;

            foreach (var sample in chapterTrackSamples)
            {
                if (sample.ChunkOffset > 0)
                {
                    var chapter = new ChapterInfo();

                    source.BaseStream.Seek(sample.ChunkOffset + sample.RelativeOffset, SeekOrigin.Begin);
                    var strDataSize = StreamUtils.DecodeBEUInt16(source.ReadBytes(2));

                    chapter.Title = Encoding.UTF8.GetString(source.ReadBytes(strDataSize));
                    chapter.StartTime = (UInt32)Math.Round(cumulatedDuration);
                    cumulatedDuration += sample.Duration * 1000;
                    chapter.EndTime = (UInt32)Math.Round(cumulatedDuration);

                    tagData.Chapters.Add(chapter);
                }
            }
        }

        /// <summary>
        /// Read MP4 header data
        /// http://www.jiscdigitalmedia.ac.uk/guide/aac-audio-and-the-mp4-media-format
        /// http://atomicparsley.sourceforge.net/mpeg-4files.html
        /// - Metadata is located in the moov/udta/meta/ilst atom
        /// - Physical information are located in the moov/trak atom (to be confirmed ?)
        /// - Binary physical data are located in the mdat atom
        /// </summary>
        /// <param name="source">Source to read from</param>
        /// <param name="readTagParams">Reading parameters</param>
        private void readMP4(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            Int64 iListSize = 0;
            Int64 iListPosition = 0;
            UInt32 metadataSize = 0;
            Byte dataClass = 0;

            Int64 moovPosition, udtaPosition, trakPosition;
            Int32 globalTimeScale;
            var mediaTimeScale = 1000;

            UInt16 int16Data = 0;
            UInt32 int32Data = 0;

            var strData = "";
            UInt32 atomSize;
            Int64 atomPosition;
            String atomHeader;

            var data32 = new Byte[4];
            var data64 = new Byte[8];

            IDictionary<Int32,IList<Int32>> chapterTrackIndexes = null; // Key is track index (1-based); lists are chapter tracks indexes (1-based)
            IList<MP4Sample> chapterTrackSamples = null;

            
            // TODO PERF - try and cache the whole tree structure to optimize browsing through nodes

            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            // FTYP atom
            source.BaseStream.Read(data32,0,4);
            atomSize = StreamUtils.DecodeBEUInt32(data32);
            source.BaseStream.Seek(atomSize - 4, SeekOrigin.Current);

            // MOOV atom
            atomSize = lookForMP4Atom(source.BaseStream, "moov"); // === Physical data
            if (0 == atomSize)
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "moov atom could not be found; aborting read");
                return;
            }

            moovPosition = source.BaseStream.Position;
            if (readTagParams.PrepareForWriting)
            {
                structureHelper.AddSize(source.BaseStream.Position - 8, atomSize);
                structureHelper.AddSize(source.BaseStream.Position - 8, atomSize, ZONE_MP4_NEROCHAPTERS);
            }

            // === Physical data header
            if (0 == lookForMP4Atom(source.BaseStream, "mvhd"))
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mvhd atom could not be found; aborting read");
                return;
            }
            var version = source.ReadByte();
            source.BaseStream.Seek(3, SeekOrigin.Current); // 3-byte flags
            if (1 == version) source.BaseStream.Seek(16, SeekOrigin.Current); else source.BaseStream.Seek(8, SeekOrigin.Current);

            globalTimeScale = StreamUtils.ReverseInt32(source.ReadInt32());
            Int64 timeLengthPerSec;
            if (1 == version) timeLengthPerSec = StreamUtils.DecodeBEInt64(source.ReadBytes(8)); else timeLengthPerSec = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));
            duration = timeLengthPerSec * 1000.0 / globalTimeScale;

            source.BaseStream.Seek(moovPosition, SeekOrigin.Begin);

            var trakSize = lookForMP4Atom(source.BaseStream, "trak");
            Byte currentTrakIndex = 0;
            var isCurrentTrackFirstChapterTrack = false;
            if (0 == trakSize)
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "trak atom could not be found; aborting read");
                return;
            }

            while (trakSize > 0)
            {
                currentTrakIndex++;
                isCurrentTrackFirstChapterTrack = false;
                trakPosition = source.BaseStream.Position - 8;

                // Look for "chap" atom to detect QT chapters for current track
                if (lookForMP4Atom(source.BaseStream, "tref") > 0 && null == chapterTrackIndexes)
                {
                    var parsePreviousTracks = false;
                    var chapSize = lookForMP4Atom(source.BaseStream, "chap");
                    if (chapSize > 8)
                    {
                        if (null == chapterTrackIndexes) chapterTrackIndexes = new Dictionary<Int32, IList<Int32>>();
                        IList<Int32> thisTrackIndexes = new List<Int32>();
                        for (var i=0; i<(chapSize-8)/4; i++)
                        {
                            thisTrackIndexes.Add(StreamUtils.DecodeBEInt32(source.ReadBytes(4)));
                        }
                        chapterTrackIndexes.Add(currentTrakIndex, thisTrackIndexes);

                        foreach (var i in thisTrackIndexes)
                        {
                            if (i < currentTrakIndex)
                            {
                                parsePreviousTracks = true;
                                break;
                            }
                        }
                    }

                    // If current track has declared a chapter track located at a previous index, come back to read it
                    if (parsePreviousTracks)
                    {
                        source.BaseStream.Seek(moovPosition, SeekOrigin.Begin);

                        trakSize = lookForMP4Atom(source.BaseStream, "trak");
                        currentTrakIndex = 1;
                        trakPosition = source.BaseStream.Position - 8;
                    }
                }

                source.BaseStream.Seek(trakPosition+8, SeekOrigin.Begin);
                if (0 == lookForMP4Atom(source.BaseStream, "mdia"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdia atom could not be found; aborting read");
                    return;
                }

                var mdiaPosition = source.BaseStream.Position;
                if (chapterTrackIndexes != null)
                {
                    if (0 == lookForMP4Atom(source.BaseStream, "mdhd"))
                    {
                        LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdia.mdhd atom could not be found; aborting read");
                        return;
                    }

                    var mdhdVersion = source.ReadByte();
                    source.BaseStream.Seek(3, SeekOrigin.Current); // Flags

                    if (0 == mdhdVersion) source.BaseStream.Seek(8, SeekOrigin.Current); else source.BaseStream.Seek(16, SeekOrigin.Current); // Creation and modification date

                    mediaTimeScale = StreamUtils.DecodeBEInt32(source.ReadBytes(4));

                    source.BaseStream.Seek(mdiaPosition, SeekOrigin.Begin);
                }

                if (0 == lookForMP4Atom(source.BaseStream, "hdlr"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdia.hdlr atom could not be found; aborting read");
                    return;
                }
                source.BaseStream.Seek(4, SeekOrigin.Current); // Version and flags
                source.BaseStream.Seek(4, SeekOrigin.Current); // Quicktime type
                var mediaType = Utils.Latin1Encoding.GetString(source.ReadBytes(4));

                // Check if current track is the 1st chapter track
                // NB : Per convention, we will admit that the 1st track referenced in the 'chap' atom
                // contains the chapter names (as opposed to chapter URLs or chapter images)
                if ("text".Equals(mediaType) && chapterTrackIndexes != null) 
                {
                    foreach (var list in chapterTrackIndexes.Values)
                    {
                        if (currentTrakIndex == list[0])
                        {
                            isCurrentTrackFirstChapterTrack = true;
                            break;
                        }
                    }
                }

                source.BaseStream.Seek(mdiaPosition, SeekOrigin.Begin);
                if (0 == lookForMP4Atom(source.BaseStream, "minf"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdia.minf atom could not be found; aborting read");
                    return;
                }
                if (0 == lookForMP4Atom(source.BaseStream, "stbl"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdia.minf.stbl atom could not be found; aborting read");
                    return;
                }
                var stblPosition = source.BaseStream.Position;

                // Look for sample rate
                if (0 == lookForMP4Atom(source.BaseStream, "stsd"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "stsd atom could not be found; aborting read");
                    return;
                }
                source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags
                var nbDescriptions = StreamUtils.ReverseUInt32(source.ReadUInt32());

                for (var i = 0; i < nbDescriptions; i++)
                {
                    int32Data = StreamUtils.ReverseUInt32(source.ReadUInt32()); // 4-byte description length
                    var descFormat = Utils.Latin1Encoding.GetString(source.ReadBytes(4));

                    if (descFormat.Equals("mp4a") || descFormat.Equals("enca") || descFormat.Equals("samr") || descFormat.Equals("sawb"))
                    {
                        source.BaseStream.Seek(4, SeekOrigin.Current); // 6-byte reserved zone set to zero

                        source.BaseStream.Seek(10, SeekOrigin.Current); // Not useful here

                        channels = (Byte)StreamUtils.ReverseUInt16(source.ReadUInt16()); // Audio channels

                        source.BaseStream.Seek(2, SeekOrigin.Current); // Sample size
                        source.BaseStream.Seek(4, SeekOrigin.Current); // Quicktime stuff

                        sampleRate = StreamUtils.ReverseInt32(source.ReadInt32());
                    }
                    else
                    {
                        source.BaseStream.Seek(int32Data - 4, SeekOrigin.Current);
                    }
                }

                if (isCurrentTrackFirstChapterTrack)
                {
                    source.BaseStream.Seek(stblPosition, SeekOrigin.Begin);
                    if (0 == lookForMP4Atom(source.BaseStream, "stts"))
                    {
                        LogDelegator.GetLogDelegate()(Log.LV_ERROR, "stts atom could not be found; aborting read");
                        return;
                    }
                    source.BaseStream.Seek(4, SeekOrigin.Current); // Version and flags
                    int32Data = StreamUtils.DecodeBEUInt32(source.ReadBytes(4)); // Number of table entries
                    if (int32Data > 0)
                    {
                        UInt32 frameCount, sampleDuration;
                        if (null == chapterTrackSamples) chapterTrackSamples = new List<MP4Sample>(); else chapterTrackSamples.Clear();

                        for (var i = 0; i < int32Data; i++)
                        {
                            source.Read(data32, 0, 4);
                            frameCount = StreamUtils.DecodeBEUInt32(data32);
                            source.Read(data32, 0, 4);
                            sampleDuration = StreamUtils.DecodeBEUInt32(data32);
                            for (var j = 0; j < frameCount; j++)
                            {
                                var sample = new MP4Sample();
                                sample.Duration = sampleDuration * 1.0 / mediaTimeScale;
                                chapterTrackSamples.Add(sample);
                            }
                        }
                    }

                    source.BaseStream.Seek(stblPosition, SeekOrigin.Begin);
                    if (0 == lookForMP4Atom(source.BaseStream, "stsc"))
                    {
                        LogDelegator.GetLogDelegate()(Log.LV_ERROR, "stsc atom could not be found; aborting read");
                        return;
                    }
                    source.BaseStream.Seek(4, SeekOrigin.Current); // Version and flags
                    int32Data = StreamUtils.DecodeBEUInt32(source.ReadBytes(4)); // Number of table entries

                    UInt32 samplesPerChunk;
                    var cumulatedSampleIndex = 0;
                    UInt32 chunkIndex = 0;
                    UInt32 previousChunkIndex = 0;
                    UInt32 previousSamplesPerChunk = 0;
                    var first = true;

                    for (var i = 0; i < int32Data; i++)
                    {
                        source.Read(data32, 0, 4);
                        chunkIndex = StreamUtils.DecodeBEUInt32(data32);
                        source.Read(data32, 0, 4);
                        samplesPerChunk = StreamUtils.DecodeBEUInt32(data32);
                        source.BaseStream.Seek(4, SeekOrigin.Current); // Sample description ID

                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            for (var j = previousChunkIndex; j < chunkIndex; j++)
                            {
                                for (var k = 0; k < previousSamplesPerChunk; k++)
                                {
                                    if (cumulatedSampleIndex < chapterTrackSamples.Count)
                                    {
                                        chapterTrackSamples[cumulatedSampleIndex].ChunkIndex = j;
                                        cumulatedSampleIndex++;
                                    }
                                }
                            }
                        }

                        previousChunkIndex = chunkIndex;
                        previousSamplesPerChunk = samplesPerChunk;
                    }

                    var remainingChunks = (Int32)Math.Ceiling((chapterTrackSamples.Count - cumulatedSampleIndex) * 1.0 / previousSamplesPerChunk);
                    // Fill the rest of the in-memory table with the last pattern
                    for (var j = 0; j < remainingChunks; j++)
                    {
                        for (var k = 0; k < previousSamplesPerChunk; k++)
                        {
                            if (cumulatedSampleIndex < chapterTrackSamples.Count)
                            {
                                chapterTrackSamples[cumulatedSampleIndex].ChunkIndex = chunkIndex;
                                cumulatedSampleIndex++;
                            }
                        }
                        chunkIndex++;
                    }
                }

                source.BaseStream.Seek(stblPosition, SeekOrigin.Begin);
                // VBR detection : if the gap between the smallest and the largest sample size is no more than 1%, we can consider the file is CBR; if not, VBR
                if (0 == lookForMP4Atom(source.BaseStream, "stsz"))
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "stsz atom could not be found; aborting read");
                    return;
                }
                source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags
                var blocByteSizeForAll = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));
                if (0 == blocByteSizeForAll) // If value other than 0, same size everywhere => CBR
                {
                    var nbSizes = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));
                    UInt32 max = 0;
                    var min = UInt32.MaxValue;

                    for (var i = 0; i < nbSizes; i++)
                    {
                        source.Read(data32, 0, 4);
                        int32Data = StreamUtils.DecodeBEUInt32(data32);
                        min = Math.Min(min, int32Data);
                        max = Math.Max(max, int32Data);

                        if (isCurrentTrackFirstChapterTrack) chapterTrackSamples[i].Size = int32Data;
                    }

                    if ((min * 1.01) < max)
                    {
                        bitrateTypeID = AAC_BITRATE_TYPE_VBR;
                    }
                    else
                    {
                        bitrateTypeID = AAC_BITRATE_TYPE_CBR;
                    }
                }
                else
                {
                    bitrateTypeID = AAC_BITRATE_TYPE_CBR;
                    if (isCurrentTrackFirstChapterTrack) for (var i = 0; i < chapterTrackSamples.Count; i++) chapterTrackSamples[i].Size = blocByteSizeForAll;
                }

                // Adjust individual sample offsets using their size for those that are in position > 0 in the same chunk
                if (isCurrentTrackFirstChapterTrack)
                {
                    var currentChunkIndex = UInt32.MaxValue;
                    UInt32 cumulatedChunkOffset = 0;

                    for (var i = 0; i < chapterTrackSamples.Count; i++)
                    {
                        if (chapterTrackSamples[i].ChunkIndex == currentChunkIndex)
                        {
                            chapterTrackSamples[i].RelativeOffset = cumulatedChunkOffset;
                        }
                        else
                        {
                            currentChunkIndex = chapterTrackSamples[i].ChunkIndex;
                            cumulatedChunkOffset = 0;
                        }
                        cumulatedChunkOffset += chapterTrackSamples[i].Size;
                    }
                }


                // "Physical" audio chunks are referenced by position (offset) in  moov.trak.mdia.minf.stbl.stco / co64
                // => They have to be rewritten if the position (offset) of the 'mdat' atom changes
                if (readTagParams.PrepareForWriting || isCurrentTrackFirstChapterTrack)
                {
                    atomPosition = source.BaseStream.Position;
                    Byte nbBytes = 0;
                    UInt32 nbChunkOffsets = 0;
                    Object valueObj;
                    Int64 valueLong;

                    // Chunk offsets
                    if (lookForMP4Atom(source.BaseStream, "stco") > 0)
                    {
                        nbBytes = 4;
                    }
                    else
                    {
                        source.BaseStream.Seek(atomPosition, SeekOrigin.Begin);
                        if (lookForMP4Atom(source.BaseStream, "co64") > 0)
                        {
                            nbBytes = 8;
                        }
                        else
                        {
                            LogDelegator.GetLogDelegate()(Log.LV_ERROR, "neither stco, not co64 atoms could not be found; aborting read");
                            return;
                        }
                    }

                    source.BaseStream.Seek(4, SeekOrigin.Current); // Version and flags
                    nbChunkOffsets = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));

                    for (var i = 0; i < nbChunkOffsets; i++)
                    {
                        if (4 == nbBytes)
                        {
                            source.Read(data32, 0, 4);
                            valueLong = StreamUtils.DecodeBEUInt32(data32);
                            valueObj = (UInt32)valueLong;

                        }
                        else
                        {
                            source.Read(data64, 0, 8);
                            valueLong = StreamUtils.DecodeBEInt64(data64);
                            valueObj = valueLong;
                        }

                        if (isCurrentTrackFirstChapterTrack) // Use the offsets to find position for QT chapter titles
                        {
                            for (var j = 0; j < chapterTrackSamples.Count; j++)
                            {
                                if (chapterTrackSamples[j].ChunkIndex == i + 1)
                                {
                                    chapterTrackSamples[j].ChunkOffset = valueLong;
                                }
                            }
                        }
                        
                        // A size-type header is used here instead of an absolute index-type header because size variation has to be recorded, and not zone position
                        // (those chunks do not even point to metadata zones but to the physical media stream)
                        structureHelper.AddSize(source.BaseStream.Position - nbBytes, valueObj);
                        structureHelper.AddSize(source.BaseStream.Position - nbBytes, valueObj, ZONE_MP4_NEROCHAPTERS); 
                    } // Chunk offsets
                }

                source.BaseStream.Seek(trakPosition + trakSize, SeekOrigin.Begin);
                trakSize = lookForMP4Atom(source.BaseStream, "trak");
            } // Loop through tracks

            // Look for QT chapters
            if (chapterTrackSamples != null && chapterTrackSamples.Count > 0) // QT chapters have been detected while browsing tracks
            {
                readQTChapters(source, chapterTrackSamples);
            }

            source.BaseStream.Seek(moovPosition, SeekOrigin.Begin);
            atomSize = lookForMP4Atom(source.BaseStream, "udta");
            if (0 == atomSize)
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "udta atom could not be found; aborting read");
                return;
            }
            udtaPosition = source.BaseStream.Position;
            if (readTagParams.PrepareForWriting)
            {
                structureHelper.AddSize(source.BaseStream.Position - 8, atomSize);
                structureHelper.AddSize(source.BaseStream.Position - 8, atomSize, ZONE_MP4_NEROCHAPTERS);
            }

            // Look for Nero chapters
            int32Data = lookForMP4Atom(source.BaseStream, "chpl");
            if (int32Data > 0)
            {
                tagExists = true;
                structureHelper.AddZone(source.BaseStream.Position - 8, (Int32)int32Data, new Byte[0], ZONE_MP4_NEROCHAPTERS);

                source.BaseStream.Seek(4, SeekOrigin.Current); // Version and flags
                source.BaseStream.Seek(1, SeekOrigin.Current); // Reserved byte
                source.BaseStream.Read(data32, 0, 4);
                var chapterCount = StreamUtils.DecodeBEUInt32(data32);

                if (chapterCount > 0)
                {
                    if (null == tagData.Chapters) tagData.Chapters = new List<ChapterInfo>(); else tagData.Chapters.Clear();
                    Byte stringSize;
                    ChapterInfo chapter;

                    for (var i = 0; i < chapterCount; i++)
                    {
                        chapter = new ChapterInfo();
                        tagData.Chapters.Add(chapter);

                        source.BaseStream.Read(data64, 0, 8);
                        chapter.StartTime = (UInt32)Math.Round(StreamUtils.DecodeBEInt64(data64) / 10000.0);
                        stringSize = source.ReadByte();
                        chapter.Title = Encoding.UTF8.GetString(source.ReadBytes(stringSize));
                    }
                }
            }
            else
            {
                structureHelper.AddZone(source.BaseStream.Position, 0, new Byte[0], ZONE_MP4_NEROCHAPTERS);
            }

            source.BaseStream.Seek(udtaPosition, SeekOrigin.Begin);
            atomSize = lookForMP4Atom(source.BaseStream, "meta");
            if (0 == atomSize)
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "meta atom could not be found");
                return;
            }
            if (readTagParams.PrepareForWriting) structureHelper.AddSize(source.BaseStream.Position - 8, atomSize);
            source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags

            if (readTagParams.ReadTag)
            {
                atomPosition = source.BaseStream.Position;
                atomSize = lookForMP4Atom(source.BaseStream, "hdlr"); // Metadata handler
                if (0 == atomSize)
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "hdlr atom could not be found; aborting read");
                    return;
                }
                var hdlrPosition = source.BaseStream.Position - 8;
                source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags
                source.BaseStream.Seek(4, SeekOrigin.Current); // Quicktime type
                strData = Utils.Latin1Encoding.GetString(source.ReadBytes(4)); // Meta data type

                if (!strData.Equals("mdir"))
                {
                    var errMsg = "ModifiedAtl does not support ";
                    if (strData.Equals("mp7t")) errMsg += "MPEG-7 XML metadata";
                    else if (strData.Equals("mp7b")) errMsg += "MPEG-7 binary XML metadata";
                    else errMsg = "Unrecognized metadata format";

                    throw new NotSupportedException(errMsg);
                }
                source.BaseStream.Seek(atomSize + hdlrPosition, SeekOrigin.Begin); // Reach the end of the hdlr box

                iListSize = lookForMP4Atom(source.BaseStream, "ilst"); // === Metadata list
                if (0 == iListSize)
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "ilst atom could not be found; aborting read");
                    return;
                }
                structureHelper.AddZone(source.BaseStream.Position - 8, (Int32)iListSize, CORE_SIGNATURE);

                if (8 == Size) // Core minimal size
                {
                    tagExists = false;
                    return;
                }
                else
                {
                    tagExists = true;
                }

                // Browse all metadata
                while (iListPosition < iListSize - 8)
                {
                    atomSize = StreamUtils.ReverseUInt32(source.ReadUInt32());
                    atomHeader = Utils.Latin1Encoding.GetString(source.ReadBytes(4));

                    if ("----".Equals(atomHeader)) // Custom text metadata
                    {
                        metadataSize = lookForMP4Atom(source.BaseStream, "mean"); // "issuer" of the field
                        if (0 == metadataSize)
                        {
                            LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mean atom could not be found; aborting read");
                            return;
                        }
                        source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags
                        atomHeader += ":" + Utils.Latin1Encoding.GetString(source.ReadBytes((Int32)metadataSize - 8 - 4));

                        metadataSize = lookForMP4Atom(source.BaseStream, "name"); // field type
                        if (0 == metadataSize)
                        {
                            LogDelegator.GetLogDelegate()(Log.LV_ERROR, "name atom could not be found; aborting read");
                            return;
                        }
                        source.BaseStream.Seek(4, SeekOrigin.Current); // 4-byte flags
                        atomHeader += ":" + Utils.Latin1Encoding.GetString(source.ReadBytes((Int32)metadataSize - 8 - 4));
                    }

                    // Having a 'data' header here means we're still on the same field, with a 2nd value
                    // (e.g. multiple embedded pictures)
                    if (!"data".Equals(atomHeader))
                    {
                        metadataSize = lookForMP4Atom(source.BaseStream, "data");
                        if (0 == metadataSize)
                        {
                            LogDelegator.GetLogDelegate()(Log.LV_ERROR, "data atom could not be found; aborting read");
                            return;
                        }
                        atomPosition = source.BaseStream.Position - 8;
                    }
                    else
                    {
                        metadataSize = atomSize;
                    }

                    // We're only looking for the last byte of the flag
                    source.BaseStream.Seek(3, SeekOrigin.Current);
                    dataClass = source.ReadByte();

                    // 4-byte NULL space
                    source.BaseStream.Seek(4, SeekOrigin.Current);

                    addFrameClass(atomHeader, dataClass);

                    if (1 == dataClass) // UTF-8 Text
                    {
                        strData = Encoding.UTF8.GetString(source.ReadBytes((Int32)metadataSize - 16));
                        SetMetaField(atomHeader, strData, readTagParams.ReadAllMetaFrames);
                    }
                    else if (21 == dataClass) // uint8
                    {
                        int16Data = source.ReadByte();
                        //                        Source.BaseStream.Seek(atomPosition+metadataSize, SeekOrigin.Begin); // The rest are padding bytes
                        SetMetaField(atomHeader, int16Data.ToString(), readTagParams.ReadAllMetaFrames);
                    }
                    else if (13 == dataClass || 14 == dataClass || (0 == dataClass && "covr".Equals(atomHeader))) // Picture
                    {
                        var picType = PictureInfo.PIC_TYPE.Generic;

                        Int32 picturePosition;
                        addPictureToken(picType);
                        picturePosition = takePicturePosition(picType);

                        if (readTagParams.ReadPictures || readTagParams.PictureStreamHandler != null)
                        {
                            // Peek the next 3 bytes to know the picture type
                            var imgFormat = ImageUtils.GetImageFormatFromPictureHeader(source.ReadBytes(3));
                            if (ImageFormat.Unsupported == imgFormat) imgFormat = ImageFormat.Png;
                            source.BaseStream.Seek(-3, SeekOrigin.Current);

                            var picInfo = new PictureInfo(imgFormat, picType, getImplementedTagType(), dataClass, picturePosition);
                            picInfo.PictureData = new Byte[metadataSize-16];
                            source.BaseStream.Read(picInfo.PictureData, 0, (Int32)metadataSize-16);

                            tagData.Pictures.Add(picInfo);

                            if (readTagParams.PictureStreamHandler != null)
                            {
                                var mem = new MemoryStream(picInfo.PictureData);
                                readTagParams.PictureStreamHandler(ref mem, picInfo.PicType, picInfo.NativeFormat, picInfo.TagType, picInfo.NativePicCode, picInfo.Position);
                                mem.Close();
                            }
                        }
                        else
                        {
                            //                            Source.BaseStream.Seek(metadataSize - 16, SeekOrigin.Current);
                        }
                    }
                    else if (0 == dataClass) // Special cases : gnre, trkn, disk
                    {
                        if ("trkn".Equals(atomHeader) || "disk".Equals(atomHeader))
                        {
                            source.BaseStream.Seek(2, SeekOrigin.Current);
                            int16Data = StreamUtils.ReverseUInt16(source.ReadUInt16());
                            source.BaseStream.Seek(2, SeekOrigin.Current); // Total number of tracks/discs is on the following 2 bytes; ignored for now
                            SetMetaField(atomHeader, int16Data.ToString(), readTagParams.ReadAllMetaFrames);
                        }
                        else if ("gnre".Equals(atomHeader)) // ©gen is a text field and doesn't belong here
                        {
                            int16Data = StreamUtils.ReverseUInt16(source.ReadUInt16());

                            strData = "";
                            if (int16Data < ID3v1.MAX_MUSIC_GENRES) strData = ID3v1.MusicGenre[int16Data - 1];

                            SetMetaField(atomHeader, strData, readTagParams.ReadAllMetaFrames);
                        }
                        else
                        { // Other unhandled cases
                            //                          Source.BaseStream.Seek(metadataSize - 16, SeekOrigin.Current);
                        }
                    }
                    else // Other unhandled cases
                    {
                        //                        Source.BaseStream.Seek(metadataSize - 16, SeekOrigin.Current);
                    }

                    source.BaseStream.Seek(atomPosition + metadataSize, SeekOrigin.Begin);
                    iListPosition += atomSize;
                }
            }

            // Seek audio data segment to calculate mean bitrate 
            // NB : This figure is closer to truth than the "average bitrate" recorded in the esds/m4ds header
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);
            var mdatSize = lookForMP4Atom(source.BaseStream, "mdat"); // === Audio binary data
            if (0 == mdatSize)
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "mdat atom could not be found; aborting read");
                return;
            }
            bitrate = (Int32)Math.Round(mdatSize * 8 / duration * 1000.0, 0);
        }

        /// <summary>
        /// Looks for the atom segment starting with the given key, at the current atom level
        /// Returns with Source positioned right after the atom header, on the 1st byte of data
        /// 
        /// Warning : stream must be positioned at the end of a previous atom before being called
        /// </summary>
        /// <param name="Source">Source to read from</param>
        /// <param name="atomKey">Atom key to look for (e.g. "udta")</param>
        /// <returns>If atom found : raw size of the atom (including the already-read 8-byte header);
        /// If atom not found : 0</returns>
        private UInt32 lookForMP4Atom(Stream Source, String atomKey)
        {
            UInt32 atomSize = 0;
            String atomHeader;
            var first = true;
            var iterations = 0;
            var data = new Byte[4];

            do
            {
                if (!first) Source.Seek(atomSize - 8, SeekOrigin.Current);
                Source.Read(data, 0, 4);
                atomSize = StreamUtils.DecodeBEUInt32(data);
                Source.Read(data, 0, 4);
                atomHeader = Utils.Latin1Encoding.GetString(data);

                if (first) first = false;
                if (++iterations > 100) return 0;
            } while (!atomKey.Equals(atomHeader) && Source.Position + atomSize - 16 < Source.Length);

            if (Source.Position + atomSize - 16 > Source.Length) return 0;

            return atomSize;
        }


        // Read data from file
        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            var result = false;

            ResetData();
            headerTypeID = recognizeHeaderType(source);
            // Read header data
            if (AAC_HEADER_TYPE_ADIF == headerTypeID) readADIF(source);
            else if (AAC_HEADER_TYPE_ADTS == headerTypeID) readADTS(source);
            else if (AAC_HEADER_TYPE_MP4 == headerTypeID) readMP4(source,readTagParams);

            result = true;

            return result;
        }

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            Int64 tagSizePos;
            UInt32 tagSize;
            var result = 0;

            if (FileStructureHelper.DEFAULT_ZONE_NAME.Equals(zone))
            {
                // ============
                // == HEADER ==
                // ============
                // Keep position in mind to calculate final size and come back here to write it
                tagSizePos = w.BaseStream.Position;
                w.Write(CORE_SIGNATURE);

                // ============
                // == FRAMES ==
                // ============
                var dataPos = w.BaseStream.Position;
                result = writeFrames(tag, w);

                // Record final size of tag into "tag size" field of header
                var finalTagPos = w.BaseStream.Position;
                w.BaseStream.Seek(tagSizePos, SeekOrigin.Begin);
                tagSize = Convert.ToUInt32(finalTagPos - tagSizePos);
                w.Write(StreamUtils.ReverseUInt32(tagSize));
                w.BaseStream.Seek(finalTagPos, SeekOrigin.Begin);
            }
            else if (ZONE_MP4_NEROCHAPTERS.Equals(zone)) // Nero chapters
            {
                result = writeNeroChapters(w, Chapters);
            }
                
            return result;
        }

        private Int32 writeFrames(TagData tag, BinaryWriter w)
        {
            var counter = 0;
            Boolean doWritePicture;

            var map = tag.ToMap();

            // Supported textual fields
            foreach (var frameType in map.Keys)
            {
                foreach (var s in frameMapping_mp4.Keys)
                {
                    if (frameType == frameMapping_mp4[s])
                    {
                        if (map[frameType].Length > 0) // No frame with empty value
                        {
                            var value = map[frameType];
                            if (TagData.TAG_FIELD_RATING == frameType) value = TrackUtils.EncodePopularity(value, ratingConvention).ToString();

                            writeTextFrame(w, s, value);
                            counter++;
                        }
                        break;
                    }
                }
            }

            // Other textual fields
            foreach (var fieldInfo in tag.AdditionalFields)
            {
                if ((fieldInfo.TagType.Equals(MetaDataIOFactory.TAG_ANY) || fieldInfo.TagType.Equals(getImplementedTagType())) && !fieldInfo.MarkedForDeletion)
                {
                    writeTextFrame(w, fieldInfo.NativeFieldCode, fieldInfo.Value);
                    counter++;
                }
            }

            // Picture fields
            var firstPic = true;
            foreach (var picInfo in tag.Pictures)
            {
                // Picture has either to be supported, or to come from the right tag standard
                doWritePicture = !picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported);
                if (!doWritePicture) doWritePicture = (getImplementedTagType() == picInfo.TagType);
                // It also has not to be marked for deletion
                doWritePicture = doWritePicture && (!picInfo.MarkedForDeletion);

                if (doWritePicture)
                {
                    writePictureFrame(w, picInfo.PictureData, picInfo.NativeFormat, firstPic);
                    counter++;
                    firstPic = false;
                }
            }

            return counter;
        }

        private void writeTextFrame(BinaryWriter writer, String frameCode, String text)
        {
            Int64 frameSizePos1, frameSizePos2, finalFramePos;
            var frameFlags = 0;

            // == METADATA HEADER ==
            frameSizePos1 = writer.BaseStream.Position;
            writer.Write((Int32)0); // Frame size placeholder to be rewritten in a few lines
            if (frameCode.StartsWith("----")) // Specific metadata
            { 
                var frameCodeComponents = frameCode.Split(':');
                if (3 == frameCodeComponents.Length)
                {
                    writer.Write(Utils.Latin1Encoding.GetBytes("----"));

                    writer.Write(StreamUtils.ReverseInt32(frameCodeComponents[1].Length + 4 + 4 + 4));
                    writer.Write(Utils.Latin1Encoding.GetBytes("mean"));
                    writer.Write(frameFlags);
                    writer.Write(Utils.Latin1Encoding.GetBytes(frameCodeComponents[1]));

                    writer.Write(StreamUtils.ReverseInt32(frameCodeComponents[2].Length + 4 + 4 + 4));
                    writer.Write(Utils.Latin1Encoding.GetBytes("name"));
                    writer.Write(frameFlags);
                    writer.Write(Utils.Latin1Encoding.GetBytes(frameCodeComponents[2]));
                }
            } else
            {
                writer.Write(Utils.Latin1Encoding.GetBytes(frameCode));
            }

            // == METADATA VALUE ==
            frameSizePos2 = writer.BaseStream.Position;
            writer.Write((Int32)0); // Frame size placeholder to be rewritten in a few lines
            writer.Write("data".ToCharArray());

            var frameClass = 1;
            if (frameClasses_mp4.ContainsKey(frameCode)) frameClass = frameClasses_mp4[frameCode];

            writer.Write(StreamUtils.ReverseInt32(frameClass));
            writer.Write(frameFlags);
    
            if (0 == frameClass) // Special cases : gnre, trkn, disk
            {
                UInt16 int16data;
                if (frameCode.Equals("trkn") || frameCode.Equals("disk"))
                {
                    int16data = 0;
                    writer.Write(int16data);
                    int16data = StreamUtils.ReverseUInt16(Convert.ToUInt16( TrackUtils.ExtractTrackNumber(text) ));
                    writer.Write(int16data);
                    int16data = 0;              // Total number of tracks/discs; unsupported for now
                    writer.Write(int16data);
                    if (frameCode.Equals("trkn")) writer.Write(int16data); // trkn field always has two more bytes than disk field....
                }
                else if (frameCode.Equals("gnre"))
                {
                    int16data = StreamUtils.ReverseUInt16(Convert.ToUInt16(text));
                }
            }
            else if (1 == frameClass) // UTF-8 text
            {
                writer.Write(Encoding.UTF8.GetBytes(text));
            }
            else if (21 == frameClass) // uint8
            {
                writer.Write(Convert.ToByte(text));
            }

            // Go back to frame size locations to write their actual size 
            finalFramePos = writer.BaseStream.Position;
            writer.BaseStream.Seek(frameSizePos1, SeekOrigin.Begin);
            writer.Write(StreamUtils.ReverseUInt32(Convert.ToUInt32(finalFramePos -frameSizePos1)));
            writer.BaseStream.Seek(frameSizePos2, SeekOrigin.Begin);
            writer.Write(StreamUtils.ReverseUInt32(Convert.ToUInt32(finalFramePos - frameSizePos2)));
            writer.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);
        }

        private void writePictureFrame(BinaryWriter writer, Byte[] pictureData, ImageFormat picFormat, Boolean firstPicture)
        {
            Int64 frameSizePos1 = 0;
            Int64 frameSizePos2;
            Int64 finalFramePos;

            var frameFlags = 0;

            // == METADATA HEADER ==
            if (firstPicture) // If multiples pictures are embedded, the 'covr' atom is not repeated; the 'data' atom is
            {
                frameSizePos1 = writer.BaseStream.Position;
                writer.Write((Int32)0); // Frame size placeholder to be rewritten in a few lines
                writer.Write(Utils.Latin1Encoding.GetBytes("covr"));
            }

            // == METADATA VALUE ==
            frameSizePos2 = writer.BaseStream.Position;
            writer.Write((Int32)0); // Frame size placeholder to be rewritten in a few lines
            writer.Write("data".ToCharArray());

            Int32 frameClass;
            if (picFormat.Equals(ImageFormat.Jpeg)) frameClass = 13;
            else if (picFormat.Equals(ImageFormat.Png)) frameClass = 14;
            else frameClass = 0;

            writer.Write(StreamUtils.ReverseInt32(frameClass));
            writer.Write(frameFlags);

            writer.Write(pictureData);

            // Go back to frame size locations to write their actual size 
            finalFramePos = writer.BaseStream.Position;
            if (firstPicture)
            {
                writer.BaseStream.Seek(frameSizePos1, SeekOrigin.Begin);
                writer.Write(StreamUtils.ReverseUInt32(Convert.ToUInt32(finalFramePos - frameSizePos1)));
            }
            writer.BaseStream.Seek(frameSizePos2, SeekOrigin.Begin);
            writer.Write(StreamUtils.ReverseUInt32(Convert.ToUInt32(finalFramePos - frameSizePos2)));
            writer.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);
        }

        private Int32 writeNeroChapters(BinaryWriter w, IList<ChapterInfo> chapters)
        {
            var result = 0;
            Int64 frameSizePos, finalFramePos;

            if (chapters != null && chapters.Count > 0)
            {
                result = chapters.Count;

                frameSizePos = w.BaseStream.Position;
                w.Write((Int32)0); // To be rewritten at the end of the method
                w.Write(Utils.Latin1Encoding.GetBytes("chpl"));

                w.Write(new Byte[5] { 1, 0, 0, 0, 0 }); // Version, flags and reserved byte
                w.Write(StreamUtils.EncodeBEInt32(chapters.Count));

                Byte[] strData;
                Byte strDataLength;

                foreach(var chapter in chapters)
                {
                    w.Write(StreamUtils.EncodeBEUInt64(chapter.StartTime*10000));
                    strData = Encoding.UTF8.GetBytes(chapter.Title);
                    strDataLength = (Byte)Math.Min(255, strData.Length);
                    w.Write(strDataLength);
                    w.Write(strData, 0, strDataLength);
                }
                
                // Go back to frame size locations to write their actual size 
                finalFramePos = w.BaseStream.Position;
                w.BaseStream.Seek(frameSizePos, SeekOrigin.Begin);
                w.Write(StreamUtils.EncodeBEInt32((Int32)(finalFramePos - frameSizePos)));
            }

            return result;
        }
    }
}