using System;
using System.IO;
using ATL.Logging;
using static ATL.AudioData.AudioDataManager;
using Commons;
using System.Text;
using System.IO.Compression;

namespace ATL.AudioData.IO
{
	/// <summary>
    /// Class for Video Game Music files (Master System, Game Gear, SG1000, Genesis) manipulation (extensions : .VGM)
    /// According to file format v1.70
    /// 
    /// Implementation notes :
    ///   1/ GD3 tag format is directly implemented in here, since it is not a "real" standard and is only used for VGM files
    ///   
    ///   2/ Gzipped files are currently supported in read-only mode (i.e. ModifiedAtl cannot write metadata to a GYM file containing gzipped data)
	/// </summary>
	class VGM : MetaDataIO, IAudioDataIO
	{
        private const String VGM_SIGNATURE = "Vgm ";
        private const String GD3_SIGNATURE = "Gd3 ";

        private const Int32 VGM_HEADER_SIZE = 256;

        private static Int32 LOOP_COUNT_DEFAULT = 1;              // Default loop count
        private static Int32 FADEOUT_DURATION_DEFAULT = 10000;    // Default fadeout duration, in milliseconds (10s)
        private static Int32 RECORDING_RATE_DEFAULT = 60;         // Default playback rate for v1.00 files

        // Standard fields
        private Int32 version;
        private Int32 sampleRate;
        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private Int32 gd3TagOffset;

        private SizeInfo sizeInfo;
        private readonly String filePath;


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // AudioDataIO
        public Int32 SampleRate // Sample rate (hz)
            =>
                sampleRate;

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
            throw new NotImplementedException();
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        private void resetData()
        {
            // Reset variables
            sampleRate = 44100; // Default value for all VGM files, according to v1.70 spec 
            bitrate = 0;
            duration = 0;
            version = 0;

            gd3TagOffset = 0;

            ResetData();
        }

