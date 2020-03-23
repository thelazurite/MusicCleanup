using System;
using System.IO;
using System.Collections.Generic;
using Commons;
using static ATL.AudioData.FileStructureHelper;
using System.Text;
using static ATL.AudioData.IO.MetaDataIO;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Free Lossless Audio Codec files manipulation (extension : .FLAC)
    /// </summary>
	class FLAC : IMetaDataIO, IAudioDataIO
	{
		private const Byte META_STREAMINFO      = 0;
		private const Byte META_PADDING         = 1;
		private const Byte META_APPLICATION     = 2;
		private const Byte META_SEEKTABLE       = 3;
		private const Byte META_VORBIS_COMMENT  = 4;
		private const Byte META_CUESHEET        = 5;
        private const Byte META_PICTURE         = 6;

        private const String FLAC_ID = "fLaC";

        private const String ZONE_VORBISTAG = "VORBISTAG";
        private const String ZONE_PICTURE = "PICTURE";


        private class FlacHeader
		{
            public String StreamMarker;
			public Byte[] MetaDataBlockHeader = new Byte[4];
			public Byte[] Info = new Byte[18];
			// 16-bytes MD5 Sum only applies to audio data
    
			public void Reset()
			{
                StreamMarker = "";
				Array.Clear(MetaDataBlockHeader,0,4);
				Array.Clear(Info,0,18);
			}

            public Boolean IsValid()
            {
                return StreamMarker.Equals(FLAC_ID);
            }

            public FlacHeader()
            {
                Reset();
            }
		}

        private readonly String filePath;
        private AudioDataManager.SizeInfo sizeInfo;

        private VorbisTag vorbisTag;
        
        private FlacHeader header;
        IList<FileStructureHelper.Zone> zones; // That's one hint of why interactions with VorbisTag need to be redesigned...

        // Internal metrics
        private Int32 paddingIndex;
		private Boolean paddingLast;
		private UInt32 padding;
		private Int64 audioOffset;
        private Int64 firstBlockPosition;

        // Physical info
		private Byte channels;
		private Int32 sampleRate;
		private Byte bitsPerSample;
		private Int64 samples;

/* Unused for now
		public byte Channels // Number of channels
		{
			get { return channels; }
		}
        public long AudioOffset //offset of audio data
        {
            get { return audioOffset; }
        }
        public byte BitsPerSample // Bits per sample
        {
            get { return bitsPerSample; }
        }
        public long Samples // Number of samples
        {
            get { return samples; }
        }
        public double Ratio // Compression ratio (%)
        {
            get { return getCompressionRatio(); }
        }
        public String ChannelMode
        {
            get { return getChannelMode(); }
        }
*/

        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Int32 SampleRate // Sample rate (hz)
            =>
                sampleRate;

        public Boolean IsVBR => false;

        public Boolean Exists => vorbisTag.Exists;

        public String FileName => filePath;

        public Double BitRate => Math.Round(((Double)(sizeInfo.FileSize - audioOffset)) * 8 / Duration);

        public Double Duration => getDuration();

        public Int32 CodecFamily => AudioDataIoFactory.CfLossless;

        #region IMetaDataReader
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
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE || metaDataType == MetaDataIOFactory.TAG_ID3V2); // Native is for VorbisTag
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            // Audio data
			padding = 0;
			paddingLast = false;
			channels = 0;
			sampleRate = 0;
			bitsPerSample = 0;
			samples = 0;
			paddingIndex = 0;
			audioOffset = 0;
		}

        public FLAC(String path)
        {
            filePath = path;
            header = new FlacHeader();
            resetData();
        }


        // ---------- SUPPORT METHODS

        // Check for right FLAC file data
        private Boolean isValid()
		{
			return ( ( header.IsValid() ) &&
				(channels > 0) &&
				(sampleRate > 0) &&
				(bitsPerSample > 0) &&
				(samples > 0) );
		}

        private void readHeader(BinaryReader source)
        {
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            // Read header data    
            header.Reset();

            header.StreamMarker = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
            header.MetaDataBlockHeader = source.ReadBytes(4);
            header.Info = source.ReadBytes(18);
            source.BaseStream.Seek(16, SeekOrigin.Current); // MD5 sum for audio data
        }

		private Double getDuration()
		{
			if ( (isValid()) && (sampleRate > 0) )  
			{
				return (Double)samples * 1000.0 / sampleRate;
			} 
			else 
			{
				return 0;
			}
		}

