using ATL.Logging;
using Commons;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static ATL.AudioData.AudioDataManager;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Audio Interchange File Format files manipulation (extension : .AIF, .AIFF, .AIFC)
    /// 
    /// Implementation notes
    /// 
    ///  1/ Annotations being somehow deprecated (Cf. specs "Use of this chunk is discouraged within FORM AIFC. The more refined Comments Chunk should be used instead"),
    ///  any data read from an ANNO chunk will be written as a COMT chunk when updating the file (ANNO chunks will be deleted in the process).
    /// 
    ///  2/ Embedded MIDI detection, parsing and writing is not supported
    ///  
    ///  3/ Instrument detection, parsing and writing is not supported
    /// </summary>
	class AIFF : MetaDataIO, IAudioDataIO, IMetaDataEmbedder
    {
        public const String AIFF_CONTAINER_ID = "FORM";

        private const String FORMTYPE_AIFF = "AIFF";
        private const String FORMTYPE_AIFC = "AIFC";

        private const String COMPRESSION_NONE       = "NONE";
        private const String COMPRESSION_NONE_LE    = "sowt";

        private const String CHUNKTYPE_COMMON       = "COMM";
        private const String CHUNKTYPE_SOUND        = "SSND";

        private const String CHUNKTYPE_MARKER       = "MARK";
        private const String CHUNKTYPE_INSTRUMENT   = "INST";
        private const String CHUNKTYPE_COMMENTS     = "COMT";
        private const String CHUNKTYPE_NAME         = "NAME";
        private const String CHUNKTYPE_AUTHOR       = "AUTH";
        private const String CHUNKTYPE_COPYRIGHT    = "(c) ";
        private const String CHUNKTYPE_ANNOTATION   = "ANNO"; // Use in discouraged by specs in favour of COMT
        private const String CHUNKTYPE_ID3TAG       = "ID3 ";

        // AIFx timestamp are defined as "the number of seconds since January 1, 1904"
        private static DateTime timestampBase = new DateTime(1904, 1, 1);

        public class CommentData
        {
            public UInt32 Timestamp;
            public Int16 MarkerId;
        }

        private struct ChunkHeader
        {
            public String ID;
            public Int32 Size;
        }

        // Private declarations 
        private UInt32 channels;
		private UInt32 bits;
        private UInt32 sampleSize;
        private UInt32 numSampleFrames;

        private String format;
        private String compression;
        private Byte versionID;

        private Int32 sampleRate;
        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private readonly String filePath;

        private Int64 id3v2Offset;
        private FileStructureHelper id3v2StructureHelper = new FileStructureHelper(false);

        private static IDictionary<String, Byte> frameMapping; // Mapping between AIFx frame codes and ModifiedAtl frame codes


        public Byte VersionID // Version code
            =>
                versionID;

        public UInt32 Channels => channels;

        public UInt32 Bits => bits;

        public Double CompressionRatio => getCompressionRatio();


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Boolean IsVBR => false;

        public Int32 CodecFamily => (compression.Equals(COMPRESSION_NONE)|| compression.Equals(COMPRESSION_NONE_LE)) ?AudioDataIoFactory.CfLossless: AudioDataIoFactory.CfLossy;

        public String FileName => filePath;

        public Int32 SampleRate => sampleRate;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE) || (metaDataType == MetaDataIOFactory.TAG_ID3V2);
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

        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier according to the ID3v2 version
            if (frameMapping.ContainsKey(ID)) supportedMetaId = frameMapping[ID];

            return supportedMetaId;
        }


        // IMetaDataEmbedder
        public Int64 HasEmbeddedID3v2 => id3v2Offset;

        public UInt32 ID3v2EmbeddingHeaderSize => 8;

        public FileStructureHelper.Zone Id3v2Zone => id3v2StructureHelper.GetZone(CHUNKTYPE_ID3TAG);


        // ---------- CONSTRUCTORS & INITIALIZERS

        static AIFF()
        {
            frameMapping = new Dictionary<String, Byte>
            {
                { CHUNKTYPE_NAME, TagData.TAG_FIELD_TITLE },
                { CHUNKTYPE_AUTHOR, TagData.TAG_FIELD_ARTIST },
                { CHUNKTYPE_COPYRIGHT, TagData.TAG_FIELD_COPYRIGHT }
            };
        }

        private void resetData()
		{
            duration = 0;
            bitrate = 0;
            isValid = false;
            id3v2StructureHelper.Clear();

            channels = 0;
			bits = 0;
			sampleRate = 0;

            versionID = 0;

            id3v2Offset = -1;

            ResetData();
        }

        public AIFF(String filePath)
        {
            this.filePath = filePath;

            resetData();
        }

        
        // ---------- SUPPORT METHODS

        private Double getCompressionRatio()
        {
            // Get compression ratio 
            if (isValid)
                return (Double)sizeInfo.FileSize / ((duration / 1000.0 * sampleRate) * (channels * bits / 8.0) + 44) * 100;
            else
                return 0;
        }

        /// <summary>
        /// Reads ID and size of a local chunk and returns them in a dedicated structure _without_ reading nor skipping the data
        /// </summary>
        /// <param name="source">Source where to read header information</param>
        /// <returns>Local chunk header information</returns>
        private ChunkHeader seekNextChunkHeader(BinaryReader source, Int64 limit)
        {
            var header = new ChunkHeader();
            var aByte = new Byte[1];

            source.BaseStream.Read(aByte, 0, 1);
            // In case previous field size is not correctly documented, tries to advance to find a suitable first character for an ID
            while ( !((aByte[0] == 40) || ((64 < aByte[0]) && (aByte[0] < 91)) ) && source.BaseStream.Position < limit) 
            {
                source.BaseStream.Read(aByte, 0, 1);
            }

            if (source.BaseStream.Position < limit)
            {
                source.BaseStream.Seek(-1, SeekOrigin.Current);
                
                // Chunk ID
                header.ID = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
                // Chunk size
                header.Size = StreamUtils.DecodeBEInt32(source.ReadBytes(4));
            }
            else
            {
                header.ID = "";
            }

            return header;
        }

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            var result = false;
            Int64 position;

            resetData();
            source.BaseStream.Seek(0, SeekOrigin.Begin);

            if (AIFF_CONTAINER_ID.Equals(Utils.Latin1Encoding.GetString(source.ReadBytes(4))))
            {
                // Container chunk size
                var containerChunkPos = source.BaseStream.Position;
                var containerChunkSize = StreamUtils.DecodeBEInt32(source.ReadBytes(4));

                if (containerChunkPos + containerChunkSize + 4 != source.BaseStream.Length)
                {
                    LogDelegator.GetLogDelegate()(Log.LV_WARNING, "Header size is incoherent with file size");
                }

                // Form type
                format = Utils.Latin1Encoding.GetString(source.ReadBytes(4));

                if (format.Equals(FORMTYPE_AIFF) || format.Equals(FORMTYPE_AIFC))
                {
                    isValid = true;

                    var commentStr = new StringBuilder("");
                    Int64 soundChunkPosition = 0;
                    Int64 soundChunkSize = 0; // Header size included
                    var nameFound = false;
                    var authorFound = false;
                    var copyrightFound = false;
                    var commentsFound = false;
                    var limit = Math.Min(containerChunkPos + containerChunkSize + 4, source.BaseStream.Length);

                    var annotationIndex = 0;
                    var commentIndex = 0;

                    while (source.BaseStream.Position < limit)
                    {
                        var header = seekNextChunkHeader(source, limit);

                        position = source.BaseStream.Position;

                        if (header.ID.Equals(CHUNKTYPE_COMMON))
                        {
                            channels = (UInt32)StreamUtils.DecodeBEInt16(source.ReadBytes(2));
                            numSampleFrames = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));
                            sampleSize = (UInt32)StreamUtils.DecodeBEInt16(source.ReadBytes(2)); // This sample size is for uncompressed data only
                            var byteArray = source.ReadBytes(10);
                            Array.Reverse(byteArray);
                            var aSampleRate = StreamUtils.ExtendedToDouble(byteArray);

                            if (format.Equals(FORMTYPE_AIFC))
                            {
                                compression = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
                            }
                            else // AIFF <=> no compression
                            {
                                compression = COMPRESSION_NONE;
                            }

                            if (aSampleRate > 0)
                            {
                                sampleRate = (Int32)Math.Round(aSampleRate);
                                duration = (Double)numSampleFrames * 1000.0 / sampleRate;

                                if (!compression.Equals(COMPRESSION_NONE)) // Sample size is specific to selected compression method
                                {
                                    if (compression.ToLower().Equals("fl32")) sampleSize = 32;
                                    else if (compression.ToLower().Equals("fl64")) sampleSize = 64;
                                    else if (compression.ToLower().Equals("alaw")) sampleSize = 8;
                                    else if (compression.ToLower().Equals("ulaw")) sampleSize = 8;
                                }
                                if (duration > 0) bitrate = sampleSize * numSampleFrames * channels / duration;
                            }
                        }
                        else if (header.ID.Equals(CHUNKTYPE_SOUND))
                        {
                            soundChunkPosition = source.BaseStream.Position - 8;
                            soundChunkSize = header.Size + 8;
                        }
                        else if (header.ID.Equals(CHUNKTYPE_NAME) || header.ID.Equals(CHUNKTYPE_AUTHOR) || header.ID.Equals(CHUNKTYPE_COPYRIGHT))
                        {
                            structureHelper.AddZone(source.BaseStream.Position - 8, header.Size + 8, header.ID);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, header.ID);

                            tagExists = true;
                            if (header.ID.Equals(CHUNKTYPE_NAME)) nameFound = true;
                            if (header.ID.Equals(CHUNKTYPE_AUTHOR)) authorFound = true;
                            if (header.ID.Equals(CHUNKTYPE_COPYRIGHT)) copyrightFound = true;

                            SetMetaField(header.ID, Utils.Latin1Encoding.GetString(source.ReadBytes(header.Size)), readTagParams.ReadAllMetaFrames);
                        }
                        else if (header.ID.Equals(CHUNKTYPE_ANNOTATION))
                        {
                            annotationIndex++;
                            structureHelper.AddZone(source.BaseStream.Position - 8, header.Size + 8, header.ID + annotationIndex);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, header.ID + annotationIndex);

                            if (commentStr.Length > 0) commentStr.Append(Settings.InternalValueSeparator);
                            commentStr.Append(Utils.Latin1Encoding.GetString(source.ReadBytes(header.Size)));
                            tagExists = true;
                        }
                        else if (header.ID.Equals(CHUNKTYPE_COMMENTS))
                        {
                            commentIndex++;
                            structureHelper.AddZone(source.BaseStream.Position - 8, header.Size + 8, header.ID+commentIndex);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, header.ID+commentIndex);

                            tagExists = true;
                            commentsFound = true;

                            var numComs = StreamUtils.DecodeBEUInt16(source.ReadBytes(2));

                            for (var i = 0; i < numComs; i++)
                            {
                                var cmtData = new CommentData();
                                cmtData.Timestamp = StreamUtils.DecodeBEUInt32(source.ReadBytes(4));
                                cmtData.MarkerId = StreamUtils.DecodeBEInt16(source.ReadBytes(2));

                                // Comments length
                                var comLength = StreamUtils.DecodeBEUInt16(source.ReadBytes(2));
                                var comment = new MetaFieldInfo(getImplementedTagType(), header.ID + commentIndex);
                                comment.Value = Utils.Latin1Encoding.GetString(source.ReadBytes(comLength));
                                comment.SpecificData = cmtData;
                                tagData.AdditionalFields.Add(comment);

                                // Only read general purpose comments, not those linked to a marker
                                if (0 == cmtData.MarkerId)
                                {
                                    if (commentStr.Length > 0) commentStr.Append(Settings.InternalValueSeparator);
                                    commentStr.Append(comment.Value);
                                }
                            }
                        }
                        else if (header.ID.Equals(CHUNKTYPE_ID3TAG))
                        {
                            id3v2Offset = source.BaseStream.Position;

                            // Zone is already added by Id3v2.Read
                            id3v2StructureHelper.AddZone(id3v2Offset - 8, header.Size + 8, CHUNKTYPE_ID3TAG);
                            id3v2StructureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_ID3TAG);
                        }

                        source.BaseStream.Position = position + header.Size;

                        if (header.ID.Equals(CHUNKTYPE_SOUND) && header.Size % 2 > 0) source.BaseStream.Position += 1; // Sound chunk size must be even
                    }

                    tagData.IntegrateValue(TagData.TAG_FIELD_COMMENT, commentStr.ToString().Replace("\0"," ").Trim());

                    if (-1 == id3v2Offset)
                    {
                        id3v2Offset = 0; // Switch status to "tried to read, but nothing found"

                        if (readTagParams.PrepareForWriting)
                        {
                            id3v2StructureHelper.AddZone(soundChunkPosition + soundChunkSize, 0, CHUNKTYPE_ID3TAG);
                            id3v2StructureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_ID3TAG);
                        }
                    }

                    // Add zone placeholders for future tag writing
                    if (readTagParams.PrepareForWriting)
                    {
                        if (!nameFound)
                        {
                            structureHelper.AddZone(soundChunkPosition, 0, CHUNKTYPE_NAME);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_NAME);
                        }
                        if (!authorFound)
                        {
                            structureHelper.AddZone(soundChunkPosition, 0, CHUNKTYPE_AUTHOR);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_AUTHOR);
                        }
                        if (!copyrightFound)
                        {
                            structureHelper.AddZone(soundChunkPosition, 0, CHUNKTYPE_COPYRIGHT);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_COPYRIGHT);
                        }
                        if (!commentsFound)
                        {
                            structureHelper.AddZone(soundChunkPosition, 0, CHUNKTYPE_COMMENTS);
                            structureHelper.AddSize(containerChunkPos, containerChunkSize, CHUNKTYPE_COMMENTS);
                        }
                    }

                    result = true;
                }
			}
  
			return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            if (zone.Equals(CHUNKTYPE_NAME))
            {
                if (tag.Title.Length > 0)
                {
                    w.Write(Utils.Latin1Encoding.GetBytes(zone));
                    var sizePos = w.BaseStream.Position;
                    w.Write((Int32)0); // Placeholder for field size that will be rewritten at the end of the method

                    var strBytes = Utils.Latin1Encoding.GetBytes(tag.Title);
                    w.Write(strBytes);

                    w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                    w.Write(StreamUtils.EncodeBEInt32(strBytes.Length));

                    result++;
                }
            }
            else if (zone.Equals(CHUNKTYPE_AUTHOR))
            {
                if (tag.Artist.Length > 0)
                {
                    w.Write(Utils.Latin1Encoding.GetBytes(zone));
                    var sizePos = w.BaseStream.Position;
                    w.Write((Int32)0); // Placeholder for field size that will be rewritten at the end of the method

                    var strBytes = Utils.Latin1Encoding.GetBytes(tag.Artist);
                    w.Write(strBytes);

                    w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                    w.Write(StreamUtils.EncodeBEInt32(strBytes.Length));

                    result++;
                }
            }
            else if (zone.Equals(CHUNKTYPE_COPYRIGHT))
            {
                if (tag.Copyright.Length > 0)
                {
                    w.Write(Utils.Latin1Encoding.GetBytes(zone));
                    var sizePos = w.BaseStream.Position;
                    w.Write((Int32)0); // Placeholder for field size that will be rewritten at the end of the method

                    var strBytes = Utils.Latin1Encoding.GetBytes(tag.Copyright);
                    w.Write(strBytes);

                    w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                    w.Write(StreamUtils.EncodeBEInt32(strBytes.Length));

                    result++;
                }
            }
            else if (zone.StartsWith(CHUNKTYPE_ANNOTATION))
            {
                // Do not write anything, this field is deprecated (Cf. specs "Use of this chunk is discouraged within FORM AIFC. The more refined Comments Chunk should be used instead")
            }
            else if (zone.StartsWith(CHUNKTYPE_COMMENTS))
            {
                var applicable = tag.Comment.Length > 0;
                if (!applicable && tag.AdditionalFields.Count > 0)
                {
                    foreach (var fieldInfo in tag.AdditionalFields)
                    {
                        applicable = (fieldInfo.NativeFieldCode.StartsWith(CHUNKTYPE_COMMENTS));
                        if (applicable) break;
                    }
                }

                if (applicable)
                {
                    UInt16 numComments = 0;
                    w.Write(Utils.Latin1Encoding.GetBytes(CHUNKTYPE_COMMENTS));
                    var sizePos = w.BaseStream.Position;
                    w.Write((Int32)0); // Placeholder for 'chunk size' field that will be rewritten at the end of the method
                    w.Write((UInt16)0); // Placeholder for 'number of comments' field that will be rewritten at the end of the method

                    // First write generic comments (those linked to the Comment field)
                    var comments = tag.Comment.Split(Settings.InternalValueSeparator);
                    foreach (var s in comments)
                    {
                        writeCommentChunk(w, null, s);
                        numComments++;
                    }

                    // Then write comments linked to a Marker ID
                    if (tag.AdditionalFields != null && tag.AdditionalFields.Count > 0)
                    {
                        foreach (var fieldInfo in tag.AdditionalFields)
                        {
                            if (fieldInfo.NativeFieldCode.StartsWith(CHUNKTYPE_COMMENTS))
                            {
                                if (((CommentData)fieldInfo.SpecificData).MarkerId != 0)
                                {
                                    writeCommentChunk(w, fieldInfo);
                                    numComments++;
                                }
                            }
                        }
                    }


                    var dataEndPos = w.BaseStream.Position;

                    w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                    w.Write(StreamUtils.EncodeBEInt32((Int32)(dataEndPos - sizePos - 4)));
                    w.Write(StreamUtils.EncodeBEUInt16(numComments));

                    result++;
                }
            }

            return result;
        }

        private void writeCommentChunk(BinaryWriter w, MetaFieldInfo info, String comment = "")
        {
            Byte[] commentData = null;

            if (null == info) // Plain string
            {
                w.Write(StreamUtils.EncodeBEUInt32(encodeTimestamp(DateTime.Now)));
                w.Write((Int16)0);
                commentData = Utils.Latin1Encoding.GetBytes(comment);
            } else
            {
                w.Write(StreamUtils.EncodeBEUInt32(((CommentData)info.SpecificData).Timestamp));
                w.Write(StreamUtils.EncodeBEInt16(((CommentData)info.SpecificData).MarkerId));
                commentData = Utils.Latin1Encoding.GetBytes(info.Value);
            }

            w.Write(StreamUtils.EncodeBEUInt16((UInt16)commentData.Length));
            w.Write(commentData);
        }

        public void WriteID3v2EmbeddingHeader(BinaryWriter w, Int64 tagSize)
        {
            w.Write(Utils.Latin1Encoding.GetBytes(CHUNKTYPE_ID3TAG));
            w.Write(StreamUtils.EncodeBEInt32((Int32)tagSize));
        }

        // AIFx timestamps are "the number of seconds since January 1, 1904"
        private static UInt32 encodeTimestamp(DateTime when)
        {
            return (UInt32)Math.Round( (when.Ticks - timestampBase.Ticks) * 1.0 / TimeSpan.TicksPerSecond );
        }
    }
}