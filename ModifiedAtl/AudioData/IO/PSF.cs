using System;
using System.IO;
using ATL.Logging;
using System.Collections.Generic;
using static ATL.AudioData.AudioDataManager;
using Commons;
using System.Text;

namespace ATL.AudioData.IO
{
	/// <summary>
	/// Class for Portable Sound Format files manipulation (extensions : .PSF, .PSF1, .PSF2, 
    /// .MINIPSF, .MINIPSF1, .MINIPSF2, .SSF, .MINISSF, .DSF, .MINIDSF, .GSF, .MINIGSF, .QSF, .MINISQF)
    /// According to Neil Corlett's specifications v. 1.6
	/// </summary>
	class PSF : MetaDataIO, IAudioDataIO
	{
		// Format Type Names
		public const String PSF_FORMAT_UNKNOWN = "Unknown";
		public const String PSF_FORMAT_PSF1 = "Playstation";
		public const String PSF_FORMAT_PSF2 = "Playstation 2";
		public const String PSF_FORMAT_SSF = "Saturn";
		public const String PSF_FORMAT_DSF = "Dreamcast";
		public const String PSF_FORMAT_USF = "Nintendo 64";
		public const String PSF_FORMAT_QSF = "Capcom QSound";

		// Tag predefined fields
		public const String TAG_LENGTH = "length";
		public const String TAG_FADE = "fade";

		private const String PSF_FORMAT_TAG = "PSF";
		private const String TAG_HEADER = "[TAG]";
		private const UInt32 HEADER_LENGTH = 16;

        private const Byte LINE_FEED = 0x0A;
        private const Byte SPACE = 0x20;

        private const Int32 PSF_DEFAULT_DURATION = 180000; // 3 minutes

        private Int32 sampleRate;
        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private readonly String filePath;

        private static IDictionary<String, Byte> frameMapping; // Mapping between PSF frame codes and ModifiedAtl frame codes
        private static IList<String> playbackFrames; // Frames that are required for playback


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // AudioDataIO
        public Int32 SampleRate // Sample rate (hz)
	        =>
		        this.sampleRate;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfSeqWav;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE);
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
        protected override Byte getFrameMapping(String zone, String ID, Byte tagVersion)
        {
            Byte supportedMetaId = 255;

            // Finds the ModifiedAtl field identifier according to the ID3v2 version
            if (frameMapping.ContainsKey(ID.ToLower())) supportedMetaId = frameMapping[ID.ToLower()];

            return supportedMetaId;
        }


        // === PRIVATE STRUCTURES/SUBCLASSES ===

        private class PSFHeader
		{
			public String FormatTag;					// Format tag (should be PSF_FORMAT_TAG)
			public Byte VersionByte;					// Version mark
			public UInt32 ReservedAreaLength;				// Length of reserved area (bytes)
			public UInt32 CompressedProgramLength;		// Length of compressed program (bytes)

			public void Reset()
			{
				FormatTag = "";
				VersionByte = 0;
				ReservedAreaLength = 0;
				CompressedProgramLength = 0;
			}
		}

		private class PSFTag
		{
			public String TagHeader;					// Tag header (should be TAG_HEADER)
            public Int32 size;

			public void Reset()
			{
				TagHeader = "";
                size = 0;
			}
		}


        // ---------- CONSTRUCTORS & INITIALIZERS

        static PSF()
        {
            frameMapping = new Dictionary<String, Byte>
            {
                { "title", TagData.TAG_FIELD_TITLE },
                { "game", TagData.TAG_FIELD_ALBUM }, // Small innocent semantic shortcut
                { "artist", TagData.TAG_FIELD_ARTIST },
                { "copyright", TagData.TAG_FIELD_COPYRIGHT },
                { "comment", TagData.TAG_FIELD_COMMENT },
                { "year", TagData.TAG_FIELD_RECORDING_YEAR },
                { "genre", TagData.TAG_FIELD_GENRE },
                { "rating", TagData.TAG_FIELD_RATING } // Does not belong to the predefined standard PSF tags
            };

            playbackFrames = new List<String>
            {
                "volume",
                "length",
                "fade",
                "filedir",
                "filename",
                "fileext"
            };
        }