/* Unused for now

		//   Get compression ratio
		private double getCompressionRatio()
		{
			if (isValid()) 
			{
				return (double)sizeInfo.FileSize / (samples * channels * bitsPerSample / 8.0) * 100;
			} 
			else 
			{
				return 0;
			}
		}

		//   Get channel mode
		private String getChannelMode()
		{
			String result;
			if (isValid())
			{
				switch(channels)
				{
					case 1 : result = "Mono"; break;
					case 2 : result = "Stereo"; break;
					default: result = "Multi Channel"; break;
				}
			} 
			else 
			{
				result = "";
			}
			return result;
		}

*/

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return Read(source, readTagParams);
        }

        public Boolean Read(BinaryReader source, ReadTagParams readTagParams)
        {
            var result = false;

            if (readTagParams.ReadTag && null == vorbisTag) vorbisTag = new VorbisTag(false, false, false);

            Byte[] aMetaDataBlockHeader;
            Int64 position;
            UInt32 blockLength;
			Int32 blockType;
			Int32 blockIndex;
            Boolean isLast;
            var bPaddingFound = false;

            readHeader(source);

			// Process data if loaded and header valid    
			if ( header.IsValid() )
			{
				channels      = (Byte)( ((header.Info[12] >> 1) & 0x7) + 1 );
				sampleRate    = ( header.Info[10] << 12 | header.Info[11] << 4 | header.Info[12] >> 4 );
				bitsPerSample = (Byte)( ((header.Info[12] & 1) << 4) | (header.Info[13] >> 4) + 1 );
				samples       = ( header.Info[14] << 24 | header.Info[15] << 16 | header.Info[16] << 8 | header.Info[17] );

				if ( 0 == (header.MetaDataBlockHeader[1] & 0x80) ) // metadata block exists
				{
					blockIndex = 0;
                    if (readTagParams.PrepareForWriting)
                    {
                        if (null == zones) zones = new List<Zone>(); else zones.Clear();
                        firstBlockPosition = source.BaseStream.Position;
                    }

                    do // read more metadata blocks if available
					{
						aMetaDataBlockHeader = source.ReadBytes(4);
                        isLast = ((aMetaDataBlockHeader[0] & 0x80) > 0); // last flag ( first bit == 1 )

                        blockIndex++;
                        blockLength = StreamUtils.DecodeBEUInt24(aMetaDataBlockHeader, 1);

						blockType = (aMetaDataBlockHeader[0] & 0x7F); // decode metablock type
                        position = source.BaseStream.Position;

						if ( blockType == META_VORBIS_COMMENT ) // Vorbis metadata
						{
                            if (readTagParams.PrepareForWriting) zones.Add(new Zone(ZONE_VORBISTAG, position - 4, (Int32)blockLength+4, new Byte[0], (Byte)(isLast ? 1 : 0)));
                            vorbisTag.Read(source, readTagParams);
						}
						else if ((blockType == META_PADDING) && (! bPaddingFound) )  // Padding block
						{ 
							padding = blockLength;                                            // if we find more skip & put them in metablock array
							paddingLast = ((aMetaDataBlockHeader[0] & 0x80) != 0);
							paddingIndex = blockIndex;
							bPaddingFound = true;
                            source.BaseStream.Seek(padding, SeekOrigin.Current); // advance into file till next block or audio data start
						}
                        else if (blockType == META_PICTURE)
                        {
                            if (readTagParams.PrepareForWriting) zones.Add(new Zone(ZONE_PICTURE, position - 4, (Int32)blockLength+4, new Byte[0], (Byte)(isLast ? 1 : 0)));
                            vorbisTag.ReadPicture(source.BaseStream, readTagParams);
                        }
                        // TODO : support for CUESHEET block

                        if (blockType < 7)
                        {
                            source.BaseStream.Seek(position + blockLength, SeekOrigin.Begin);
                        }
                        else
                        {
                            // Abnormal header : incorrect size and/or misplaced last-metadata-block flag
                            break;
                        }
					}
					while ( !isLast );

                    if (readTagParams.PrepareForWriting)
                    {
                        var vorbisTagFound = false;
                        var pictureFound = false;

                        foreach(var zone in zones)
                        {
                            if (zone.Name.Equals(ZONE_PICTURE)) pictureFound = true;
                            else if (zone.Name.Equals(ZONE_VORBISTAG)) vorbisTagFound = true;
                        }

                        if (!vorbisTagFound) zones.Add(new Zone(ZONE_VORBISTAG, firstBlockPosition, 0, new Byte[0]));
                        if (!pictureFound) zones.Add(new Zone(ZONE_PICTURE, firstBlockPosition, 0, new Byte[0]));
                    }
				}
			}

            if (isValid())
            {
                audioOffset = source.BaseStream.Position;  // we need that to rebuild the file if nedeed
                result = true;
            }

			return result;  
		}

        // NB1 : previously scattered picture blocks become contiguous after rewriting
        // NB2 : This only works if writeVorbisTag is called _before_ writePictures, since tagData fusion is done by vorbisTag.Write
        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            var result = true;
            Int32 oldTagSize, writtenFields;
            Int64 newTagSize;
            var pictureBlockFound = false;
            Int64 cumulativeDelta = 0;

            // Read all the fields in the existing tag (including unsupported fields)
            var readTagParams = new ReadTagParams(true, true);
            readTagParams.PrepareForWriting = true;
            Read(r, readTagParams);

            // Rewrite vorbis tag zone
            foreach (var zone in zones)
            {
                oldTagSize = zone.Size;

                // Write new tag to a MemoryStream
                using (var s = new MemoryStream(zone.Size))
                using (var msw = new BinaryWriter(s, Settings.DefaultTextEncoding))
                {
                    if (zone.Name.Equals(ZONE_VORBISTAG)) writtenFields = writeVorbisTag(msw, tag, 1 == zone.Flag);
                    else if (zone.Name.Equals(ZONE_PICTURE))
                    {
                        if (!pictureBlockFound) // All pictures are written at the position of the 1st picture block
                        {
                            pictureBlockFound = true;
                            writtenFields = writePictures(msw, vorbisTag.EmbeddedPictures, 1 == zone.Flag);
                        } else
                        {
                            writtenFields = 0; // Other picture blocks are erased
                        }
                    } else
                    {
                        writtenFields = 0;
                    }

                    if (0 == writtenFields) s.SetLength(0); // No core signature for metadata in FLAC structure

                    newTagSize = s.Length;

                    // -- Adjust tag slot to new size in file --
                    var tagBeginOffset = zone.Offset + cumulativeDelta;
                    var tagEndOffset = tagBeginOffset + zone.Size;

                    // Need to build a larger file
                    if (newTagSize > zone.Size)
                    {
                        StreamUtils.LengthenStream(w.BaseStream, tagEndOffset, (UInt32)(newTagSize - zone.Size));
                    }
                    else if (newTagSize < zone.Size) // Need to reduce file size
                    {
                        StreamUtils.ShortenStream(w.BaseStream, tagEndOffset, (UInt32)(zone.Size - newTagSize));
                    }

                    // Copy tag contents to the new slot
                    r.BaseStream.Seek(tagBeginOffset, SeekOrigin.Begin);
                    s.Seek(0, SeekOrigin.Begin);

                    if (newTagSize > zone.CoreSignature.Length)
                    {
                        StreamUtils.CopyStream(s, w.BaseStream);
                    }
                    else
                    {
                        if (zone.CoreSignature.Length > 0) msw.Write(zone.CoreSignature);
                    }

                    cumulativeDelta += newTagSize - oldTagSize;
                }
            } // Loop through zones

            return result;
        }

        private Int32 writeVorbisTag(BinaryWriter w, TagData tag, Boolean isLast)
        {
            Int32 result;
            Int64 sizePos, dataPos, finalPos;
            var blockType = META_VORBIS_COMMENT;
            if (isLast) blockType = (Byte)(blockType & 0x80);

            w.Write(blockType);
            sizePos = w.BaseStream.Position;
            w.Write(new Byte[] { 0, 0, 0 }); // Placeholder for 24-bit integer that will be rewritten at the end of the method

            dataPos = w.BaseStream.Position;
            result = vorbisTag.Write(w.BaseStream, tag);

            finalPos = w.BaseStream.Position;
            w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            w.Write(StreamUtils.EncodeBEUInt24((UInt32)(finalPos - dataPos)));
            w.BaseStream.Seek(finalPos, SeekOrigin.Begin);

            return result;
        }

        private Int32 writePictures(BinaryWriter w, IList<PictureInfo> pictures, Boolean isLast)
        {
            var result = 0;
            Int64 sizePos, dataPos, finalPos;
            Byte blockType;

            foreach (var picture in pictures)
            {
                blockType = META_PICTURE;
                if (isLast) blockType = (Byte)(blockType & 0x80);
                
                w.Write(blockType);
                sizePos = w.BaseStream.Position;
                w.Write(new Byte[] { 0, 0, 0 }); // Placeholder for 24-bit integer that will be rewritten at the end of the method

                dataPos = w.BaseStream.Position;
                vorbisTag.WritePicture(w, picture.PictureData, picture.NativeFormat, ImageUtils.GetMimeTypeFromImageFormat(picture.NativeFormat), picture.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported) ? picture.NativePicCode : ID3v2.EncodeID3v2PictureType(picture.PicType), picture.Description);

                finalPos = w.BaseStream.Position;
                w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                w.Write(StreamUtils.EncodeBEUInt24((UInt32)(finalPos - dataPos)));
                w.BaseStream.Seek(finalPos, SeekOrigin.Begin);
                result++;
            }

            return result;
        }

        public Boolean Remove(BinaryWriter w)
        {
            var result = true;
            Int64 cumulativeDelta = 0;

            foreach (var zone in zones)
            {
                if (zone.Offset > -1 && zone.Size > zone.CoreSignature.Length)
                {
                    StreamUtils.ShortenStream(w.BaseStream, zone.Offset + zone.Size - cumulativeDelta, (UInt32)(zone.Size - zone.CoreSignature.Length));
                    vorbisTag.Clear();

                    cumulativeDelta += zone.Size - zone.CoreSignature.Length;
                }
            }

            return result;
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