        public VGM(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


		// === PRIVATE METHODS ===

		private Boolean readHeader(BinaryReader source, ReadTagParams readTagParams)
		{
            Int32 nbSamples, loopNbSamples;
            var nbLoops = LOOP_COUNT_DEFAULT;
            var recordingRate = RECORDING_RATE_DEFAULT;

            var initialPosition = source.BaseStream.Position;
            var headerSignature = source.ReadBytes(VGM_SIGNATURE.Length);
            if (VGM_SIGNATURE.Equals(Utils.Latin1Encoding.GetString(headerSignature)))
			{
				source.BaseStream.Seek(4,SeekOrigin.Current); // EOF offset
                version = source.ReadInt32();
                source.BaseStream.Seek(8, SeekOrigin.Current); // Clocks
                gd3TagOffset = source.ReadInt32() + (Int32)source.BaseStream.Position - 4;

                if (/*gd3TagOffset > 0 && */readTagParams.PrepareForWriting)
                {
                    if (gd3TagOffset > VGM_HEADER_SIZE)
                    {
                        structureHelper.AddZone(gd3TagOffset, (Int32)sizeInfo.FileSize - gd3TagOffset);
                        structureHelper.AddIndex(source.BaseStream.Position - 4, gd3TagOffset, true);
                    } else
                    {
                        structureHelper.AddZone(sizeInfo.FileSize, 0);
                        structureHelper.AddIndex(source.BaseStream.Position - 4, 0, true);
                    }
                }

                nbSamples = source.ReadInt32();

                source.BaseStream.Seek(4, SeekOrigin.Current); // Loop offset

                loopNbSamples = source.ReadInt32();
                if (version >= 0x00000101)
                {
                    recordingRate = source.ReadInt32();
                }
                if (version >= 0x00000160)
                {
                    source.BaseStream.Seek(0x7E, SeekOrigin.Begin);
                    nbLoops -= source.ReadSByte();                  // Loop base
                }
                if (version >= 0x00000151)
                {
                    source.BaseStream.Seek(0x7F, SeekOrigin.Begin);
                    nbLoops = nbLoops * source.ReadByte();          // Loop modifier
                }

                duration = (nbSamples * 1000.0 / sampleRate) + (nbLoops * (loopNbSamples * 1000.0 / sampleRate));
                if (Settings.GYM_VGM_playbackRate > 0)
                {
                    duration = duration * (Settings.GYM_VGM_playbackRate / (Double)recordingRate);
                }
                if (nbLoops > 0) duration += FADEOUT_DURATION_DEFAULT;

                bitrate = (sizeInfo.FileSize - VGM_HEADER_SIZE) * 8 / duration; // TODO - use unpacked size if applicable, and not raw file size

                return true;
			}
            else
			{
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Not a VGM file");
                return false;
			}
		}

        private void readGd3Tag(BinaryReader source, Int32 offset)
        {
            source.BaseStream.Seek(offset, SeekOrigin.Begin);
            String str;

            if (GD3_SIGNATURE.Equals(Utils.Latin1Encoding.GetString(source.ReadBytes(GD3_SIGNATURE.Length))))
            {
                source.BaseStream.Seek(4, SeekOrigin.Current); // Version number
                source.BaseStream.Seek(4, SeekOrigin.Current); // Length

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Title (english)
                tagData.IntegrateValue(TagData.TAG_FIELD_TITLE, str);
                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Title (japanese)
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "TITLE_J", str));

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Game name (english)
                tagData.IntegrateValue(TagData.TAG_FIELD_ALBUM,  str);
                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Game name (japanese)
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "GAME_J", str));

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // System name (english)
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "SYSTEM", str));
                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // System name (japanese)
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "SYSTEM_J", str));

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Author (english)
                tagData.IntegrateValue(TagData.TAG_FIELD_ARTIST, str);
                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Author (japanese)
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "AUTHOR_J", str));

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Release date
                tagData.IntegrateValue(TagData.TAG_FIELD_RECORDING_DATE, str);

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Dumper
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "DUMPER", str));

                str = StreamUtils.ReadNullTerminatedString(source, Encoding.Unicode); // Notes
                tagData.IntegrateValue(TagData.TAG_FIELD_COMMENT, str);
            }
            else
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "Not a GD3 footer");
            }
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

            resetData();

            source.BaseStream.Seek(0, SeekOrigin.Begin);

            MemoryStream memStream = null;
            var usedSource = source;

            var headerSignature = source.ReadBytes(2);
            source.BaseStream.Seek(0, SeekOrigin.Begin);
            if (headerSignature[0] == 0x1f && headerSignature[1] == 0x8b) // File is GZIP-compressed
            {
                if (readTagParams.PrepareForWriting)
                {
                    LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Writing metadata to gzipped VGM files is not supported yet.");
                    return false;
                }

                using (var gzStream = new GZipStream(source.BaseStream, CompressionMode.Decompress))
                {
                    memStream = new MemoryStream();
                    StreamUtils.CopyStream(gzStream, memStream);
                    memStream.Seek(0, SeekOrigin.Begin);
                    usedSource = new BinaryReader(memStream);
                }
            }

            isValid = readHeader(usedSource, readTagParams);

            if (isValid && gd3TagOffset > VGM_HEADER_SIZE)
            {
                tagExists = true;
                readGd3Tag(usedSource, gd3TagOffset);
            }

            return result;
		}

        // Write GD3 tag
        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var endString = new Byte[2] { 0, 0 };
            var result = 11; // 11 field to write
            Int64 sizePos;
            String str;
            var unicodeEncoder = Encoding.Unicode;

            w.Write(Utils.Latin1Encoding.GetBytes(GD3_SIGNATURE));
            w.Write(0x00000100); // Version number

            sizePos = w.BaseStream.Position;
            w.Write((Int32)0);

            w.Write(unicodeEncoder.GetBytes(tag.Title));
            w.Write(endString); // Strings must be null-terminated
            str = "";
            if (AdditionalFields.ContainsKey("TITLE_J")) str = AdditionalFields["TITLE_J"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);

            w.Write(unicodeEncoder.GetBytes(tag.Album));
            w.Write(endString);
            str = "";
            if (AdditionalFields.ContainsKey("GAME_J")) str = AdditionalFields["GAME_J"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);

            str = "";
            if (AdditionalFields.ContainsKey("SYSTEM")) str = AdditionalFields["SYSTEM"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);
            str = "";
            if (AdditionalFields.ContainsKey("SYSTEM_J")) str = AdditionalFields["SYSTEM_J"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);

            w.Write(unicodeEncoder.GetBytes(tag.Artist));
            w.Write(endString);
            str = "";
            if (AdditionalFields.ContainsKey("AUTHOR_J")) str = AdditionalFields["AUTHOR_J"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);

            var dateStr = "";
            if (Date != DateTime.MinValue) dateStr = Date.ToString("yyyy/MM/dd");
            else if (tag.RecordingYear != null && tag.RecordingYear.Length == 4)
            {
                dateStr = tag.RecordingYear;
                if (tag.RecordingDayMonth != null && tag.RecordingDayMonth.Length >= 4)
                {
                    dateStr += "/" + tag.RecordingDayMonth.Substring(tag.RecordingDayMonth.Length - 2, 2) + "/" + tag.RecordingDayMonth.Substring(0, 2);
                }
            }
            w.Write(unicodeEncoder.GetBytes(dateStr));
            w.Write(endString);

            str = "";
            if (AdditionalFields.ContainsKey("DUMPER")) str = AdditionalFields["DUMPER"];
            w.Write(unicodeEncoder.GetBytes(str));
            w.Write(endString);

            w.Write(unicodeEncoder.GetBytes(tag.Comment));
            w.Write(endString);

            w.Write(endString); // Is supposed to be there, according to sample files

            var size = (Int32)(w.BaseStream.Position - sizePos - 4);
            w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            w.Write(size);

            return result;
        }
    }

}