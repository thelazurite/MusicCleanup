using System;
using System.IO;
using ATL.Logging;
using System.Collections.Generic;
using System.Text;
using Commons;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Windows Media Audio 7,8 and 9 files manipulation (extension : .WMA)
    /// </summary>
	class WMA : MetaDataIO, IAudioDataIO
	{
		// Channel modes
		public const Byte WMA_CM_UNKNOWN = 0;                                               // Unknown
		public const Byte WMA_CM_MONO = 1;                                                     // Mono
		public const Byte WMA_CM_STEREO = 2;                                                 // Stereo

        private const String ZONE_CONTENT_DESCRIPTION = "contentDescription";
        private const String ZONE_EXTENDED_CONTENT_DESCRIPTION = "extContentDescription";
        private const String ZONE_EXTENDED_HEADER_METADATA = "extHeaderMeta";
        private const String ZONE_EXTENDED_HEADER_METADATA_LIBRARY = "extHeaderMetaLibrary";

        // Channel mode names
        public static String[] WMA_MODE = new String[3] {"Unknown", "Mono", "Stereo"};


        // Object IDs
        private static readonly Byte[] WMA_HEADER_ID = new Byte[16] { 48, 38, 178, 117, 142, 102, 207, 17, 166, 217, 0, 170, 0, 98, 206, 108 };
        private static readonly Byte[] WMA_HEADER_EXTENSION_ID = new Byte[16] { 0xB5, 0x03, 0xBF, 0x5F, 0x2E, 0xA9, 0xCF, 0x11, 0x8E, 0xE3, 0x00, 0xc0, 0x0c, 0x20, 0x53, 0x65 };

        private static readonly Byte[] WMA_METADATA_OBJECT_ID = new Byte[16] { 0xEA, 0xCB, 0xF8, 0xC5, 0xAF, 0x5B, 0x77, 0x48, 0x84, 0x67, 0xAA, 0x8C, 0x44, 0xFA, 0x4C, 0xCA };
        private static readonly Byte[] WMA_METADATA_LIBRARY_OBJECT_ID = new Byte[16] { 0x94, 0x1C, 0x23, 0x44, 0x98, 0x94, 0xD1, 0x49, 0xA1, 0x41, 0x1D, 0x13, 0x4E, 0x45, 0x70, 0x54 };

        private static readonly Byte[] WMA_FILE_PROPERTIES_ID = new Byte[16] { 161, 220, 171, 140, 71, 169, 207, 17, 142, 228, 0, 192, 12, 32, 83, 101 };
        private static readonly Byte[] WMA_STREAM_PROPERTIES_ID = new Byte[16] { 145, 7, 220, 183, 183, 169, 207, 17, 142, 230, 0, 192, 12, 32, 83, 101 };
        private static readonly Byte[] WMA_CONTENT_DESCRIPTION_ID = new Byte[16] { 51, 38, 178, 117, 142, 102, 207, 17, 166, 217, 0, 170, 0, 98, 206, 108 };
        private static readonly Byte[] WMA_EXTENDED_CONTENT_DESCRIPTION_ID = new Byte[16] { 64, 164, 208, 210, 7, 227, 210, 17, 151, 240, 0, 160, 201, 94, 168, 80 };

        private static readonly Byte[] WMA_LANGUAGE_LIST_OBJECT_ID = new Byte[16] { 0xA9, 0x46, 0x43, 0x7C, 0xE0, 0xEF, 0xFC, 0x4B, 0xB2, 0x29, 0x39, 0x3E, 0xDE, 0x41, 0x5C, 0x85 };


        // Format IDs
        private const Int32 WMA_ID = 0x161;
        private const Int32 WMA_PRO_ID = 0x162;
        private const Int32 WMA_LOSSLESS_ID = 0x163;
        private const Int32 WMA_GSM_CBR_ID = 0x7A21;
        private const Int32 WMA_GSM_VBR_ID = 0x7A22;

        // Max. number of characters in tag field
        private const Byte WMA_MAX_STRING_SIZE = 250;

        // File data - for internal use
        private class FileData
        {
            public Int64 HeaderSize;
            public Int32 FormatTag;                                       // Format ID tag
            public UInt16 Channels;                                // Number of channels
            public Int32 SampleRate;                                   // Sample rate (hz)

            public UInt32 ObjectCount;                     // Number of high-level objects
            public Int64 ObjectListOffset;       // Offset of the high-level objects list


            public FileData() { Reset(); }

            public void Reset()
            {
                HeaderSize = 0;
                FormatTag = 0;
                Channels = 0;
                SampleRate = 0;
                ObjectCount = 0;
                ObjectListOffset = -1;
            }
        }

        private FileData fileData;

		private Byte channelModeID;
		private Int32 sampleRate;
		private Boolean isVBR;
		private Boolean isLossless;
        private Double bitrate;
        private Double duration;

        private static IDictionary<String, Byte> frameMapping; // Mapping between WMA frame codes and ModifiedAtl frame codes
        private static IList<String> embeddedFields; // Field that are embedded in standard ASF description, and do not need to be written in any other frame
        private static IDictionary<String, UInt16> frameClasses; // Mapping between WMA frame codes and frame classes that aren't class 0 (Unicode string)

        private IList<String> languages; // Optional language index described in the WMA header

        private AudioDataManager.SizeInfo sizeInfo;
        private String filePath;

/* Unused for now
        public bool IsStreamed
        {
            get { return true; }
        }
        public byte ChannelModeID // Channel mode code
        {
            get { return this.channelModeID; }
        }
        public String ChannelMode // Channel mode name
        {
            get { return this.getChannelMode(); }
        }
*/

        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Int32 SampleRate // Sample rate (hz)
            =>
                sampleRate;

        public Boolean IsVBR => isVBR;

        public Int32 CodecFamily => isLossless ? AudioDataIoFactory.CfLossless : AudioDataIoFactory.CfLossy;
        public String FileName => filePath;
        public Double BitRate => bitrate;
        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_ID3V1) || (metaDataType == MetaDataIOFactory.TAG_ID3V2) || (metaDataType == MetaDataIOFactory.TAG_APE) || (metaDataType == MetaDataIOFactory.TAG_NATIVE);
        }
        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier according to the ID3v2 version
            if (frameMapping.ContainsKey(ID)) supportedMetaId = frameMapping[ID];

            return supportedMetaId;
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
        protected override Byte ratingConvention => RC_ASF;


        // ---------- CONSTRUCTORS & INITIALIZERS

        static WMA()
        {
            // NB : WM/TITLE, WM/AUTHOR, WM/COPYRIGHT, WM/DESCRIPTION and WM/RATING are not WMA extended fields; therefore
            // their ID will not appear as is in the WMA header. 
            // Their info is contained in the standard Content Description block at the very beginning of the file
            frameMapping = new Dictionary<String, Byte>
            {
                { "WM/TITLE", TagData.TAG_FIELD_TITLE },
                { "WM/AlbumTitle", TagData.TAG_FIELD_ALBUM },
                { "WM/AUTHOR", TagData.TAG_FIELD_ARTIST },
                { "WM/COPYRIGHT", TagData.TAG_FIELD_COPYRIGHT },
                { "WM/DESCRIPTION", TagData.TAG_FIELD_COMMENT },
                { "WM/Year", TagData.TAG_FIELD_RECORDING_YEAR },
                { "WM/Genre", TagData.TAG_FIELD_GENRE },
                { "WM/TrackNumber", TagData.TAG_FIELD_TRACK_NUMBER },
                { "WM/PartOfSet", TagData.TAG_FIELD_DISC_NUMBER },
                { "WM/RATING", TagData.TAG_FIELD_RATING },
                { "WM/SharedUserRating", TagData.TAG_FIELD_RATING },
                { "WM/Composer", TagData.TAG_FIELD_COMPOSER },
                { "WM/AlbumArtist", TagData.TAG_FIELD_ALBUM_ARTIST },
                { "WM/Conductor", TagData.TAG_FIELD_CONDUCTOR }
            };

            embeddedFields = new List<String>
            {
                { "WM/TITLE" },
                { "WM/AUTHOR" },
                { "WM/COPYRIGHT" },
                { "WM/DESCRIPTION" },
                { "WM/RATING" }
            };


            frameClasses = new Dictionary<String, UInt16>(); // To be further populated while reading
            frameClasses.Add("WM/SharedUserRating", 3);
        }

        private void resetData()
        {
            channelModeID = WMA_CM_UNKNOWN;
            sampleRate = 0;
            isVBR = false;
            isLossless = false;
            bitrate = 0;
            duration = 0;

            ResetData();
        }

        public WMA(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // ---------- SUPPORT METHODS

        private static void addFrameClass(String frameCode, UInt16 frameClass)
        {
            if (!frameClasses.ContainsKey(frameCode)) frameClasses.Add(frameCode, frameClass);
        }

        private void cacheLanguageIndex(Stream source)
        {
            if (null == languages)
            {
                Int64 position, initialPosition;
                UInt64 objectSize;
                Byte[] bytes;

                languages = new List<String>();

                initialPosition = source.Position;
                source.Seek(fileData.ObjectListOffset, SeekOrigin.Begin);

                var r = new BinaryReader(source);
                
                for (var i = 0; i < fileData.ObjectCount; i++)
                {
                    position = source.Position;
                    bytes = r.ReadBytes(16);
                    objectSize = r.ReadUInt64();

                    // Language index (optional; one only -- useful to map language codes to extended header tag information)
                    if (StreamUtils.ArrEqualsArr(WMA_LANGUAGE_LIST_OBJECT_ID, bytes))
                    {
                        var nbLanguages = r.ReadUInt16();
                        Byte strLen;

                        for (var j = 0; j < nbLanguages; j++)
                        {
                            strLen = r.ReadByte();
                            var position2 = source.Position;
                            if (strLen > 2) languages.Add(Utils.StripEndingZeroChars(Encoding.Unicode.GetString(r.ReadBytes(strLen))));
                            source.Seek(position2 + strLen, SeekOrigin.Begin);
                        }
                    }

                    source.Seek(position + (Int64)objectSize, SeekOrigin.Begin);
                }

                source.Seek(initialPosition, SeekOrigin.Begin);
            }
        }

        private UInt16 encodeLanguage(Stream source, String languageCode)
        {
            if (null == languages) cacheLanguageIndex(source);

            if (0 == languages.Count)
            {
                return 0;
            } else
            {
                return (UInt16)languages.IndexOf(languageCode);
            }
        }

        private String decodeLanguage(Stream source, UInt16 languageIndex)
        {
            if (null == languages) cacheLanguageIndex(source);

            if (languages.Count > 0)
            {
                if (languageIndex < languages.Count)
                {
                    return languages[languageIndex];
                }
                else
                {
                    return languages[0]; // Index out of bounds
                }
            } else
            {
                return "";
            }
        }

		private void readContentDescription(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
		{
            var fieldSize = new UInt16[5];
			String fieldValue;

			// Read standard field sizes
			for (var i=0;i<5;i++) fieldSize[i] = source.ReadUInt16();

            // Read standard field values
            for (var i = 0; i < 5; i++)
            {
                if (fieldSize[i] > 0)
                {
                    // Read field value
                    fieldValue = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode);

                    // Set corresponding tag field if supported
                    switch (i)
                    {
                        case 0: SetMetaField("WM/TITLE", fieldValue, readTagParams.ReadAllMetaFrames, ZONE_CONTENT_DESCRIPTION); break;
                        case 1: SetMetaField("WM/AUTHOR", fieldValue, readTagParams.ReadAllMetaFrames, ZONE_CONTENT_DESCRIPTION); break;
                        case 2: SetMetaField("WM/COPYRIGHT", fieldValue, readTagParams.ReadAllMetaFrames, ZONE_CONTENT_DESCRIPTION); break;
                        case 3: SetMetaField("WM/DESCRIPTION", fieldValue, readTagParams.ReadAllMetaFrames, ZONE_CONTENT_DESCRIPTION); break;
                        case 4: SetMetaField("WM/RATING", fieldValue, readTagParams.ReadAllMetaFrames, ZONE_CONTENT_DESCRIPTION); break;
                    }
                }
            }
		}

        private void readHeaderExtended(BinaryReader source, Int64 sizePosition1, UInt64 size1, Int64 sizePosition2, UInt64 size2, MetaDataIO.ReadTagParams readTagParams)
        {
            Byte[] headerExtensionObjectId;
            UInt64 headerExtensionObjectSize = 0;
            Int64 position, framePosition, sizePosition3, dataPosition;
            UInt64 limit;
            UInt16 streamNumber, languageIndex;

            source.BaseStream.Seek(16, SeekOrigin.Current); // Reserved field 1
            source.BaseStream.Seek(2, SeekOrigin.Current); // Reserved field 2

            sizePosition3 = source.BaseStream.Position;
            var headerExtendedSize = source.ReadUInt32(); // Size of actual data

            // Looping through header extension objects
            position = source.BaseStream.Position;
            limit = (UInt64)position + headerExtendedSize;
            while ((UInt64)position < limit)
            {
                framePosition = source.BaseStream.Position;
                headerExtensionObjectId = source.ReadBytes(16);
                headerExtensionObjectSize = source.ReadUInt64();

                // Additional metadata (Optional frames)
                if (StreamUtils.ArrEqualsArr(WMA_METADATA_OBJECT_ID, headerExtensionObjectId) || StreamUtils.ArrEqualsArr(WMA_METADATA_LIBRARY_OBJECT_ID, headerExtensionObjectId))
                {
                    UInt16 nameSize;            // Length (in bytes) of Name field
                    UInt16 fieldDataType;       // Type of data stored in current field
                    Int32 fieldDataSize;          // Size of data stored in current field
                    String fieldName;           // Name of current field
                    var nbObjects = source.ReadUInt16();
                    var isLibraryObject = StreamUtils.ArrEqualsArr(WMA_METADATA_LIBRARY_OBJECT_ID, headerExtensionObjectId);

                    var zoneCode = isLibraryObject ? ZONE_EXTENDED_HEADER_METADATA_LIBRARY : ZONE_EXTENDED_HEADER_METADATA;

                    structureHelper.AddZone(framePosition, (Int32)headerExtensionObjectSize, zoneCode);
                    // Store frame information for future editing, since current frame is optional
                    if (readTagParams.PrepareForWriting)
                    {
                        structureHelper.AddSize(sizePosition1, size1, zoneCode);
                        structureHelper.AddSize(sizePosition2, size2, zoneCode);
                        structureHelper.AddSize(sizePosition3, headerExtendedSize, zoneCode);
                    }

                    for (var i = 0; i < nbObjects; i++)
                    {
                        languageIndex = source.ReadUInt16();
                        streamNumber = source.ReadUInt16();
                        nameSize = source.ReadUInt16();
                        fieldDataType = source.ReadUInt16();
                        fieldDataSize = source.ReadInt32();
                        fieldName = Utils.StripEndingZeroChars(Encoding.Unicode.GetString(source.ReadBytes(nameSize)));

                        dataPosition = source.BaseStream.Position;
                        readTagField(source, zoneCode, fieldName, fieldDataType, fieldDataSize, readTagParams, true, languageIndex, streamNumber);

                        source.BaseStream.Seek(dataPosition + fieldDataSize, SeekOrigin.Begin);
                    }
                }

                source.BaseStream.Seek(position + (Int64)headerExtensionObjectSize, SeekOrigin.Begin);
                position = source.BaseStream.Position;
            }

            // Add absent zone definitions for further editing
            if (readTagParams.PrepareForWriting)
            {
                if (!structureHelper.ZoneNames.Contains(ZONE_EXTENDED_HEADER_METADATA))
                {
                    structureHelper.AddZone(source.BaseStream.Position, 0, ZONE_EXTENDED_HEADER_METADATA);
                    structureHelper.AddSize(sizePosition1, size1, ZONE_EXTENDED_HEADER_METADATA);
                    structureHelper.AddSize(sizePosition2, size2, ZONE_EXTENDED_HEADER_METADATA);
                    structureHelper.AddSize(sizePosition3, headerExtendedSize, ZONE_EXTENDED_HEADER_METADATA);
                }
                if (!structureHelper.ZoneNames.Contains(ZONE_EXTENDED_HEADER_METADATA_LIBRARY))
                {
                    structureHelper.AddZone(source.BaseStream.Position, 0, ZONE_EXTENDED_HEADER_METADATA_LIBRARY);
                    structureHelper.AddSize(sizePosition1, size1, ZONE_EXTENDED_HEADER_METADATA_LIBRARY);
                    structureHelper.AddSize(sizePosition2, size2, ZONE_EXTENDED_HEADER_METADATA_LIBRARY);
                    structureHelper.AddSize(sizePosition3, headerExtendedSize, ZONE_EXTENDED_HEADER_METADATA_LIBRARY);
                }
            }
        }

        private void readExtendedContentDescription(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            Int64 dataPosition;
			UInt16 fieldCount;
			UInt16 dataSize;
			UInt16 dataType;
			String fieldName;

			// Read extended tag data
			fieldCount = source.ReadUInt16();
			for (var iterator1=0; iterator1 < fieldCount; iterator1++)
			{
				// Read field name
				dataSize = source.ReadUInt16();
				fieldName = Utils.StripEndingZeroChars(Encoding.Unicode.GetString(source.ReadBytes(dataSize)));
                // Read value data type
                dataType = source.ReadUInt16();
				dataSize = source.ReadUInt16();

                dataPosition = source.BaseStream.Position;
                readTagField(source, ZONE_EXTENDED_CONTENT_DESCRIPTION, fieldName, dataType, dataSize, readTagParams);

                source.BaseStream.Seek(dataPosition + dataSize, SeekOrigin.Begin);
            }
		}

        public void readTagField(BinaryReader source, String zoneCode, String fieldName, UInt16 fieldDataType, Int32 fieldDataSize, ReadTagParams readTagParams, Boolean isExtendedHeader = false, UInt16 languageIndex = 0, UInt16 streamNumber = 0)
        {
            var fieldValue = "";
            var setMeta = true;

            addFrameClass(fieldName, fieldDataType);

            if (0 == fieldDataType) // Unicode string
            {
                fieldValue = Utils.StripEndingZeroChars(Encoding.Unicode.GetString(source.ReadBytes(fieldDataSize)));
            }
            else if (1 == fieldDataType) // Byte array
            {
                if (fieldName.ToUpper().Equals("WM/PICTURE"))
                {
                    var picCode = source.ReadByte();
                    // TODO factorize : abstract PictureTypeDecoder + unsupported / supported decision in MetaDataIO ? 
                    var picType = ID3v2.DecodeID3v2PictureType(picCode);

                    Int32 picturePosition;
                    if (picType.Equals(PictureInfo.PIC_TYPE.Unsupported))
                    {
                        addPictureToken(MetaDataIOFactory.TAG_NATIVE, picCode);
                        picturePosition = takePicturePosition(MetaDataIOFactory.TAG_NATIVE, picCode);
                    }
                    else
                    {
                        addPictureToken(picType);
                        picturePosition = takePicturePosition(picType);
                    }

                    if (readTagParams.ReadPictures || readTagParams.PictureStreamHandler != null)
                    {
                        var picSize = source.ReadInt32();
                        var mimeType = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode);
                        var description = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode);

                        var picInfo = new PictureInfo(ImageUtils.GetImageFormatFromMimeType(mimeType), picType, getImplementedTagType(), picCode, picturePosition);
                        picInfo.Description = description;
                        picInfo.PictureData = new Byte[picSize];
                        source.BaseStream.Read(picInfo.PictureData, 0, picSize);

                        tagData.Pictures.Add(picInfo);

                        if (readTagParams.PictureStreamHandler != null)
                        {
                            var mem = new MemoryStream(picInfo.PictureData);
                            readTagParams.PictureStreamHandler(ref mem, picInfo.PicType, picInfo.NativeFormat, picInfo.TagType, picInfo.NativePicCode, picInfo.Position);
                            mem.Close();
                        }
                    }
                    setMeta = false;
                }
                else
                {
                    source.BaseStream.Seek(fieldDataSize, SeekOrigin.Current);
                }
            }
            else if (2 == fieldDataType) // 16-bit Boolean (metadata); 32-bit Boolean (extended header)
            {
                if (isExtendedHeader) fieldValue = source.ReadUInt32().ToString();
                else fieldValue = source.ReadUInt16().ToString();
            }
            else if (3 == fieldDataType) // 32-bit unsigned integer
            {
                var intValue = source.ReadUInt32();
                if (fieldName.Equals("WM/GENRE",StringComparison.OrdinalIgnoreCase)) intValue++;
                fieldValue = intValue.ToString();
            }
            else if (4 == fieldDataType) // 64-bit unsigned integer
            {
                fieldValue = source.ReadUInt64().ToString();
            }
            else if (5 == fieldDataType) // 16-bit unsigned integer
            {
                fieldValue = source.ReadUInt16().ToString();
            }
            else if (6 == fieldDataType) // 128-bit GUID; unused for now
            {
                source.BaseStream.Seek(fieldDataSize, SeekOrigin.Current);
            }

            if (setMeta) SetMetaField(fieldName.Trim(), fieldValue, readTagParams.ReadAllMetaFrames, zoneCode, 0, streamNumber, decodeLanguage(source.BaseStream,languageIndex));
        }

		private Boolean readData(BinaryReader source, ReadTagParams readTagParams)
        {
            var fs = source.BaseStream;

            Byte[] ID;
			UInt32 objectCount;
            UInt64 headerSize, objectSize;
			Int64 initialPos, position;
            Int64 countPosition, sizePosition1, sizePosition2;
            var result = false;

            if (languages != null) languages.Clear();

            fs.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            initialPos = fs.Position;

            // Check for existing header
            ID = source.ReadBytes(16);

            // Header (mandatory; one only)
			if ( StreamUtils.ArrEqualsArr(WMA_HEADER_ID,ID) )
			{
                sizePosition1 = fs.Position;
                headerSize = source.ReadUInt64();
                countPosition = fs.Position;
                objectCount = source.ReadUInt32();		  
				fs.Seek(2, SeekOrigin.Current); // Reserved data
                fileData.ObjectCount = objectCount;
                fileData.ObjectListOffset = fs.Position;

				// Read all objects in header and get needed data
				for (var i=0; i<objectCount; i++)
				{
					position = fs.Position;
                    ID = source.ReadBytes(16);
                    sizePosition2 = fs.Position;
                    objectSize = source.ReadUInt64();

                    // File properties (mandatory; one only)
                    if (StreamUtils.ArrEqualsArr(WMA_FILE_PROPERTIES_ID, ID))
                    {
                        source.BaseStream.Seek(40, SeekOrigin.Current);
                        duration = source.ReadUInt64() / 10000.0;       // Play duration (100-nanoseconds)
                        source.BaseStream.Seek(8, SeekOrigin.Current);  // Send duration; unused for now
                        duration -= source.ReadUInt64();                // Preroll duration (ms)
                    }
                    // Stream properties (mandatory; one per stream)
                    else if (StreamUtils.ArrEqualsArr(WMA_STREAM_PROPERTIES_ID, ID))
                    {
                        source.BaseStream.Seek(54, SeekOrigin.Current);
                        fileData.FormatTag = source.ReadUInt16();
                        fileData.Channels = source.ReadUInt16();
                        fileData.SampleRate = source.ReadInt32();
                    }
                    // Content description (optional; one only)
                    // -> standard, pre-defined metadata
                    else if (StreamUtils.ArrEqualsArr(WMA_CONTENT_DESCRIPTION_ID, ID) && readTagParams.ReadTag)
                    {
                        tagExists = true;
                        structureHelper.AddZone(position, (Int32)objectSize, ZONE_CONTENT_DESCRIPTION);
                        // Store frame information for future editing, since current frame is optional
                        if (readTagParams.PrepareForWriting)
                        {
                            structureHelper.AddSize(sizePosition1, headerSize, ZONE_CONTENT_DESCRIPTION);
                            structureHelper.AddCounter(countPosition, objectCount, ZONE_CONTENT_DESCRIPTION);
                        }
                        readContentDescription(source, readTagParams);
                    }
                    // Extended content description (optional; one only)
                    // -> extended, dynamic metadata
                    else if (StreamUtils.ArrEqualsArr(WMA_EXTENDED_CONTENT_DESCRIPTION_ID, ID) && readTagParams.ReadTag)
                    {
                        tagExists = true;
                        structureHelper.AddZone(position, (Int32)objectSize, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                        // Store frame information for future editing, since current frame is optional
                        if (readTagParams.PrepareForWriting)
                        {
                            structureHelper.AddSize(sizePosition1, headerSize, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                            structureHelper.AddCounter(countPosition, objectCount, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                        }
                        readExtendedContentDescription(source, readTagParams);
                    }
                    // Header extension (mandatory; one only)
                    // -> extended, dynamic additional metadata such as additional embedded pictures (any picture after the 1st one stored in extended content)
                    else if (StreamUtils.ArrEqualsArr(WMA_HEADER_EXTENSION_ID, ID) && readTagParams.ReadTag)
                    {
                        readHeaderExtended(source, sizePosition1, headerSize, sizePosition2, objectSize, readTagParams);
                    }

                    fs.Seek(position + (Int64)objectSize, SeekOrigin.Begin);				
				}

                // Add absent zone definitions for further editing
                if (readTagParams.PrepareForWriting)
                {
                    if (!structureHelper.ZoneNames.Contains(ZONE_CONTENT_DESCRIPTION))
                    {
                        structureHelper.AddZone(fs.Position, 0, ZONE_CONTENT_DESCRIPTION);
                        structureHelper.AddSize(sizePosition1, headerSize, ZONE_CONTENT_DESCRIPTION);
                        structureHelper.AddCounter(countPosition, objectCount, ZONE_CONTENT_DESCRIPTION);
                    }
                    if (!structureHelper.ZoneNames.Contains(ZONE_EXTENDED_CONTENT_DESCRIPTION))
                    {
                        structureHelper.AddZone(fs.Position, 0, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                        structureHelper.AddSize(sizePosition1, headerSize, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                        structureHelper.AddCounter(countPosition, objectCount, ZONE_EXTENDED_CONTENT_DESCRIPTION);
                    }
                }

                result = true;
			}

            fileData.HeaderSize = fs.Position - initialPos;

            return result;
		}

		private Boolean isValid(FileData Data)
		{
			// Check for data validity
			return (
				((Data.Channels == WMA_CM_MONO) || (Data.Channels == WMA_CM_STEREO))
				&& (Data.SampleRate >= 8000) && (Data.SampleRate <= 96000)
                );
		}

		private String getChannelMode()
		{
			// Get channel mode name
			return WMA_MODE[channelModeID];
		}

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            fileData = new FileData();

            resetData();
            var result = readData(source, readTagParams);

			// Process data if loaded and valid
			if ( result && isValid(fileData) )
			{
				channelModeID = (Byte)fileData.Channels;
				sampleRate = fileData.SampleRate;
                bitrate = (sizeInfo.FileSize - sizeInfo.TotalTagSize - fileData.HeaderSize) * 8.0 / duration;
                isVBR = (WMA_GSM_VBR_ID == fileData.FormatTag);
                isLossless = (WMA_LOSSLESS_ID == fileData.FormatTag);
            }

            return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            if (ZONE_CONTENT_DESCRIPTION.Equals(zone)) return writeContentDescription(tag, w);
            else if (ZONE_EXTENDED_HEADER_METADATA.Equals(zone)) return writeExtendedHeaderMeta(tag, w);
            else if (ZONE_EXTENDED_HEADER_METADATA_LIBRARY.Equals(zone)) return writeExtendedHeaderMetaLibrary(tag, w);
            else if (ZONE_EXTENDED_CONTENT_DESCRIPTION.Equals(zone)) return writeExtendedContentDescription(tag, w);
            else return 0;
        }

        private Int32 writeContentDescription(TagData tag, BinaryWriter w)
        {
            Int64 beginPos, frameSizePos, finalFramePos;

            beginPos = w.BaseStream.Position;
            w.Write(WMA_CONTENT_DESCRIPTION_ID);
            frameSizePos = w.BaseStream.Position;
            w.Write((UInt64)0); // Frame size placeholder to be rewritten at the end of the method

            var title = "";
            var author = "";
            var copyright = "";
            var comment = "";
            var rating = ""; // TODO - check if it really should be a string

            var map = tag.ToMap();

            // Supported textual fields
            foreach (var frameType in map.Keys)
            {
                if (map[frameType].Length > 0) // No frame with empty value
                {
                    if (TagData.TAG_FIELD_TITLE.Equals(frameType)) title = map[frameType];
                    else if (TagData.TAG_FIELD_ARTIST.Equals(frameType)) author = map[frameType];
                    else if (TagData.TAG_FIELD_COPYRIGHT.Equals(frameType)) copyright = map[frameType];
                    else if (TagData.TAG_FIELD_COMMENT.Equals(frameType)) comment = map[frameType];
                    else if (TagData.TAG_FIELD_RATING.Equals(frameType)) rating = map[frameType];
                }
            }

            // Read standard field sizes (+1 for last null characher; x2 for unicode)
            if (title.Length > 0) w.Write((UInt16)((title.Length + 1) * 2)); else w.Write((UInt16)0);
            if (author.Length > 0) w.Write((UInt16)((author.Length + 1) * 2)); else w.Write((UInt16)0);
            if (copyright.Length > 0) w.Write((UInt16)((copyright.Length + 1) * 2)); else w.Write((UInt16)0);
            if (comment.Length > 0) w.Write((UInt16)((comment.Length + 1) * 2)); else w.Write((UInt16)0);
            if (rating.Length > 0) w.Write((UInt16)((rating.Length + 1) * 2)); else w.Write((UInt16)0);

            if (title.Length > 0) w.Write(Encoding.Unicode.GetBytes(title + '\0'));
            if (author.Length > 0) w.Write(Encoding.Unicode.GetBytes(author + '\0'));
            if (copyright.Length > 0) w.Write(Encoding.Unicode.GetBytes(copyright + '\0'));
            if (comment.Length > 0) w.Write(Encoding.Unicode.GetBytes(comment + '\0'));
            if (rating.Length > 0) w.Write(Encoding.Unicode.GetBytes(rating + '\0'));

            // Go back to frame size locations to write their actual size 
            finalFramePos = w.BaseStream.Position;
            w.BaseStream.Seek(frameSizePos, SeekOrigin.Begin);
            w.Write(Convert.ToUInt64(finalFramePos - beginPos));
            w.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);

            return ((title.Length > 0)?1:0) + ((author.Length > 0)?1:0) + ((copyright.Length > 0)?1:0) + ((comment.Length > 0)?1:0) + ((rating.Length > 0)?1:0);
        }

        private Int32 writeExtendedContentDescription(TagData tag, BinaryWriter w)
        {
            Int64 beginPos, frameSizePos, counterPos, finalFramePos;
            UInt16 counter = 0;
            Boolean doWritePicture;

            beginPos = w.BaseStream.Position;
            w.Write(WMA_EXTENDED_CONTENT_DESCRIPTION_ID);
            frameSizePos = w.BaseStream.Position;
            w.Write((UInt64)0); // Frame size placeholder to be rewritten at the end of the method
            counterPos = w.BaseStream.Position;
            w.Write((UInt16)0); // Counter placeholder to be rewritten at the end of the method

            var map = tag.ToMap();

            // Supported textual fields
            foreach (var frameType in map.Keys)
            {
                foreach (var s in frameMapping.Keys)
                {
                    if (!embeddedFields.Contains(s) && frameType == frameMapping[s])
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
                if ((fieldInfo.TagType.Equals(MetaDataIOFactory.TAG_ANY) || fieldInfo.TagType.Equals(getImplementedTagType())) && !fieldInfo.MarkedForDeletion && (ZONE_EXTENDED_CONTENT_DESCRIPTION.Equals(fieldInfo.Zone) || "".Equals(fieldInfo.Zone)) )
                {
                    writeTextFrame(w, fieldInfo.NativeFieldCode, fieldInfo.Value);
                    counter++;
                }
            }

            // Picture fields
            foreach (var picInfo in tag.Pictures)
            {
                // Picture has either to be supported, or to come from the right tag standard
                doWritePicture = !picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported);
                if (!doWritePicture) doWritePicture = (getImplementedTagType() == picInfo.TagType);
                // It also has not to be marked for deletion
                doWritePicture = doWritePicture && (!picInfo.MarkedForDeletion);

                if (doWritePicture && picInfo.PictureData.Length + 50 <= UInt16.MaxValue)
                {
                    writePictureFrame(w, picInfo.PictureData, picInfo.NativeFormat, ImageUtils.GetMimeTypeFromImageFormat(picInfo.NativeFormat), picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported) ? (Byte)picInfo.NativePicCode : ID3v2.EncodeID3v2PictureType(picInfo.PicType), picInfo.Description);
                    counter++;
                }
            }


            // Go back to frame size locations to write their actual size 
            finalFramePos = w.BaseStream.Position;
            w.BaseStream.Seek(frameSizePos, SeekOrigin.Begin);
            w.Write(Convert.ToUInt64(finalFramePos - beginPos));
            w.BaseStream.Seek(counterPos, SeekOrigin.Begin);
            w.Write(counter);
            w.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);

            return counter;
        }

        private Int32 writeExtendedHeaderMeta(TagData tag, BinaryWriter w)
        {
            Int64 beginPos, frameSizePos, counterPos, finalFramePos;
            UInt16 counter;

            beginPos = w.BaseStream.Position;
            w.Write(WMA_METADATA_OBJECT_ID);
            frameSizePos = w.BaseStream.Position;
            w.Write((UInt64)0); // Frame size placeholder to be rewritten at the end of the method
            counterPos = w.BaseStream.Position;
            w.Write((UInt16)0); // Counter placeholder to be rewritten at the end of the method

            counter = writeExtendedMeta(tag, w);

            // Go back to frame size locations to write their actual size 
            finalFramePos = w.BaseStream.Position;
            w.BaseStream.Seek(frameSizePos, SeekOrigin.Begin);
            w.Write(Convert.ToUInt64(finalFramePos - beginPos));
            w.BaseStream.Seek(counterPos, SeekOrigin.Begin);
            w.Write(counter);
            w.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);

            return counter;
        }

        private Int32 writeExtendedHeaderMetaLibrary(TagData tag, BinaryWriter w)
        {
            Int64 beginPos, frameSizePos, counterPos, finalFramePos;
            UInt16 counter;

            beginPos = w.BaseStream.Position;
            w.Write(WMA_METADATA_LIBRARY_OBJECT_ID);
            frameSizePos = w.BaseStream.Position;
            w.Write((UInt64)0); // Frame size placeholder to be rewritten at the end of the method
            counterPos = w.BaseStream.Position;
            w.Write((UInt16)0); // Counter placeholder to be rewritten at the end of the method

            counter = writeExtendedMeta(tag, w, true);

            // Go back to frame size locations to write their actual size 
            finalFramePos = w.BaseStream.Position;
            w.BaseStream.Seek(frameSizePos, SeekOrigin.Begin);
            w.Write(Convert.ToUInt64(finalFramePos - beginPos));
            w.BaseStream.Seek(counterPos, SeekOrigin.Begin);
            w.Write(counter);
            w.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);

            return counter;
        }

        private UInt16 writeExtendedMeta(TagData tag, BinaryWriter w, Boolean isExtendedMetaLibrary=false)
        {
            Boolean doWritePicture;
            UInt16 counter = 0;

            var map = tag.ToMap();

            // Supported textual fields : all current supported fields are located in extended content description frame

            // Other textual fields
            foreach (var fieldInfo in tag.AdditionalFields)
            {
                if ((fieldInfo.TagType.Equals(MetaDataIOFactory.TAG_ANY) || fieldInfo.TagType.Equals(getImplementedTagType())) && !fieldInfo.MarkedForDeletion)
                {
                    if ( (ZONE_EXTENDED_HEADER_METADATA.Equals(fieldInfo.Zone) && !isExtendedMetaLibrary) || (ZONE_EXTENDED_HEADER_METADATA_LIBRARY.Equals(fieldInfo.Zone) && isExtendedMetaLibrary) )
                    {
                        writeTextFrame(w, fieldInfo.NativeFieldCode, fieldInfo.Value, true, encodeLanguage(w.BaseStream, fieldInfo.Language), fieldInfo.StreamNumber);
                        counter++;
                    }
                }
            }

            // Picture fields (exclusively written in Metadata Library Object zone)
            if (isExtendedMetaLibrary)
            {
                foreach (var picInfo in tag.Pictures)
                {
                    // Picture has either to be supported, or to come from the right tag standard
                    doWritePicture = !picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported);
                    if (!doWritePicture) doWritePicture = (getImplementedTagType() == picInfo.TagType);
                    // It also has not to be marked for deletion
                    doWritePicture = doWritePicture && (!picInfo.MarkedForDeletion);

                    if (doWritePicture && picInfo.PictureData.Length + 50 > UInt16.MaxValue)
                    {
                        writePictureFrame(w, picInfo.PictureData, picInfo.NativeFormat, ImageUtils.GetMimeTypeFromImageFormat(picInfo.NativeFormat), picInfo.PicType.Equals(PictureInfo.PIC_TYPE.Unsupported) ? (Byte)picInfo.NativePicCode : ID3v2.EncodeID3v2PictureType(picInfo.PicType), picInfo.Description, true);
                        counter++;
                    }
                }
            }

            return counter;
        }

        private void writeTextFrame(BinaryWriter writer, String frameCode, String text, Boolean isExtendedHeader=false, UInt16 languageIndex = 0, UInt16 streamNumber = 0)
        {
            Int64 dataSizePos, dataPos, finalFramePos;
            var nameBytes = Encoding.Unicode.GetBytes(frameCode + '\0');
            var nameSize = (UInt16)nameBytes.Length;

            if (isExtendedHeader)
            {
                writer.Write(languageIndex); // Metadata object : Reserved / Metadata library object : Language list index
                writer.Write(streamNumber); // Corresponding stream number
            }

            // Name length and name
            writer.Write(nameSize);
            if (!isExtendedHeader) writer.Write(nameBytes);

            UInt16 frameClass = 0;
            if (frameClasses.ContainsKey(frameCode)) frameClass = frameClasses[frameCode];
            writer.Write(frameClass);

            dataSizePos = writer.BaseStream.Position;
            // Data size placeholder to be rewritten in a few lines
            if (isExtendedHeader)
            {
                writer.Write((UInt32)0);
            }
            else
            {
                writer.Write((UInt16)0);
            }

            if (isExtendedHeader) writer.Write(nameBytes);

            dataPos = writer.BaseStream.Position;
            if (0 == frameClass) // Unicode string
            {
                writer.Write(Encoding.Unicode.GetBytes(text + '\0'));
            }
            else if (1 == frameClass) // Byte array
            {
                // Only used for embedded pictures
            }
            else if (2 == frameClass) // 32-bit boolean; 16-bit boolean if in extended header
            {
                if (isExtendedHeader) writer.Write(Utils.ToBoolean(text)?(UInt16)1:(UInt16)0);
                else writer.Write(Utils.ToBoolean(text) ? (UInt32)1 : (UInt32)0);
            }
            else if (3 == frameClass) // 32-bit unsigned integer
            {
                writer.Write(Convert.ToUInt32(text));
            }
            else if (4 == frameClass) // 64-bit unsigned integer
            {
                writer.Write(Convert.ToUInt64(text));
            }
            else if (5 == frameClass) // 16-bit unsigned integer
            {
                writer.Write(Convert.ToUInt16(text));
            }
            else if (6 == frameClass) // 128-bit GUID
            {
                // Unused for now
            }

            // Go back to frame size locations to write their actual size 
            finalFramePos = writer.BaseStream.Position;
            writer.BaseStream.Seek(dataSizePos, SeekOrigin.Begin);
            if (!isExtendedHeader)
            {
                writer.Write(Convert.ToUInt16(finalFramePos - dataPos));
            } else
            {
                writer.Write(Convert.ToUInt32(finalFramePos - dataPos));
            }
            writer.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);
        }

        private void writePictureFrame(BinaryWriter writer, Byte[] pictureData, ImageFormat picFormat, String mimeType, Byte pictureTypeCode, String description, Boolean isExtendedHeader = false, UInt16 languageIndex = 0, UInt16 streamNumber = 0)
        {
            Int64 dataSizePos, dataPos, finalFramePos;
            var nameBytes = Encoding.Unicode.GetBytes("WM/Picture" + '\0');
            var nameSize = (UInt16)nameBytes.Length;

            if (isExtendedHeader)
            {
                writer.Write(languageIndex); // Metadata object : Reserved / Metadata library object : Language list index
                writer.Write(streamNumber); // Corresponding stream number
            }

            // Name length and name
            writer.Write(nameSize);
            if (!isExtendedHeader) writer.Write(nameBytes);

            UInt16 frameClass = 1;
            writer.Write(frameClass);

            dataSizePos = writer.BaseStream.Position;
            // Data size placeholder to be rewritten in a few lines
            if (isExtendedHeader)
            {
                writer.Write((UInt32)0);
            }
            else
            {
                writer.Write((UInt16)0);
            }

            if (isExtendedHeader)
            {
                writer.Write(nameBytes);
            }
            dataPos = writer.BaseStream.Position;

            writer.Write(pictureTypeCode);
            writer.Write(pictureData.Length);

            writer.Write(Encoding.Unicode.GetBytes(mimeType+'\0'));
            writer.Write(Encoding.Unicode.GetBytes(description + '\0'));     // Picture description

            writer.Write(pictureData);

            // Go back to frame size locations to write their actual size 
            finalFramePos = writer.BaseStream.Position;
            writer.BaseStream.Seek(dataSizePos, SeekOrigin.Begin);
            if (isExtendedHeader)
            {
                writer.Write(Convert.ToUInt32(finalFramePos-dataPos));
            }
            else
            {
                writer.Write(Convert.ToUInt16(finalFramePos-dataPos));
            }
            writer.BaseStream.Seek(finalFramePos, SeekOrigin.Begin);
        }

        // Specific implementation for conservation of non-WM/xxx fields
        public override Boolean Remove(BinaryWriter w)
        {
            if (Settings.ASF_keepNonWMFieldsWhenRemovingTag)
            {
                var tag = new TagData();

                foreach (var b in frameMapping.Values)
                {
                    tag.IntegrateValue(b, "");
                }

                foreach (var fieldInfo in GetAdditionalFields())
                {
                    if (fieldInfo.NativeFieldCode.ToUpper().StartsWith("WM/"))
                    {
                        var emptyFieldInfo = new MetaFieldInfo(fieldInfo);
                        emptyFieldInfo.MarkedForDeletion = true;
                        tag.AdditionalFields.Add(emptyFieldInfo);
                    }
                }

                var r = new BinaryReader(w.BaseStream);
                return Write(r, w, tag);
            } else
            {
                return base.Remove(w);
            }
        }
    }
}