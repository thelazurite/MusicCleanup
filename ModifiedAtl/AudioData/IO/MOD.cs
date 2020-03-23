using System;
using System.IO;
using System.Collections.Generic;
using ATL.Logging;
using System.Text;
using static ATL.AudioData.AudioDataManager;
using Commons;

namespace ATL.AudioData.IO
{
	/// <summary>
    /// Class for Noisetracker/Soundtracker/Protracker Module files manipulation (extensions : .MOD)
    /// Based on info obtained from Thunder's readme (MODFIL10.TXT - Version 1.0)
	/// </summary>
	class MOD : MetaDataIO, IAudioDataIO
	{
        private const String ZONE_TITLE = "title";

        private const String SIG_POWERPACKER = "PP20";
        private const Byte NB_CHANNELS_DEFAULT = 4;
        private const Byte MAX_ROWS = 64;

        private const Byte DEFAULT_TICKS_PER_ROW = 6;
        private const Byte DEFAULT_BPM = 125;

        // Effects
        private const Byte EFFECT_POSITION_JUMP = 0xB;
        private const Byte EFFECT_PATTERN_BREAK = 0xD;
        private const Byte EFFECT_SET_SPEED     = 0xF;
        private const Byte EFFECT_EXTENDED      = 0xE;

        private const Byte EFFECT_EXTENDED_LOOP = 0x6;
        private const Byte EFFECT_NOTE_CUT      = 0xC;
        private const Byte EFFECT_NOTE_DELAY    = 0xD;
        private const Byte EFFECT_PATTERN_DELAY = 0xE;
        private const Byte EFFECT_INVERT_LOOP   = 0xF;

        private static IDictionary<String, ModFormat> modFormats;

		// Standard fields
        private IList<Sample> FSamples;
        private IList<IList<IList<Int32>>> FPatterns;
        private IList<Byte> FPatternTable;
        private Byte nbValidPatterns;
        private String formatTag;
        private Byte nbChannels;
        private String trackerName;

        private Double bitrate;
        private Double duration;

        private SizeInfo sizeInfo;
        private readonly String filePath;

        
        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        // IAudioDataIO
        public Int32 SampleRate // Sample rate (hz)
            =>
                0;

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


        // === PRIVATE STRUCTURES/SUBCLASSES ===

        private class Sample
		{
            public String Name;
            public Int32 Size;
            public SByte Finetune;
            public Byte Volume;

            public Int32 RepeatOffset;
            public Int32 RepeatLength;

			public void Reset()
			{
                Name = "";
                Size = 0;
                Finetune = 0;
                Volume = 0;
                
                RepeatLength = 0;
                RepeatOffset = 0;
            }
		}

        private class ModFormat
        {
            public String Name = "";
            public String Signature = "";
            public Byte NbSamples = 0;
            public Byte NbChannels = 0;

            public ModFormat(String name, String sig, Byte nbSamples, Byte nbChannels)
            {
                Name = name;
                Signature = sig;
                NbSamples = nbSamples;
                NbChannels = nbChannels;
            }
        }

        
        // ---------- CONSTRUCTORS & INITIALIZERS

        static MOD()
        {
            modFormats = new Dictionary<String, ModFormat>();

            modFormats.Add("M.K.", new ModFormat("ProTracker", "M.K.", 31, 4));
            modFormats.Add("M!K!", new ModFormat("ProTracker", "M!K!", 31, 4));
            modFormats.Add("FLT4", new ModFormat("StarTrekker", "FLT4", 31, 4));
            modFormats.Add("2CHN", new ModFormat("FastTracker", "2CHN", 31, 2));
            modFormats.Add("4CHN", new ModFormat("FastTracker", "4CHN", 31, 4));
            modFormats.Add("6CHN", new ModFormat("FastTracker", "6CHN", 31, 6));
            modFormats.Add("8CHN", new ModFormat("FastTracker", "8CHN", 31, 8));
            modFormats.Add("OCTA", new ModFormat("FastTracker", "OCTA", 31, 8));
            modFormats.Add("FLT8", new ModFormat("StarTrekker", "FLT8", 31, 8));
            modFormats.Add("CD81", new ModFormat("Falcon", "CD81", 31, 8));
            modFormats.Add("10CH", new ModFormat("FastTracker", "10CH", 31, 10));
            modFormats.Add("12CH", new ModFormat("FastTracker", "12CH", 31, 12));
            modFormats.Add("14CH", new ModFormat("FastTracker", "14CH", 31, 14));
            modFormats.Add("11CH", new ModFormat("TakeTracker", "11CH", 31, 11));
            modFormats.Add("13CH", new ModFormat("TakeTracker", "13CH", 31, 13));
            modFormats.Add("15CH", new ModFormat("TakeTracker", "15CH", 31, 15));
            for (Byte i = 16; i < 33; i++)
            {
                modFormats.Add(i + "CH", new ModFormat("FastTracker", i + "CH", 31, i));
            }
            modFormats.Add("TDZ1", new ModFormat("TakeTracker", "TDZ1", 31, 1));
            modFormats.Add("TDZ2", new ModFormat("TakeTracker", "TDZ2", 31, 2));
            modFormats.Add("TDZ3", new ModFormat("TakeTracker", "TDZ3", 31, 3));
            modFormats.Add("5CHN", new ModFormat("TakeTracker", "5CHN", 31, 5));
            modFormats.Add("7CHN", new ModFormat("TakeTracker", "7CHN", 31, 7));
            modFormats.Add("9CHN", new ModFormat("TakeTracker", "9CHN", 31, 9));
        }

