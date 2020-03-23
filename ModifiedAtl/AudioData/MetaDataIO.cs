using Commons;
using System;
using System.Collections.Generic;
using System.IO;
using static ATL.AudioData.FileStructureHelper;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Superclass that "consolidates" all metadata I/O algorithms to ease development of new classes and minimize their code
    /// </summary>
    public abstract class MetaDataIO : IMetaDataIO
    {
        // ------ CONSTS -----------------------------------------------------

        // Default tag offset
        protected const Int32 TO_EOF = 0;     // Tag offset is at End Of File
        protected const Int32 TO_BOF = 1;     // Tag offset is at Beginning Of File
        protected const Int32 TO_BUILTIN = 2; // Tag offset is at a Built-in location (e.g. MP4)

        // Rating conventions
        public const Int32 RC_ID3v2 = 0;       // ID3v2 convention (0..255 scale with various tweaks)
        public const Int32 RC_ASF = 1;         // ASF convention (0..100 scale with 1 being encoded as 1)
        public const Int32 RC_APE = 2;         // APE convention (proper 0..100 scale)


        // ------ INNER CLASSES -----------------------------------------------------

        /// <summary>
        /// Container class describing tag reading parameters
        /// </summary>
        public class ReadTagParams
        {
            /// <summary>
            /// True : read metadata; False : do not read metadata (only "physical" audio data)
            /// </summary>
            public Boolean ReadTag = true;

            /// <summary>
            /// True : read all metadata frames; False : only read metadata frames that match IMetaDataIO public properties (="supported" metadata)
            /// </summary>
            public Boolean ReadAllMetaFrames = false;

            /// <summary>
            /// True : read embedded pictures; False : skip embedded pictures
            /// </summary>
            public Boolean ReadPictures = false;

            /// <summary>
            /// Handler to call when reading embedded picture binary data. If the handler is null, embedded pictures binary data will _not_ be read.
            /// </summary>
            public TagData.PictureStreamHandlerDelegate PictureStreamHandler = null;

            /// <summary>
            /// True : read all data that will be useful for writing; False : only read metadata values
            /// </summary>
            public Boolean PrepareForWriting = false;

            /// <summary>
            /// File offset to start reading metadata from (bytes)
            /// </summary>
            public Int64 offset = 0;

            [Obsolete]
            public ReadTagParams(TagData.PictureStreamHandlerDelegate pictureStreamHandler, Boolean readAllMetaFrames)
            {
                PictureStreamHandler = pictureStreamHandler; ReadAllMetaFrames = readAllMetaFrames;
            }

            public ReadTagParams(Boolean readPictures, Boolean readAllMetaFrames)
            {
                ReadPictures = readPictures; ReadAllMetaFrames = readAllMetaFrames;
            }
        }


        // ------ PROPERTIES -----------------------------------------------------

        protected Boolean tagExists;
        protected Int32 tagVersion;
        protected TagData tagData;
        protected IList<PictureInfo> pictureTokens;

        private IList<KeyValuePair<String, Int32>> picturePositions;
        
        internal FileStructureHelper structureHelper;

        protected IMetaDataEmbedder embedder;


        // ------ READ-ONLY "PHYSICAL" TAG INFO FIELDS ACCESSORS -----------------------------------------------------

        /// <summary>
        /// True if tag has been found in media file
        /// </summary>
        public Boolean Exists => this.tagExists;

        /// <summary>
        /// Tag version
        /// </summary>
        public Int32 Version => this.tagVersion;

        /// <summary>
        /// Total size of tag (in bytes)
        /// </summary>
        public Int32 Size
        {
            get {
                var result = 0;

                foreach (var zone in Zones) result += zone.Size;

                return result;
            }
        }
        public ICollection<Zone> Zones => structureHelper.Zones;


        // ------ TAGDATA FIELDS ACCESSORS -----------------------------------------------------

        /// <summary>
        /// Song/piece title
        /// </summary>
        public String Title
        {
            get => Utils.ProtectValue(tagData.Title);
            set => tagData.Title = value;
        }
        /// <summary>
        /// Artist (Performer)
        /// </summary>
        public String Artist
        {
            get
            {
                var result = Utils.ProtectValue(tagData.Artist);
                if (0 == result.Length) result = AlbumArtist;
                return result;
            }
            set => tagData.Artist = value;
        }
        /// <summary>
        /// Album Artist
        /// </summary>
        public String AlbumArtist
        {
            get => Utils.ProtectValue(tagData.AlbumArtist);
            set => tagData.AlbumArtist = value;
        }
        /// <summary>
        /// Composer
        /// </summary>
        public String Composer
        {
            get => Utils.ProtectValue(tagData.Composer);
            set => tagData.Composer = value;
        }
        /// <summary>
        /// Album title
        /// </summary>
        public String Album
        {
            get => Utils.ProtectValue(tagData.Album);
            set => tagData.Album = value;
        }
        /// <summary>
        /// Track number
        /// </summary>
        public UInt16 Track
        {
            get => TrackUtils.ExtractTrackNumber(tagData.TrackNumber);
            set => tagData.TrackNumber = value.ToString();
        }
        /// <summary>
        /// Disc number
        /// </summary>
        public UInt16 Disc
        {
            get => TrackUtils.ExtractTrackNumber(tagData.DiscNumber);
            set => tagData.DiscNumber = value.ToString();
        }
        /// <summary>
        /// Rating, from 0 to 5
        /// </summary>
        [Obsolete("Use Popularity")]
        public UInt16 Rating => (UInt16)Math.Round(Popularity * 5);

        /// <summary>
        /// Rating, from 0 to 100%
        /// </summary>
        public Single Popularity => TrackUtils.DecodePopularity(tagData.Rating, ratingConvention);

        /// <summary>
        /// Release year
        /// </summary>
        public String Year
        {
            get
            {
                String result;
                result = TrackUtils.ExtractStrYear(tagData.RecordingYear);
                if (0 == result.Length) result = TrackUtils.ExtractStrYear(tagData.RecordingDate);
                return result;
            }
            set => tagData.RecordingYear = value;
        }
        /// <summary>
        /// Release date (DateTime.MinValue if field does not exist)
        /// </summary>
        public DateTime Date
        {
            get // TODO - TEST EXTENSIVELY
            {
                var result = DateTime.MinValue;
                if (!DateTime.TryParse(tagData.RecordingDate, out result)) // First try with a proper Recording date field
                {
                    var success = false;
                    var dayMonth = Utils.ProtectValue(tagData.RecordingDayMonth); // If not, try to assemble year and dateMonth (e.g. ID3v2)
                    if (4 == dayMonth.Length && 4 == Year.Length)
                    {
                        success = DateTime.TryParse(dayMonth.Substring(0, 2) + "/" + dayMonth.Substring(3, 2) + "/" + Year, out result);
                    }
                }
                return result;
            }
            set => tagData.RecordingDate = value.ToShortDateString();
        }
        /// <summary>
        /// Genre name
        /// </summary>
        public String Genre
        {
            get => Utils.ProtectValue(tagData.Genre);
            set => tagData.Genre = value;
        }
        /// <summary>
        /// Commment
        /// </summary>
        public String Comment
        {
            get => Utils.ProtectValue(tagData.Comment);
            set => tagData.Comment = value;
        }
        /// <summary>
        /// Copyright
        /// </summary>
        public String Copyright
        {
            get => Utils.ProtectValue(tagData.Copyright);
            set => tagData.Copyright = value;
        }
        /// <summary>
        /// Original artist
        /// </summary>
        public String OriginalArtist
        {
            get => Utils.ProtectValue(tagData.OriginalArtist);
            set => tagData.OriginalArtist = value;
        }
        /// <summary>
        /// Original album
        /// </summary>
        public String OriginalAlbum
        {
            get => Utils.ProtectValue(tagData.OriginalAlbum);
            set => tagData.OriginalAlbum = value;
        }
        /// <summary>
        /// General Description
        /// </summary>
        public String GeneralDescription
        {
            get => Utils.ProtectValue(tagData.GeneralDescription);
            set => tagData.GeneralDescription = value;
        }
        /// <summary>
        /// Publisher
        /// </summary>
        public String Publisher
        {
            get => Utils.ProtectValue(tagData.Publisher);
            set => tagData.Publisher = value;
        }
        /// <summary>
        /// Conductor
        /// </summary>
        public String Conductor
        {
            get => Utils.ProtectValue(tagData.Conductor);
            set => tagData.Conductor = value;
        }


        // ------ NON-TAGDATA FIELDS ACCESSORS -----------------------------------------------------

        /// <summary>
        /// Collection of fields that are not supported by ModifiedAtl (i.e. not implemented by a getter/setter of MetaDataIO class; e.g. custom fields such as "MOOD")
        /// NB : when querying multi-stream files (e.g. MP4, ASF), this attribute will only return stream-independent properties of the whole file, in the first language available
        /// For detailed, stream-by-stream and language-by-language properties, use GetAdditionalFields
        /// </summary>
        public IDictionary<String, String> AdditionalFields
        {
            get {
                IDictionary<String, String> result = new Dictionary<String, String>();

                var additionalFields = GetAdditionalFields(0);
                foreach (var fieldInfo in additionalFields)
                {
                    if (!result.ContainsKey(fieldInfo.NativeFieldCode)) result.Add(fieldInfo.NativeFieldCode, fieldInfo.Value);
                }

                return result;
            }
        }

        public IList<MetaFieldInfo> GetAdditionalFields(Int32 streamNumber = -1, String language = "")
        {
            IList<MetaFieldInfo> result = new List<MetaFieldInfo>();

            foreach (var fieldInfo in tagData.AdditionalFields)
            {
                if (
                    getImplementedTagType().Equals(fieldInfo.TagType)
                    && (-1 == streamNumber) || (streamNumber == fieldInfo.StreamNumber)
                    && ("".Equals(language) || language.Equals(fieldInfo.Language))
                    )
                {
                    result.Add(fieldInfo);
                }
            }

            return result;
        }

        public IList<PictureInfo> EmbeddedPictures
        {
            get
            {
                IList<PictureInfo> result = new List<PictureInfo>();

                foreach (var picInfo in tagData.Pictures)
                {
                    if ( !picInfo.MarkedForDeletion && ( !picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported) || (picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported) && picInfo.TagType.Equals(getImplementedTagType()) ) ) ) result.Add(picInfo);
                }

                return result;
            }
        }

        /// <summary>
        /// Each positioned flag indicates the presence of an embedded picture
        /// </summary>
        public IList<PictureInfo> PictureTokens => this.pictureTokens;

        public IList<ChapterInfo> Chapters
        {
            get
            {
                IList<ChapterInfo> result = new List<ChapterInfo>();

                if (tagData.Chapters != null)
                {
                    foreach (var chapter in tagData.Chapters)
                    {
                        result.Add(new ChapterInfo(chapter));
                    }
                }

                return result;
            }
        }

        public virtual Byte FieldCodeFixedLength => 0;

        protected virtual Boolean isLittleEndian => true;

        protected virtual Byte ratingConvention => RC_ID3v2;


        // ------ PICTURE HELPER METHODS -----------------------------------------------------

        protected void addPictureToken(PictureInfo.PIC_TYPE picType)
        {
            pictureTokens.Add( new PictureInfo(ImageFormat.Undefined, picType) );
        }

        protected void addPictureToken(Int32 tagType, Byte nativePicCode)
        {
            pictureTokens.Add(new PictureInfo(ImageFormat.Undefined, tagType, nativePicCode) );
        }

        protected void addPictureToken(Int32 tagType, String nativePicCode)
        {
            pictureTokens.Add(new PictureInfo(ImageFormat.Undefined, tagType, nativePicCode));
        }

        protected Int32 takePicturePosition(PictureInfo.PIC_TYPE picType)
        {
            return takePicturePosition(new PictureInfo(ImageFormat.Undefined, picType));
        }

        protected Int32 takePicturePosition(Int32 tagType, Byte nativePicCode)
        {
            return takePicturePosition(new PictureInfo(ImageFormat.Undefined, tagType, nativePicCode));
        }

        protected Int32 takePicturePosition(Int32 tagType, String nativePicCode)
        {
            return takePicturePosition(new PictureInfo(ImageFormat.Undefined, tagType, nativePicCode));
        }

        protected Int32 takePicturePosition(PictureInfo picInfo)
        {
            var picId = picInfo.ToString();
            var found = false;
            var picPosition = 1;

            for (var i=0;i<picturePositions.Count; i++)
            {
                if (picturePositions[i].Key.Equals(picId))
                {
                    picPosition = picturePositions[i].Value + 1;
                    picturePositions[i] = new KeyValuePair<String, Int32>(picId, picPosition);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                picturePositions.Add(new KeyValuePair<String, Int32>(picId, 1));
            }

            return picPosition;
        }


        // ------ ABSTRACT METHODS -----------------------------------------------------

        protected abstract Boolean read(BinaryReader source, ReadTagParams readTagParams);

        protected abstract Int32 write(TagData tag, BinaryWriter w, String zone);

        protected abstract Int32 getDefaultTagOffset();

        protected abstract Int32 getImplementedTagType();

        protected abstract Byte getFrameMapping(String zone, String ID, Byte tagVersion);


        // ------ COMMON METHODS -----------------------------------------------------

        public void SetEmbedder(IMetaDataEmbedder embedder)
        {
            this.embedder = embedder;
        }

        protected void ResetData()
        {
            tagExists = false;
            tagVersion = 0;

            if (null == tagData) tagData = new TagData(); else tagData.Clear();
            if (null == pictureTokens) pictureTokens = new List<PictureInfo>(); else pictureTokens.Clear();
            if (null == picturePositions) picturePositions = new List<KeyValuePair<String, Int32>>(); else picturePositions.Clear();
            if (null == structureHelper) structureHelper = new FileStructureHelper(isLittleEndian); else structureHelper.Clear();
        }

        public void SetMetaField(String ID, String data, Boolean readAllMetaFrames, String zone = FileStructureHelper.DEFAULT_ZONE_NAME, Byte tagVersion = 0, UInt16 streamNumber = 0, String language = "")
        {
            // Finds the ModifiedAtl field identifier
            var supportedMetaID = getFrameMapping(zone, ID, tagVersion);

            // If ID has been mapped with an 'classic' ModifiedAtl field, store it in the dedicated place...
            if (supportedMetaID < 255)
            {
                setMetaField(supportedMetaID, data);
            }
            else if (readAllMetaFrames) // ...else store it in the additional fields Dictionary
            {
                if (ID.Length > 0)
                {
                    var fieldInfo = new MetaFieldInfo(getImplementedTagType(), ID, data, streamNumber, language, zone);
                    if (tagData.AdditionalFields.Contains(fieldInfo)) // Prevent duplicates
                    {
                        tagData.AdditionalFields.Remove(fieldInfo);
                    }
                    tagData.AdditionalFields.Add(fieldInfo);
                }
            }
        }

        private void setMetaField(Byte ID, String data)
        {
            tagData.IntegrateValue(ID, data);
        }

        public void Clear()
        {
            ResetData();
        }

        public Boolean Read(BinaryReader Source, ReadTagParams readTagParams)
        {
            if (readTagParams.PrepareForWriting) structureHelper.Clear();

            return read(Source, readTagParams);
        }

        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            Int64 oldTagSize;
            Int64 newTagSize;
            Int64 cumulativeDelta = 0;
            var result = true;

            // Constraint-check on non-supported values
            if (FieldCodeFixedLength > 0)
            {
                if (tag.Pictures != null)
                {
                    foreach (var picInfo in tag.Pictures)
                    {
                        if (PictureInfo.PIC_TYPE.Unsupported.Equals(picInfo.PicType) && (picInfo.TagType.Equals(getImplementedTagType())))
                        {
                            if ((-1 == picInfo.NativePicCode) && (Utils.ProtectValue(picInfo.NativePicCodeStr).Length != FieldCodeFixedLength))
                            {
                                throw new NotSupportedException("Field code fixed length is " + FieldCodeFixedLength + "; detected field '" + Utils.ProtectValue(picInfo.NativePicCodeStr) + "' is " + Utils.ProtectValue(picInfo.NativePicCodeStr).Length + " characters long and cannot be written");
                            }
                        }
                    }
                }
                foreach (var fieldInfo in tag.AdditionalFields)
                {
                    if (fieldInfo.TagType.Equals(getImplementedTagType()) || MetaDataIOFactory.TAG_ANY == fieldInfo.TagType)
                    {
                        if (Utils.ProtectValue(fieldInfo.NativeFieldCode).Length != FieldCodeFixedLength)
                        {
                            throw new NotSupportedException("Field code fixed length is " + FieldCodeFixedLength + "; detected field '" + Utils.ProtectValue(fieldInfo.NativeFieldCode) + "' is " + Utils.ProtectValue(fieldInfo.NativeFieldCode).Length + " characters long and cannot be written");
                        }
                    }
                }
            }

            structureHelper.Clear();
            tagData.Pictures.Clear();

            // Read all the fields in the existing tag (including unsupported fields)
            var readTagParams = new ReadTagParams(true, true);
            readTagParams.PrepareForWriting = true;

            if (embedder != null && embedder.HasEmbeddedID3v2 > 0)
            {
                readTagParams.offset = embedder.HasEmbeddedID3v2;
            }

            this.read(r, readTagParams);

            if (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2)
            {
                structureHelper.Clear();
                structureHelper.AddZone(embedder.Id3v2Zone);
            }

            // Give engine something to work with if the tag is really empty
            if (!tagExists && 0 == Zones.Count)
            {
                structureHelper.AddZone(0, 0);
            }

            TagData dataToWrite;
            dataToWrite = tagData;
            dataToWrite.IntegrateValues(tag); // Merge existing information + new tag information

            foreach (var zone in Zones)
            {
                oldTagSize = zone.Size;
                Boolean isTagWritten;

                // Write new tag to a MemoryStream
                using (var s = new MemoryStream(zone.Size))
                using (var msw = new BinaryWriter(s, Settings.DefaultTextEncoding))
                {
                    if (write(dataToWrite, msw, zone.Name) > 0)
                    {
                        isTagWritten = true;
                        newTagSize = s.Length;

                        if (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2  && embedder.ID3v2EmbeddingHeaderSize > 0)
                        {
                            StreamUtils.LengthenStream(s, 0, embedder.ID3v2EmbeddingHeaderSize);
                            s.Position = 0;
                            embedder.WriteID3v2EmbeddingHeader(msw, newTagSize);

                            newTagSize = s.Length;
                        }
                    } else {
                        isTagWritten = false;
                        newTagSize = zone.CoreSignature.Length;
                    }

                    // -- Adjust tag slot to new size in file --
                    Int64 tagBeginOffset, tagEndOffset;

                    if (tagExists && zone.Size > zone.CoreSignature.Length) // An existing tag has been reprocessed
                    {
                        tagBeginOffset = zone.Offset + cumulativeDelta;
                        tagEndOffset = tagBeginOffset + zone.Size;
                    }
                    else // A brand new tag has been added to the file
                    {
                        if (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2)
                        {
                            tagBeginOffset = embedder.Id3v2Zone.Offset;
                        }
                        else
                        {
                            switch (getDefaultTagOffset())
                            {
                                case TO_EOF: tagBeginOffset = r.BaseStream.Length; break;
                                case TO_BOF: tagBeginOffset = 0; break;
                                case TO_BUILTIN: tagBeginOffset = zone.Offset + cumulativeDelta; break;
                                default: tagBeginOffset = -1; break;
                            }
                        }
                        tagEndOffset = tagBeginOffset + zone.Size;
                    }

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

                    if (isTagWritten)
                    {
                        StreamUtils.CopyStream(s, w.BaseStream);
                    } else
                    {
                        if (zone.CoreSignature.Length > 0) msw.Write(zone.CoreSignature);
                    }

                    var delta = (Int32)(newTagSize - oldTagSize);
                    cumulativeDelta += delta;

                    // Edit wrapping size markers and frame counters if needed
                    if (delta != 0 && (MetaDataIOFactory.TAG_NATIVE == getImplementedTagType() || (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2)))
                    {
                        Int32 action;

                        if (oldTagSize == zone.CoreSignature.Length && isTagWritten) action = ACTION_ADD;
                        else if (newTagSize == zone.CoreSignature.Length && !isTagWritten) action = ACTION_DELETE;
                        else action = ACTION_EDIT;

                        result = structureHelper.RewriteHeaders(w, delta, action, zone.Name);
                    }
                }
            } // Loop through zones

            // Update tag information without calling Read
            /* TODO - this implementation is too risky : 
             *   - if one of the writing operations fails, data is updated as if everything went right
             *   - any picture slot with a markForDeletion flag is recorded as-is in the tag
             */
            tagData = dataToWrite;

            return result;
        }

        public virtual Boolean Remove(BinaryWriter w)
        {
            var result = true;
            Int64 cumulativeDelta = 0;

            if (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2)
            {
                structureHelper.Clear();
                structureHelper.AddZone(embedder.Id3v2Zone);
            }

            foreach (var zone in Zones)
            {
                if (zone.Offset > -1)
                {
                    if (zone.Size > zone.CoreSignature.Length) StreamUtils.ShortenStream(w.BaseStream, zone.Offset + zone.Size - cumulativeDelta, (UInt32)(zone.Size - zone.CoreSignature.Length));

                    if (zone.CoreSignature.Length > 0)
                    {
                        w.BaseStream.Position = zone.Offset - cumulativeDelta;
                        w.Write(zone.CoreSignature);
                    }
                    if (MetaDataIOFactory.TAG_NATIVE == getImplementedTagType() || (embedder != null && getImplementedTagType() == MetaDataIOFactory.TAG_ID3V2)) result = result && structureHelper.RewriteHeaders(w, -zone.Size + zone.CoreSignature.Length, FileStructureHelper.ACTION_DELETE, zone.Name);

                    cumulativeDelta += zone.Size - zone.CoreSignature.Length;
                }
            }

            return result;
        }
    }
}
