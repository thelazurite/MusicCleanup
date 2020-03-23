using System;
using System.IO;
using ATL.Logging;
using static ATL.AudioData.AudioDataManager;
using Commons;
using System.Text;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Genesis YM2612 files manipulation (extensions : .GYM)
    /// 
    /// Implementation notes
    /// 
    ///     1/ Looping : I have yet to find a GYM file that actually contains a loop.
    ///     All archives I found so far are direct recording of game audio instructions
    ///     that actually repeat the same pattern twice (looping data is not used at all)
    ///     
    ///     2/ Gzipped stream : I have yet to find a GYM file that contains gzipped data.
    ///     => Rather than to make a theoretical implementation, there is no implementation at all.
    /// 
    /// </summary>
    class GYM : MetaDataIO, IAudioDataIO
	{
        private const String GYM_SIGNATURE = "GYMX";

        private const Int32 GYM_HEADER_SIZE = 428;

        private static UInt32 LOOP_COUNT_DEFAULT = 1;         // Default loop count
        private static UInt32 FADEOUT_DURATION_DEFAULT = 0;   // Default fadeout duration, in seconds
        private static UInt32 PLAYBACK_RATE_DEFAULT = 60;     // Default playback rate if no preference set (Hz)

        private static Byte[] CORE_SIGNATURE;

        // Standard fields
        private Int32 sampleRate;
        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        UInt32 loopStart;

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

        static GYM()
        {
            CORE_SIGNATURE = new Byte[416];
            Array.Clear(CORE_SIGNATURE, 0, 416);
        }

        private void resetData()
        {
            // Reset variables
            sampleRate = 44100; // Seems to be default value according to foobar2000
            bitrate = 0;
            duration = 0;

            loopStart = 0;

            ResetData();
        }

        public GYM(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


		// === PRIVATE METHODS ===

		private Boolean readHeader(BufferedBinaryReader source, ReadTagParams readTagParams)
		{
            String str;

            var initialPosition = source.Position;
            if (GYM_SIGNATURE.Equals(Utils.Latin1Encoding.GetString(source.ReadBytes(GYM_SIGNATURE.Length))))
			{
                if (readTagParams.PrepareForWriting)
                {
                    structureHelper.AddZone(source.Position, 416, CORE_SIGNATURE);
                }

                tagExists = true;

                str = Utils.StripEndingZeroChars( Encoding.UTF8.GetString(source.ReadBytes(32)) ).Trim();
                tagData.IntegrateValue(TagData.TAG_FIELD_TITLE, str);
                str = Utils.StripEndingZeroChars(Encoding.UTF8.GetString(source.ReadBytes(32))).Trim();
                tagData.IntegrateValue(TagData.TAG_FIELD_ALBUM, str);
                str = Utils.StripEndingZeroChars(Encoding.UTF8.GetString(source.ReadBytes(32))).Trim();
                tagData.IntegrateValue(TagData.TAG_FIELD_COPYRIGHT, str);
                str = Utils.StripEndingZeroChars(Encoding.UTF8.GetString(source.ReadBytes(32))).Trim();
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "EMULATOR", str));
                str = Utils.StripEndingZeroChars(Encoding.UTF8.GetString(source.ReadBytes(32))).Trim();
                tagData.AdditionalFields.Add(new MetaFieldInfo(getImplementedTagType(), "DUMPER", str));
                str = Utils.StripEndingZeroChars(Encoding.UTF8.GetString(source.ReadBytes(256))).Trim();
                tagData.IntegrateValue(TagData.TAG_FIELD_COMMENT, str);

                loopStart = source.ReadUInt32();
                var packedSize = source.ReadUInt32();

                if (packedSize > 0)
                {
                    LogDelegator.GetLogDelegate()(Log.LV_WARNING, "GZIP-compressed files are not supported"); // will be as soon as I find a sample to test with
                    return false;
                }

                return true;
			}
            else
			{
                LogDelegator.GetLogDelegate()(Log.LV_ERROR, "Not a GYM file");
                return false;
			}
		}

        private UInt32 calculateDuration(BufferedBinaryReader source, UInt32 loopStart, UInt32 nbLoops)
        {
            var streamSize = source.Length;
            Byte frameType;
            UInt32 frameIndex = 0;
            UInt32 nbTicks_all = 0;
            UInt32 nbTicks_loop = 0;
            var loopReached = false;

            while (source.Position < streamSize)
            {
                frameIndex++;
                if (frameIndex == loopStart) loopReached = true;

                frameType = source.ReadByte();
                switch (frameType)
                {
                    case (0x00):
                        nbTicks_all++;
                        if (loopReached) nbTicks_loop++;
                        break;
                    case (0x01): 
                    case (0x02): source.Seek(2, SeekOrigin.Current); break;
                    case (0x03): source.Seek(1, SeekOrigin.Current); break;
                }
            }

            var result = (nbTicks_all - nbTicks_loop) + (nbLoops * nbTicks_loop);
            if (Settings.GYM_VGM_playbackRate > 0)
            {
                result = (UInt32)Math.Round(result * (1.0 / Settings.GYM_VGM_playbackRate));
            }
            else
            {
                result = (UInt32)Math.Round(result * (1.0 / PLAYBACK_RATE_DEFAULT));
            }
            if (loopStart > 0) result += FADEOUT_DURATION_DEFAULT;

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
            var bufferedSource = new BufferedBinaryReader(source.BaseStream); // Optimize parsing speed

            resetData();

            source.BaseStream.Seek(0, SeekOrigin.Begin);

            isValid = readHeader(bufferedSource, readTagParams);

            if (isValid)
            {
                duration = calculateDuration(bufferedSource, loopStart, LOOP_COUNT_DEFAULT) * 1000.0;

                bitrate = (sizeInfo.FileSize - GYM_HEADER_SIZE) * 8 / duration; // TODO - use unpacked size if applicable, and not raw file size
            }

            return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 6;

            w.Write(Utils.BuildStrictLengthStringBytes(tag.Title, 32, 0, Encoding.UTF8));
            w.Write(Utils.BuildStrictLengthStringBytes(tag.Album, 32, 0, Encoding.UTF8));
            w.Write(Utils.BuildStrictLengthStringBytes(tag.Copyright, 32, 0, Encoding.UTF8));
            var str = "";
            if (AdditionalFields.ContainsKey("EMULATOR")) str = AdditionalFields["EMULATOR"];
            w.Write(Utils.BuildStrictLengthStringBytes(str, 32, 0, Encoding.UTF8));
            str = "";
            if (AdditionalFields.ContainsKey("DUMPER")) str = AdditionalFields["DUMPER"];
            w.Write(Utils.BuildStrictLengthStringBytes(str, 32, 0, Encoding.UTF8));
            w.Write(Utils.BuildStrictLengthStringBytes(tag.Comment, 256, 0, Encoding.UTF8));

            return result;
        }
    }

}