        private void resetData()
        {
            duration = 0;
            bitrate = 0;

            FSamples = new List<Sample>();
            FPatterns = new List<IList<IList<Int32>>>();
            FPatternTable = new List<Byte>();
            nbValidPatterns = 0;
            formatTag = "";
            trackerName = "";
            nbChannels = 0;

            ResetData();
        }

        public MOD(String filePath)
		{
            this.filePath = filePath;
            resetData();
        }



        // ---------- SUPPORT METHODS

        //                 24 * beats/minute
        // lines/minute = -----------------
        //                    ticks/line
        private Double calculateDuration()
        {
            Double result = 0;

            // Jump and break control variables
            var currentPattern = 0;
            var currentRow = 0;
            var positionJump = false;
            var patternBreak = false;

            // Loop control variables
            var isInsideLoop = false;
            Double loopDuration = 0;

            IList<Int32> row;
            
            Int32 temp;
            Double ticksPerRow = DEFAULT_TICKS_PER_ROW;
            Double bpm = DEFAULT_BPM;

            Int32 effect;
            Int32 arg1;
            Int32 arg2;

            do // Patterns loop
            {
                do // Rows loop
                {
                    row = FPatterns[FPatternTable[currentPattern]][currentRow];
                    foreach (var note in row) // Channels loop
                    {
                        effect = (note & 0xF00) >> 8;
                        arg1 = (note & 0xF0) >> 4;
                        arg2 = note & 0xF;

                        if (effect.Equals(EFFECT_SET_SPEED))
                        {
                            temp = arg1 * 16 + arg2;
                            if (temp > 32) // BPM
                            {
                                bpm = temp;
                            }
                            else // ticks per row
                            {
                                ticksPerRow = temp;
                            }
                        }
                        else if (effect.Equals(EFFECT_POSITION_JUMP))
                        {
                            temp = arg1 * 16 + arg2;

                            // Processes position jump only if the jump is forward
                            // => Prevents processing "forced" song loops ad infinitum
                            if (temp > currentPattern)
                            {
                                currentPattern = temp;
                                currentRow = 0;
                                positionJump = true;
                            }
                        }
                        else if (effect.Equals(EFFECT_PATTERN_BREAK))
                        {
                            currentPattern++;
                            currentRow = arg1 * 10 + arg2;
                            patternBreak = true;
                        }
                        else if (effect.Equals(EFFECT_EXTENDED))
                        {
                            if (arg1.Equals(EFFECT_EXTENDED_LOOP))
                            {
                                if (arg2.Equals(0)) // Beginning of loop
                                {
                                    loopDuration = 0;
                                    isInsideLoop = true; 
                                }
                                else // End of loop + nb. repeat indicator
                                {
                                    result += loopDuration * arg2;
                                    isInsideLoop = false;
                                }
                            }
                        }
                        if (positionJump || patternBreak) break;
                    } // end channels loop
                    
                    result += 60 * (ticksPerRow / (24 * bpm));
                    if (isInsideLoop) loopDuration += 60 * (ticksPerRow / (24 * bpm));
                    
                    if (positionJump || patternBreak) break;

                    currentRow++;
                } while (currentRow < MAX_ROWS);

                if (positionJump || patternBreak)
                {
                    positionJump = false;
                    patternBreak = false;
                }
                else
                {
                    currentPattern++;
                    currentRow = 0;
                }
            } while (currentPattern < nbValidPatterns); // end patterns loop

            return result * 1000.0;
        }