        private void resetData()
        {
            sampleRate = 44100; // Seems to be de facto value for all PSF files, even though spec doesn't say anything about it
            bitrate = 0;
            duration = 0;

            ResetData();
        }

        public PSF(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // ---------- SUPPORT METHODS

        private Boolean readHeader(BinaryReader source, ref PSFHeader header)
		{
            header.FormatTag = Utils.Latin1Encoding.GetString(source.ReadBytes(3));
			if (PSF_FORMAT_TAG == header.FormatTag)
			{
				header.VersionByte = source.ReadByte();
				header.ReservedAreaLength = source.ReadUInt32();
				header.CompressedProgramLength = source.ReadUInt32();
				return true;
			}
			else
			{
				return false;
			}
		}

        private String readPSFLine(Stream source, Encoding encoding)
		{
            var lineStart = source.Position;
            Int64 lineEnd;
            var hasEOL = true;

            if (StreamUtils.FindSequence(source, new Byte[1] { LINE_FEED }))
            {
                lineEnd = source.Position;
            }
            else
            {
                lineEnd = source.Length;
                hasEOL = false;
            }

            source.Seek(lineStart, SeekOrigin.Begin);

            var data = new Byte[lineEnd - lineStart];
            source.Read(data, 0, data.Length);

            for (var i=0;i<data.Length; i++) if (data[i] < SPACE) data[i] = SPACE; // According to spec : "All characters 0x01-0x20 are considered whitespace"

            return encoding.GetString(data,0,data.Length - (hasEOL?1:0) ).Trim(); // -1 because we don't want to include LINE_FEED in the result
		}

        private Boolean readTag(BinaryReader source, ref PSFTag tag, ReadTagParams readTagParams)
		{
            var initialPosition = source.BaseStream.Position;
            var encoding = Utils.Latin1Encoding;

            tag.TagHeader = Utils.Latin1Encoding.GetString(source.ReadBytes(5));
			if (TAG_HEADER == tag.TagHeader)
			{
				var s = readPSFLine(source.BaseStream, encoding);
				
				Int32 equalIndex;
                String keyStr, valueStr, lowKeyStr;
                var lastKey = "";
                var lastValue = "";
                var lengthFieldFound = false;

				while ( s != "" )
				{
					equalIndex = s.IndexOf("=");
					if (equalIndex != -1)
					{
						keyStr = s.Substring(0,equalIndex).Trim();
                        lowKeyStr = keyStr.ToLower();
						valueStr = s.Substring(equalIndex+1, s.Length-(equalIndex+1)).Trim();

                        if (lowKeyStr.Equals("utf8") && valueStr.Equals("1")) encoding = Encoding.UTF8;

                        if (lowKeyStr.Equals(TAG_LENGTH) || lowKeyStr.Equals(TAG_FADE))
                        {
                            if (lowKeyStr.Equals(TAG_LENGTH)) lengthFieldFound = true;
                            duration += parsePSFDuration(valueStr);
                        }

                        // PSF specifics : a field appearing more than once is the same field, with values spanning over multiple lines
                        if (lastKey.Equals(keyStr))
                        {
                            lastValue +=  Environment.NewLine + valueStr;
                        }
                        else
                        {
                            SetMetaField(lastKey, lastValue, readTagParams.ReadAllMetaFrames);
                            lastValue = valueStr;
                        }
                        lastKey = keyStr;
					}

					s = readPSFLine(source.BaseStream, encoding);
				} // Metadata lines 
                SetMetaField(lastKey, lastValue, readTagParams.ReadAllMetaFrames);

                // PSF files without any 'length' tag take default duration, regardless of 'fade' value
                if (!lengthFieldFound) duration = PSF_DEFAULT_DURATION;

                tag.size = (Int32)(source.BaseStream.Position - initialPosition);
                if (readTagParams.PrepareForWriting)
                {
                    structureHelper.AddZone(initialPosition, tag.size);
                }
				return true;
			}
			else
			{
				return false;
			}
		}

		private Double parsePSFDuration(String durationStr)
		{
			var hStr = "";
			var mStr = "";
			var sStr = "";
			var dStr = "";
			Double result = 0;

			Int32 sepIndex;

			// decimal
			sepIndex = durationStr.LastIndexOf(".");
			if (-1 == sepIndex) sepIndex = durationStr.LastIndexOf(",");

			if (-1 != sepIndex)
			{
				sepIndex++;
				dStr = durationStr.Substring(sepIndex,durationStr.Length-sepIndex);
				durationStr = durationStr.Substring(0,Math.Max(0,sepIndex-1));
			}

			
			// seconds
			sepIndex = durationStr.LastIndexOf(":");

			sepIndex++;
			sStr = durationStr.Substring(sepIndex,durationStr.Length-sepIndex);
            //if (1 == sStr.Length) sStr = sStr + "0"; // "2:2" means 2:20 and not 2:02

			durationStr = durationStr.Substring(0,Math.Max(0,sepIndex-1));

			// minutes
			if (durationStr.Length > 0)
			{
				sepIndex = durationStr.LastIndexOf(":");
				
				sepIndex++;
				mStr = durationStr.Substring(sepIndex,durationStr.Length-sepIndex);

				durationStr = durationStr.Substring(0,Math.Max(0,sepIndex-1));
			}

			// hours
			if (durationStr.Length > 0)
			{
				sepIndex = durationStr.LastIndexOf(":");
				
				sepIndex++;
				hStr = durationStr.Substring(sepIndex,durationStr.Length-sepIndex);
			}

			if (dStr != "") result = result + (Int32.Parse(dStr) * 100);
			if (sStr != "") result = result + (Int32.Parse(sStr) * 1000);
			if (mStr != "") result = result + (Int32.Parse(mStr) * 60000);
			if (hStr != "") result = result + (Int32.Parse(hStr) * 3600000);
			
			return result;
		}

        // === PUBLIC METHODS ===

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        protected override Boolean read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            var result = true;
			var header = new PSFHeader();
			var tag = new PSFTag();

            header.Reset();
            tag.Reset();
            resetData();

            isValid = readHeader(source, ref header);
			if ( !isValid ) throw new Exception("Not a PSF file");

			if (source.BaseStream.Length > HEADER_LENGTH+header.CompressedProgramLength+header.ReservedAreaLength)
			{
				source.BaseStream.Seek((Int64)(4+header.CompressedProgramLength+header.ReservedAreaLength),SeekOrigin.Current);

                if (!readTag(source, ref tag, readTagParams)) throw new Exception("Not a PSF tag");

                tagExists = true;
			} 

            bitrate = (sizeInfo.FileSize-tag.size)* 8 / duration;

			return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            w.Write(Utils.Latin1Encoding.GetBytes(TAG_HEADER));

            // Announce UTF-8 support
            w.Write(Utils.Latin1Encoding.GetBytes("utf8=1"));
            w.Write(LINE_FEED);

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
                if ((fieldInfo.TagType.Equals(MetaDataIOFactory.TAG_ANY) || fieldInfo.TagType.Equals(getImplementedTagType())) && !fieldInfo.MarkedForDeletion && !fieldInfo.NativeFieldCode.Equals("utf8")) // utf8 already written
                {
                    writeTextFrame(w, fieldInfo.NativeFieldCode, fieldInfo.Value);
                    result++;
                }
            }

            // Remove the last end-of-line character
            w.BaseStream.SetLength(w.BaseStream.Length - 1);

            return result;
        }

        private void writeTextFrame(BinaryWriter writer, String frameCode, String text)
        {
            String[] textLines;
            if (text.Contains(Environment.NewLine))
            {
                // Split a multiple-line value into multiple frames with the same code
                textLines = text.Split(Environment.NewLine.ToCharArray());
            }
            else
            {
                textLines = new String[1] { text };
            }

            foreach (var s in textLines)
            {
                writer.Write(Utils.Latin1Encoding.GetBytes(frameCode));
                writer.Write('=');
                writer.Write(Encoding.UTF8.GetBytes(s));
                writer.Write(LINE_FEED);
            }
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
                if (!fieldCode.StartsWith("_") && !playbackFrames.Contains(fieldCode) )
                {
                    var emptyFieldInfo = new MetaFieldInfo(fieldInfo);
                    emptyFieldInfo.MarkedForDeletion = true;
                    tag.AdditionalFields.Add(emptyFieldInfo);
                }
            }

            w.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);
            var r = new BinaryReader(w.BaseStream);
            return Write(r, w, tag);
        }

    }
}
