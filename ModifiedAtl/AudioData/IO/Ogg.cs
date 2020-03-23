using ATL.Logging;
using Commons;
using System;
using System.Collections.Generic;
using System.IO;
using static ATL.AudioData.IO.MetaDataIO;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for OGG files manipulation. Current implementation covers :
    ///   - Vorbis data (extensions : .OGG)
    ///   - Opus data (extensions : .OPUS)
    ///   
    /// Implementation notes
    /// 
    ///   1. CRC's : Current implementation does not test OGG page header CRC's
    ///   2. Page numbers : Current implementation does not test page numbers consistency
    /// 
    /// </summary>
	class Ogg : IAudioDataIO, IMetaDataIO
    {
        // Contents of the file
        private const Int32 CONTENTS_UNSUPPORTED = -1;	    // Unsupported
        private const Int32 CONTENTS_VORBIS = 0;				// Vorbis
        private const Int32 CONTENTS_OPUS = 1;                // Opus

        // Used with ChannelModeID property
        private const Byte VORBIS_CM_MONO = 1;              // Code for mono mode		
        private const Byte VORBIS_CM_STEREO = 2;            // Code for stereo mode
        private const Byte VORBIS_CM_MULTICHANNEL = 6;		// Code for Multichannel Mode

        private const Int32 MAX_PAGE_SIZE = 255 * 255;

        // Channel mode names
        private static readonly String[] VORBIS_MODE = new String[4] { "Unknown", "Mono", "Stereo", "Multichannel" };

        // Ogg page header ID
        private const String OGG_PAGE_ID = "OggS";

        // Vorbis identification packet (frame) ID
        private static readonly String VORBIS_HEADER_ID = (Char)1 + "vorbis";

        // Vorbis tag packet (frame) ID
        private static readonly String VORBIS_TAG_ID = (Char)3 + "vorbis";

        // Vorbis setup packet (frame) ID
        private static readonly String VORBIS_SETUP_ID = (Char)5 + "vorbis";

        // Vorbis parameter frame ID
        private const String OPUS_HEADER_ID = "OpusHead";

        // Opus tag frame ID
        private const String OPUS_TAG_ID = "OpusTags";



        private readonly String filePath;
        private VorbisTag vorbisTag;

        private FileInfo info = new FileInfo();

        private Int32 contents;

        private Byte channelModeID;
        private Int32 sampleRate;
        private UInt16 bitRateNominal;
        private UInt64 samples;

        private AudioDataManager.SizeInfo sizeInfo;



        public Byte ChannelModeID // Channel mode code
            =>
                channelModeID;

        public String ChannelMode // Channel mode name
            =>
                getChannelMode();

        public Int32 SampleRate // Sample rate (hz)
            =>
                sampleRate;

        public UInt16 BitRateNominal // Nominal bit rate
            =>
                bitRateNominal;

        public Boolean Valid // True if file valid
            =>
                isValid();

        public String FileName => filePath;

        public Double BitRate => getBitRate();

        public Double Duration => getDuration();

        public Boolean IsVBR => true;


        // Ogg page header
        private class OggHeader
        {
            public String ID;                                               // Always "OggS"
            public Byte StreamVersion;                           // Stream structure version
            public Byte TypeFlag;                                        // Header type flag
            public UInt64 AbsolutePosition;                      // Absolute granule position
            public Int32 Serial;                                       // Stream serial number
            public Int32 PageNumber;                                   // Page sequence number
            public UInt32 Checksum;                                              // Page CRC32
            public Byte Segments;                                 // Number of page segments
            public Byte[] LacingValues;                     // Lacing values - segment sizes

            public void Reset()
            {
                ID = "";
                StreamVersion = 0;
                TypeFlag = 0;
                AbsolutePosition = 0;
                Serial = 0;
                PageNumber = 0;
                Checksum = 0;
                Segments = 0;
            }

            public void ReadFromStream(BufferedBinaryReader r)
            {
                ID = Utils.Latin1Encoding.GetString(r.ReadBytes(4));
                StreamVersion = r.ReadByte();
                TypeFlag = r.ReadByte();
                AbsolutePosition = r.ReadUInt64();
                Serial = r.ReadInt32();
                PageNumber = r.ReadInt32();
                Checksum = r.ReadUInt32();
                Segments = r.ReadByte();
                LacingValues = r.ReadBytes(Segments);
            }

            public void ReadFromStream(BinaryReader r)
            {
                ID = Utils.Latin1Encoding.GetString(r.ReadBytes(4));
                StreamVersion = r.ReadByte();
                TypeFlag = r.ReadByte();
                AbsolutePosition = r.ReadUInt64();
                Serial = r.ReadInt32();
                PageNumber = r.ReadInt32();
                Checksum = r.ReadUInt32();
                Segments = r.ReadByte();
                LacingValues = r.ReadBytes(Segments);
            }

            public void WriteToStream(BinaryWriter w)
            {
                w.Write(Utils.Latin1Encoding.GetBytes(ID));
                w.Write(StreamVersion);
                w.Write(TypeFlag);
                w.Write(AbsolutePosition);
                w.Write(Serial);
                w.Write(PageNumber);
                w.Write(Checksum);
                w.Write(Segments);
                w.Write(LacingValues);
            }

            public Int32 GetPageLength()
            {
                var length = 0;
                for (var i = 0; i < Segments; i++)
                {
                    length += LacingValues[i];
                }
                return length;
            }

            public Int32 GetHeaderSize()
            {
                return 27 + LacingValues.Length;
            }

            public Boolean IsValid()
            {
                return ((ID != null) && ID.Equals(OGG_PAGE_ID));
            }
        }

        // Vorbis parameter header
        private class VorbisHeader
        {
            public String ID;
            public Byte[] BitstreamVersion = new Byte[4];  // Bitstream version number
            public Byte ChannelMode;                             // Number of channels
            public Int32 SampleRate;                                 // Sample rate (hz)
            public Int32 BitRateMaximal;                         // Bit rate upper limit
            public Int32 BitRateNominal;                             // Nominal bit rate
            public Int32 BitRateMinimal;                         // Bit rate lower limit
            public Byte BlockSize;             // Coded size for small and long blocks
            public Byte StopFlag;                                          // Always 1

            public void Reset()
            {
                ID = "";
                Array.Clear(BitstreamVersion, 0, BitstreamVersion.Length);
                ChannelMode = 0;
                SampleRate = 0;
                BitRateMaximal = 0;
                BitRateNominal = 0;
                BitRateMinimal = 0;
                BlockSize = 0;
                StopFlag = 0;
            }
        }

        // Opus parameter header
        private class OpusHeader
        {
            public String ID;
            public Byte Version;
            public Byte OutputChannelCount;
            public UInt16 PreSkip;
            public UInt32 InputSampleRate;
            public Int16 OutputGain;
            public Byte ChannelMappingFamily;

            public Byte StreamCount;
            public Byte CoupledStreamCount;
            public Byte[] ChannelMapping;

            public void Reset()
            {
                ID = "";
                Version = 0;
                OutputChannelCount = 0;
                PreSkip = 0;
                InputSampleRate = 0;
                OutputGain = 0;
                ChannelMappingFamily = 0;
                StreamCount = 0;
                CoupledStreamCount = 0;
            }
        }

        // File data
        private class FileInfo
        {
            // First, second and third Vorbis packets
            public OggHeader IdentificationHeader = new OggHeader();
            public OggHeader CommentHeader = new OggHeader();
            public OggHeader SetupHeader = new OggHeader();

            // Following two properties are mutually exclusive
            // TODO - handle Theora
            public VorbisHeader VorbisParameters = new VorbisHeader();  // Vorbis parameter header
            public OpusHeader OpusParameters = new OpusHeader();        // Opus parameter header

            // Total number of samples
            public UInt64 Samples;

            // Metrics to ease parsing
            public Int64 CommentHeaderStart;     // Begin offset of comment header
            public Int64 CommentHeaderEnd;       // End offset of comment header
            public Int32 CommentHeaderSpanPages;  // Number of pages the Comment header spans over

            public Int64 SetupHeaderStart;       // Begin offset of setup header
            public Int64 SetupHeaderEnd;         // End offset of setup header
            public Int32 SetupHeaderSpanPages;    // Number of pages the Setup header spans over

            public void Reset()
            {
                IdentificationHeader.Reset();
                CommentHeader.Reset();
                SetupHeader.Reset();

                VorbisParameters.Reset();
                OpusParameters.Reset();

                Samples = 0;

                CommentHeaderStart = 0;
                CommentHeaderEnd = 0;
                CommentHeaderSpanPages = 0;
                SetupHeaderStart = 0;
                SetupHeaderEnd = 0;
                SetupHeaderSpanPages = 0;
            }
        }

        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            // Reset variables
            channelModeID = 0;
            sampleRate = 0;
            bitRateNominal = 0;
            samples = 0;
            contents = -1;

            info.Reset();
        }

        public Ogg(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        #region IMetaDataIO
        public Boolean Exists => ((IMetaDataIO)vorbisTag).Exists;

        public String Title => ((IMetaDataIO)vorbisTag).Title;

        public String Artist => ((IMetaDataIO)vorbisTag).Artist;

        public String Composer => ((IMetaDataIO)vorbisTag).Composer;

        public String Comment => ((IMetaDataIO)vorbisTag).Comment;

        public String Genre => ((IMetaDataIO)vorbisTag).Genre;

        public UInt16 Track => ((IMetaDataIO)vorbisTag).Track;

        public UInt16 Disc => ((IMetaDataIO)vorbisTag).Disc;

        public String Year => ((IMetaDataIO)vorbisTag).Year;

        public String Album => ((IMetaDataIO)vorbisTag).Album;

        [Obsolete("Use popularity")]
        public UInt16 Rating => ((IMetaDataIO)vorbisTag).Rating;

        public Single Popularity => ((IMetaDataIO)vorbisTag).Popularity;

        public String Copyright => ((IMetaDataIO)vorbisTag).Copyright;

        public String OriginalArtist => ((IMetaDataIO)vorbisTag).OriginalArtist;

        public String OriginalAlbum => ((IMetaDataIO)vorbisTag).OriginalAlbum;

        public String GeneralDescription => ((IMetaDataIO)vorbisTag).GeneralDescription;

        public String Publisher => ((IMetaDataIO)vorbisTag).Publisher;

        public String AlbumArtist => ((IMetaDataIO)vorbisTag).AlbumArtist;

        public String Conductor => ((IMetaDataIO)vorbisTag).Conductor;

        public IList<PictureInfo> PictureTokens => ((IMetaDataIO)vorbisTag).PictureTokens;

        public Int32 Size => ((IMetaDataIO)vorbisTag).Size;

        public IDictionary<String, String> AdditionalFields => ((IMetaDataIO)vorbisTag).AdditionalFields;

        public IList<ChapterInfo> Chapters => ((IMetaDataIO)vorbisTag).Chapters;

        public IList<PictureInfo> EmbeddedPictures => ((IMetaDataIO)vorbisTag).EmbeddedPictures;

        #endregion

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE); // According to id3.org (FAQ), ID3 is not compatible with OGG. Hence ModifiedAtl does not allow ID3 tags to be written on OGG files; native is for VorbisTag
        }


        // ---------------------------------------------------------------------------

        // Read total samples of OGG file, which are located on the very last page of the file
        private UInt64 getSamples(BufferedBinaryReader source)
        {
            var header = new OggHeader();

            String headerId;
            Byte typeFlag;
            var lacingValues = new Byte[255];
            Byte nbLacingValues = 0;
            Int64 nextPageOffset = 0;

            // TODO - fine tune seekSize value
            var seekSize = (Int32)Math.Round(MAX_PAGE_SIZE * 0.75);
            if (seekSize > source.Length) seekSize = (Int32)Math.Round(source.Length * 0.5);
            source.Seek(-seekSize, SeekOrigin.End); 
            if (!StreamUtils.FindSequence(source, Utils.Latin1Encoding.GetBytes(OGG_PAGE_ID)))
            {
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "No OGG header found; aborting read operation"); // Throw exception ?
                return 0;
            }
            source.Seek(-4, SeekOrigin.Current);

            // Iterate until last page is encountered
            do
            {
                if (source.Position + nextPageOffset + 27 > source.Length) // End of stream about to be reached => last OGG header did not have the proper type flag
                {
                    break;
                }

                source.Seek(nextPageOffset, SeekOrigin.Current);

                headerId = Utils.Latin1Encoding.GetString(source.ReadBytes(4));

                if (headerId.Equals(OGG_PAGE_ID))
                {
                    source.Seek(1, SeekOrigin.Current);
                    typeFlag = source.ReadByte();
                    source.Seek(20, SeekOrigin.Current);
                    nbLacingValues = source.ReadByte();
                    nextPageOffset = 0;
                    source.Read(lacingValues, 0, nbLacingValues);
                    for (var i = 0; i < nbLacingValues; i++)
                    {
                        nextPageOffset += lacingValues[i];
                    }
                }
                else
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Invalid OGG header found while looking for total samples; aborting read operation"); // Throw exception ?
                    return 0;
                }

            } while (0 == (typeFlag & 0x04)); // 0x04 marks the last page of the logical bitstream


            // Stream is positioned at the end of the last page header; backtracking to read AbsolutePosition field
            source.Seek(-nbLacingValues - 21, SeekOrigin.Current);

            return source.ReadUInt64();
        }

        private void readAllBlocks(BufferedBinaryReader source)
        {
            var header = new OggHeader();

            // Start from the 1st page following headers
            Byte typeFlag;
            Byte nbLacingValues;
            var lacingValues = new Byte[255];
            Int64 nextPageOffset = 0;
            UInt64 lastAbsPosition = 0;

            source.Seek(info.SetupHeaderEnd, SeekOrigin.Begin);

            // Iterate until last page is encountered
            do
            {
                source.Seek(nextPageOffset, SeekOrigin.Current);

                header.ReadFromStream(source);

                nbLacingValues = header.Segments;
                typeFlag = header.TypeFlag;
                nextPageOffset = header.GetPageLength();

                System.Console.WriteLine(header.AbsolutePosition - lastAbsPosition + ";" + nextPageOffset);
                lastAbsPosition = header.AbsolutePosition;

            } while (0 == (typeFlag & 0x04)); // 0x04 marks the last page of the logical bitstream
        }

        private Boolean getInfo(BufferedBinaryReader source, FileInfo info, ReadTagParams readTagParams)
        {
            // Get info from file
            var result = false;
            var isValidHeader = false;

            // Check for ID3v2 (NB : this case should not even exist since OGG has its own native tagging system, and is not deemed compatible with ID3v2 according to the ID3 FAQ)
            source.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            // Read global file header
            info.IdentificationHeader.ReadFromStream(source);

            if (info.IdentificationHeader.IsValid())
            {
                source.Seek(sizeInfo.ID3v2Size + info.IdentificationHeader.Segments + 27, SeekOrigin.Begin); // 27 being the size from 'ID' to 'Segments'

                // Read Vorbis or Opus stream info
                var position = source.Position;

                var headerStart = Utils.Latin1Encoding.GetString(source.ReadBytes(3));
                source.Seek(position, SeekOrigin.Begin);
                if (VORBIS_HEADER_ID.StartsWith(headerStart))
                {
                    contents = CONTENTS_VORBIS;
                    info.VorbisParameters.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(7));
                    isValidHeader = VORBIS_HEADER_ID.Equals(info.VorbisParameters.ID);

                    info.VorbisParameters.BitstreamVersion = source.ReadBytes(4);
                    info.VorbisParameters.ChannelMode = source.ReadByte();
                    info.VorbisParameters.SampleRate = source.ReadInt32();
                    info.VorbisParameters.BitRateMaximal = source.ReadInt32();
                    info.VorbisParameters.BitRateNominal = source.ReadInt32();
                    info.VorbisParameters.BitRateMinimal = source.ReadInt32();
                    info.VorbisParameters.BlockSize = source.ReadByte();
                    info.VorbisParameters.StopFlag = source.ReadByte();
                }
                else if (OPUS_HEADER_ID.StartsWith(headerStart))
                {
                    contents = CONTENTS_OPUS;
                    info.OpusParameters.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(8));
                    isValidHeader = OPUS_HEADER_ID.Equals(info.OpusParameters.ID);

                    info.OpusParameters.Version = source.ReadByte();
                    info.OpusParameters.OutputChannelCount = source.ReadByte();
                    info.OpusParameters.PreSkip = source.ReadUInt16();
                    //info.OpusParameters.InputSampleRate = source.ReadUInt32();
                    info.OpusParameters.InputSampleRate = 48000; // Actual sample rate is hardware-dependent. Let's assume for now that the hardware ModifiedAtl runs on supports 48KHz
                    source.Seek(4, SeekOrigin.Current);
                    info.OpusParameters.OutputGain = source.ReadInt16();

                    info.OpusParameters.ChannelMappingFamily = source.ReadByte();

                    if (info.OpusParameters.ChannelMappingFamily > 0)
                    {
                        info.OpusParameters.StreamCount = source.ReadByte();
                        info.OpusParameters.CoupledStreamCount = source.ReadByte();

                        info.OpusParameters.ChannelMapping = new Byte[info.OpusParameters.OutputChannelCount];
                        for (var i = 0; i < info.OpusParameters.OutputChannelCount; i++)
                        {
                            info.OpusParameters.ChannelMapping[i] = source.ReadByte();
                        }
                    }
                }

                if (isValidHeader)
                {
                    info.CommentHeaderStart = source.Position;
                    IList<Int64> pagePos = new List<Int64>();

                    // Reads all related Vorbis pages that describe Comment and Setup headers
                    // and concatenate their content into a single, continuous data stream
                    var loop = true;
                    var first = true;
                    using (var s = new MemoryStream())
                    {
                        // Reconstruct the whole Comment header from OGG pages to a MemoryStream
                        while (loop)
                        {
                            info.SetupHeaderEnd = source.Position; // When the loop stops, cursor is starting to read a brand new page located after Comment _and_ Setup headers
                            info.CommentHeader.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
                            info.CommentHeader.StreamVersion = source.ReadByte();
                            info.CommentHeader.TypeFlag = source.ReadByte();
                            // 0 marks a new page
                            if (0 == info.CommentHeader.TypeFlag)
                            {
                                loop = first;
                            }
                            if (loop)
                            {
                                info.CommentHeader.AbsolutePosition = source.ReadUInt64();
                                info.CommentHeader.Serial = source.ReadInt32();
                                info.CommentHeader.PageNumber = source.ReadInt32();
                                info.CommentHeader.Checksum = source.ReadUInt32();
                                info.CommentHeader.Segments = source.ReadByte();
                                info.CommentHeader.LacingValues = source.ReadBytes(info.CommentHeader.Segments);
                                s.Write(source.ReadBytes(info.CommentHeader.GetPageLength()), 0, info.CommentHeader.GetPageLength());
                                pagePos.Add(info.SetupHeaderEnd);
                            }
                            first = false;
                        }

                        if (readTagParams.PrepareForWriting) // Metrics to prepare writing
                        {
                            if (pagePos.Count > 1) source.Position = pagePos[pagePos.Count - 2]; else source.Position = pagePos[0];

                            // Determine the boundaries of 3rd header (Setup header) by searching from last-but-one page
                            if (StreamUtils.FindSequence(source, Utils.Latin1Encoding.GetBytes(VORBIS_SETUP_ID)))
                            {
                                info.SetupHeaderStart = source.Position - VORBIS_SETUP_ID.Length;
                                info.CommentHeaderEnd = info.SetupHeaderStart;

                                if (pagePos.Count > 1)
                                {
                                    var firstSetupPage = -1;
                                    for (var i = 1; i < pagePos.Count; i++)
                                    {
                                        if (info.CommentHeaderEnd < pagePos[i])
                                        {
                                            info.CommentHeaderSpanPages = i - 1;
                                            firstSetupPage = i - 1;
                                        }
                                        if (info.SetupHeaderEnd < pagePos[i]) info.SetupHeaderSpanPages = i - firstSetupPage;
                                    }
                                    /// Not found yet => comment header takes up all pages, and setup header is on the end of the last page
                                    if (-1 == firstSetupPage)
                                    {
                                        info.CommentHeaderSpanPages = pagePos.Count;
                                        info.SetupHeaderSpanPages = 1;
                                    }

                                }
                                else
                                {
                                    info.CommentHeaderSpanPages = 1;
                                    info.SetupHeaderSpanPages = 1;
                                }
                            }
                        }

                        // Get total number of samples
                        info.Samples = getSamples(source);

                        // Read metadata from the reconstructed Comment header inside the memoryStream
                        if (readTagParams.ReadTag)
                        {
                            var msr = new BinaryReader(s);
                            s.Seek(0, SeekOrigin.Begin);

                            String tagId;
                            var isValidTagHeader = false;
                            if (contents.Equals(CONTENTS_VORBIS))
                            {
                                tagId = Utils.Latin1Encoding.GetString(msr.ReadBytes(7));
                                isValidTagHeader = (VORBIS_TAG_ID.Equals(tagId));
                            }
                            else if (contents.Equals(CONTENTS_OPUS))
                            {
                                tagId = Utils.Latin1Encoding.GetString(msr.ReadBytes(8));
                                isValidTagHeader = (OPUS_TAG_ID.Equals(tagId));
                            }

                            if (isValidTagHeader) vorbisTag.Read(msr, readTagParams);
                        }
                    } // using MemoryStream

                    result = true;
                }
            }

            return result;
        }

        private String getChannelMode()
        {
            String result;
            // Get channel mode name
            if (channelModeID > 2) result = VORBIS_MODE[3];
            else
                result = VORBIS_MODE[channelModeID];

            return VORBIS_MODE[channelModeID];
        }

        // Calculate duration time
        private Double getDuration()
        {
            Double result;

            if (samples > 0)
                if (sampleRate > 0)
                    result = ((Double)samples * 1000.0 / sampleRate);
                else
                    result = 0;
            else
                if ((bitRateNominal > 0) && (channelModeID > 0))
                result = (1000.0 * (Double)sizeInfo.FileSize - sizeInfo.ID3v2Size) /
                    (Double)bitRateNominal / channelModeID / 125.0 * 2;
            else
                result = 0;

            return result;
        }

        private Double getBitRate()
        {
            // Calculate average bit rate
            Double result = 0;

            if (getDuration() > 0) result = (sizeInfo.FileSize - sizeInfo.TotalTagSize) * 8.0 / getDuration();

            return result;
        }

        private Boolean isValid()
        {
            // Check for file correctness
            return ((((VORBIS_CM_MONO <= channelModeID) && (channelModeID <= VORBIS_CM_STEREO)) || (VORBIS_CM_MULTICHANNEL == channelModeID)) &&
                (sampleRate > 0) && (getDuration() > 0.1) && (getBitRate() > 0));
        }

        // ---------------------------------------------------------------------------

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return Read(source, readTagParams);
        }

        public Boolean Read(BinaryReader source, ReadTagParams readTagParams)
        {
            var result = false;

            var reader = new BufferedBinaryReader(source.BaseStream);

            if (readTagParams.ReadTag && null == vorbisTag) vorbisTag = new VorbisTag(true, true, true);
            info.Reset();

            if (getInfo(reader, info, readTagParams))
            {
                if (contents.Equals(CONTENTS_VORBIS))
                {
                    channelModeID = info.VorbisParameters.ChannelMode;
                    sampleRate = info.VorbisParameters.SampleRate;
                    bitRateNominal = (UInt16)(info.VorbisParameters.BitRateNominal / 1000); // Integer division
                }
                else if (contents.Equals(CONTENTS_OPUS))
                {
                    channelModeID = info.OpusParameters.OutputChannelCount;
                    sampleRate = (Int32)info.OpusParameters.InputSampleRate;
                    // No nominal bitrate for OPUS
                }

                samples = info.Samples;

                result = true;
            }
            return result;
        }

        // Specific implementation for OGG container (multiple pages with limited size)

        // TODO DOC
        // Simplified implementation of MetaDataIO tweaked for OGG-Vorbis specifics, i.e.
        //  - tag spans over multiple pages, each having its own header
        //  - last page may include whole or part of 3rd Vorbis header (setup header)

        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            var result = true;
            var writtenPages = 0;
            Int64 nextPageOffset = 0;

            // Read all the fields in the existing tag (including unsupported fields)
            var readTagParams = new ReadTagParams(true, true);
            readTagParams.PrepareForWriting = true;
            Read(r, readTagParams);

            // Get "unpaged" virtual stream to be written, containing the vorbis tag (=comment header)
            using (var stream = new MemoryStream((Int32)(info.SetupHeaderEnd - info.CommentHeaderStart)))
            {
                stream.Write(Utils.Latin1Encoding.GetBytes(VORBIS_TAG_ID), 0, VORBIS_TAG_ID.Length);
                vorbisTag.Write(stream, tag);

                var newTagSize = stream.Position;
                var setupHeaderSize = (Int32)(info.SetupHeaderEnd - info.SetupHeaderStart);

                // Append the setup header in the "unpaged" virtual stream
                r.BaseStream.Seek(info.SetupHeaderStart, SeekOrigin.Begin);
                if (1 == info.SetupHeaderSpanPages)
                {
                    StreamUtils.CopyStream(r.BaseStream, stream, setupHeaderSize);
                }
                else
                {
                    // TODO - handle case where initial setup header spans across two pages
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "ModifiedAtl does not yet handle the case where Vorbis setup header spans across two OGG pages");
                    return false;
                }


                // Construct the entire segments table
                var commentsHeader_nbSegments = (Int32)Math.Ceiling(1.0 * newTagSize / 255);
                var commentsHeader_remainingBytesInLastSegment = (Byte)(newTagSize % 255);

                var setupHeader_nbSegments = (Int32)Math.Ceiling(1.0 * setupHeaderSize / 255);
                var setupHeader_remainingBytesInLastSegment = (Byte)(setupHeaderSize % 255);

                var entireSegmentsTable = new Byte[commentsHeader_nbSegments + setupHeader_nbSegments];
                for (var i = 0; i < commentsHeader_nbSegments - 1; i++)
                {
                    entireSegmentsTable[i] = 255;
                }
                entireSegmentsTable[commentsHeader_nbSegments - 1] = commentsHeader_remainingBytesInLastSegment;
                for (var i = commentsHeader_nbSegments; i < commentsHeader_nbSegments + setupHeader_nbSegments - 1; i++)
                {
                    entireSegmentsTable[i] = 255;
                }
                entireSegmentsTable[commentsHeader_nbSegments + setupHeader_nbSegments - 1] = setupHeader_remainingBytesInLastSegment;

                var nbPageHeaders = (Int32)Math.Ceiling((commentsHeader_nbSegments + setupHeader_nbSegments) / 255.0);
                var totalPageHeadersSize = (nbPageHeaders * 27) + setupHeader_nbSegments + commentsHeader_nbSegments;


                // Resize the whole virtual stream once and for all to avoid multiple reallocations while repaging
                stream.SetLength(stream.Position + totalPageHeadersSize);


                /// Repage comments header & setup header within the virtual stream
                stream.Seek(0, SeekOrigin.Begin);

                var header = new OggHeader()
                {
                    ID = OGG_PAGE_ID,
                    StreamVersion = info.CommentHeader.StreamVersion,
                    TypeFlag = 0,
                    AbsolutePosition = UInt64.MaxValue,
                    Serial = info.CommentHeader.Serial,
                    PageNumber = 1,
                    Checksum = 0
                };

                var segmentsLeftToPage = commentsHeader_nbSegments + setupHeader_nbSegments;
                var bytesLeftToPage = (Int32)newTagSize + setupHeaderSize;
                var pagedSegments = 0;
                var pagedBytes = 0;
                Int64 position;

                var virtualW = new BinaryWriter(stream);
                IList<KeyValuePair<Int64, Int32>> pageHeaderOffsets = new List<KeyValuePair<Int64, Int32>>();

                // Repaging
                while (segmentsLeftToPage > 0)
                {
                    header.Segments = (Byte)Math.Min(255, segmentsLeftToPage);
                    header.LacingValues = new Byte[header.Segments];
                    if (segmentsLeftToPage == header.Segments) header.AbsolutePosition = 0; // Last header page has its absolutePosition = 0

                    Array.Copy(entireSegmentsTable, pagedSegments, header.LacingValues, 0, header.Segments);

                    position = stream.Position;
                    // Push current data to write header
                    StreamUtils.CopySameStream(stream, stream.Position, stream.Position + header.GetHeaderSize(), bytesLeftToPage);
                    stream.Seek(position, SeekOrigin.Begin);

                    pageHeaderOffsets.Add(new KeyValuePair<Int64, Int32>(position, header.GetPageLength() + header.GetHeaderSize()));

                    header.WriteToStream(virtualW);
                    stream.Seek(header.GetPageLength(), SeekOrigin.Current);

                    pagedSegments += header.Segments;
                    segmentsLeftToPage -= header.Segments;
                    pagedBytes += header.GetPageLength();
                    bytesLeftToPage -= header.GetPageLength();

                    header.PageNumber++;
                    if (0 == header.TypeFlag) header.TypeFlag = 1;
                }
                writtenPages = header.PageNumber - 1;


                // Generate CRC32 of created pages
                UInt32 crc;
                Byte[] data;
                foreach (var kv in pageHeaderOffsets)
                {
                    crc = 0;
                    stream.Seek(kv.Key, SeekOrigin.Begin);
                    data = new Byte[kv.Value];
                    stream.Read(data, 0, kv.Value);
                    crc = OggCRC32.CalculateCRC(crc, data, (UInt32)kv.Value);
                    stream.Seek(kv.Key + 22, SeekOrigin.Begin); // Position of CRC within OGG header
                    virtualW.Write(crc);
                }


                /// Insert the virtual paged stream into the actual file
                var oldHeadersSize = info.SetupHeaderEnd - info.CommentHeaderStart;
                var newHeadersSize = stream.Length;

                if (newHeadersSize > oldHeadersSize) // Need to build a larger file
                {
                    StreamUtils.LengthenStream(w.BaseStream, info.CommentHeaderEnd, (UInt32)(newHeadersSize - oldHeadersSize));
                }
                else if (newHeadersSize < oldHeadersSize) // Need to reduce file size
                {
                    StreamUtils.ShortenStream(w.BaseStream, info.CommentHeaderEnd, (UInt32)(oldHeadersSize - newHeadersSize));
                }

                // Rewrite Comment and Setup headers
                w.BaseStream.Seek(info.CommentHeaderStart, SeekOrigin.Begin);
                stream.Seek(0, SeekOrigin.Begin);

                StreamUtils.CopyStream(stream, w.BaseStream);

                nextPageOffset = info.CommentHeaderStart + stream.Length;
            }

            // If the number of written pages is different than the number of previous existing pages,
            // all the next pages of the file need to be renumbered, and their CRC accordingly recalculated
            if (writtenPages != info.CommentHeaderSpanPages + info.SetupHeaderSpanPages - 1)
            {
                var header = new OggHeader();
                Byte[] data;
                UInt32 crc;

                do
                {
                    w.BaseStream.Seek(nextPageOffset, SeekOrigin.Begin);
                    header.ReadFromStream(r);

                    if (header.IsValid())
                    {
                        // Rewrite page number
                        writtenPages++;
                        w.BaseStream.Seek(nextPageOffset + 18, SeekOrigin.Begin);
                        w.Write(writtenPages);

                        // Rewrite CRC
                        w.BaseStream.Seek(nextPageOffset, SeekOrigin.Begin);
                        data = new Byte[header.GetHeaderSize() + header.GetPageLength()];
                        r.Read(data, 0, data.Length);

                        // Checksum has to include its own location, as if it were 0
                        data[22] = 0;
                        data[23] = 0;
                        data[24] = 0;
                        data[25] = 0;

                        crc = OggCRC32.CalculateCRC(0, data, (UInt32)data.Length);
                        r.BaseStream.Seek(nextPageOffset + 22, SeekOrigin.Begin); // Position of CRC within OGG header
                        w.Write(crc);

                        // To the next header
                        nextPageOffset += data.Length;
                    }
                    else
                    {
                        LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Invalid OGG header found; aborting writing operation"); // Throw exception ?
                        return false;
                    }

                } while (0 == (header.TypeFlag & 0x04));  // 0x04 marks the last page of the logical bitstream
            }

            return result;
        }

        public Boolean Remove(BinaryWriter w)
        {
            var tag = vorbisTag.GetDeletionTagData();

            var r = new BinaryReader(w.BaseStream);
            return Write(r, w, tag);
        }

        public void SetEmbedder(IMetaDataEmbedder embedder)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            vorbisTag.Clear();
        }
    }
}