        private Byte detectNbSamples(BinaryReader source)
        {
            Byte result = 31;
            var position = source.BaseStream.Position;

            source.BaseStream.Seek(1080, SeekOrigin.Begin);

            formatTag = Utils.Latin1Encoding.GetString(source.ReadBytes(4)).Trim();

            if (!modFormats.ContainsKey(formatTag)) result = 15;

            source.BaseStream.Seek(position, SeekOrigin.Begin);

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
            var maxPatterns = -1;
            Byte nbSamples = 31;

            String readString;
            var comment = new StringBuilder("");

            Sample sample;
            IList<IList<Int32>> pattern;
            IList<Int32> row;

            resetData();


            // == TITLE ==
            readString = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
            if (readString.Equals(SIG_POWERPACKER))
            {
                result = false;
                throw new Exception("MOD files compressed with PowerPacker are not supported yet");
            }

            tagExists = true;

            // Restart from beginning, else parser might miss empty titles
            source.BaseStream.Seek(0, SeekOrigin.Begin);

            // Title = max first 20 chars; null-terminated
            var title = StreamUtils.ReadNullTerminatedStringFixed(source, System.Text.Encoding.ASCII, 20);
            if (readTagParams.PrepareForWriting)
            {
                structureHelper.AddZone(0, 20, new Byte[20] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ZONE_TITLE);
            }
            tagData.IntegrateValue(TagData.TAG_FIELD_TITLE, title.Trim());

            // == SAMPLES ==
            nbSamples = detectNbSamples(source);
            var charOne = Utils.Latin1Encoding.GetString( new Byte[] { 1 } );

            for (var i = 0; i < nbSamples; i++)
            {
                sample = new Sample();
                sample.Name = StreamUtils.ReadNullTerminatedStringFixed(source, System.Text.Encoding.ASCII, 22).Trim();
                sample.Name = sample.Name.Replace("\0", "");
                sample.Name = sample.Name.Replace(charOne, "");
                sample.Size = StreamUtils.ReverseUInt16(source.ReadUInt16())*2;
                sample.Finetune = source.ReadSByte();
                sample.Volume = source.ReadByte();
                sample.RepeatOffset = StreamUtils.ReverseUInt16(source.ReadUInt16())*2;
                sample.RepeatLength = StreamUtils.ReverseUInt16(source.ReadUInt16())*2;
                FSamples.Add(sample);
            }


            // == SONG ==
            nbValidPatterns = source.ReadByte();
            source.BaseStream.Seek(1, SeekOrigin.Current); // Controversial byte; no real use here
            for (var i = 0; i < 128; i++) FPatternTable.Add(source.ReadByte()); // Pattern table

            // File format tag
            formatTag = Utils.Latin1Encoding.GetString(source.ReadBytes(4)).Trim();
            if (modFormats.ContainsKey(formatTag))
            {
                nbChannels = modFormats[formatTag].NbChannels;
                trackerName = modFormats[formatTag].Name;
            }
            else // Default
            {
                nbChannels = NB_CHANNELS_DEFAULT;
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "MOD format tag '" + formatTag + "'not recognized");
            }
            
            // == PATTERNS ==
            // Some extra information about the "FLT8" -type MOD's:
            //
            // These MOD's have 8 channels, still the format isn't the same as the
            // other 8 channel formats ("OCTA", "CD81", "8CHN"): instead of storing
            // ONE 8-track pattern, it stores TWO 4-track patterns per logical pattern.
            // i.e. The first 4 channels of the first logical pattern are stored in
            // the first physical 4-channel pattern (size 1kb) whereas channel 5 until
            // channel 8 of the first logical pattern are stored as the SECOND physical
            // 4-channel pattern. Got it? ;-).
            // If you convert all the 4 channel patterns to 8 channel patterns, do not
            // forget to divide each pattern nr by 2 in the pattern sequence table!
           
            foreach (var b in FPatternTable) maxPatterns = Math.Max(maxPatterns, b);

            for (var p = 0; p < maxPatterns+1; p++) // Patterns loop
            {
                FPatterns.Add(new List<IList<Int32>>());
                pattern = FPatterns[FPatterns.Count - 1];
                // Rows loop
                for (var l = 0; l < MAX_ROWS; l++)
                {
                    pattern.Add(new List<Int32>());
                    row = pattern[pattern.Count - 1];
                    for (var c = 0; c < nbChannels; c++) // Channels loop
                    {
                        row.Add( StreamUtils.ReverseInt32(source.ReadInt32()) );
                    } // end channels loop
                } // end rows loop
            } // end patterns loop


            // == Computing track properties

            duration = calculateDuration();

            foreach (var aSample in FSamples)
            {
                if (aSample.Name.Length > 0) comment.Append(aSample.Name).Append(Settings.InternalValueSeparator);
            }
            if (comment.Length > 0) comment.Remove(comment.Length - 1, 1);

            tagData.IntegrateValue(TagData.TAG_FIELD_COMMENT, comment.ToString());

            bitrate = sizeInfo.FileSize / duration;

            return result;
		}

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            if (ZONE_TITLE.Equals(zone))
            {
                var title = tag.Title;
                if (title.Length > 20) title = title.Substring(0, 20);
                else if (title.Length < 20) title = Utils.BuildStrictLengthString(title, 20, '\0');
                w.Write(Utils.Latin1Encoding.GetBytes(title));
                result = 1;
            }

            return result;
        }
    }

}