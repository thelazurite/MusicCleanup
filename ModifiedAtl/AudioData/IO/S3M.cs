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
    /// Class for ScreamTracker Module files manipulation (extensions : .S3M)
    /// 
    /// Note : Parsing as it is considers the file as one single song. 
    /// Modules with song delimiters (pattern code 0xFF) are supported, but displayed as one track
    /// instead of multiple tracks (behaviour of foobar2000).
    /// 
    /// As a consequence, modules containing multiple songs and exotic loops (i.e. looping from song 2 to song 1)
    /// might not be detected with their exact duration.
    /// </summary>
    class S3M : MetaDataIO, IAudioDataIO
    {
        private const String ZONE_TITLE = "title";

        private const String S3M_SIGNATURE = "SCRM";
        private const Byte MAX_ROWS = 64;

        // Effects
        private const Byte EFFECT_SET_SPEED = 0x01;
        private const Byte EFFECT_ORDER_JUMP = 0x02;
        private const Byte EFFECT_JUMP_TO_ROW = 0x03;
        private const Byte EFFECT_EXTENDED = 0x13;
        private const Byte EFFECT_SET_BPM = 0x14;

        private const Byte EFFECT_EXTENDED_LOOP = 0xB;


        // Standard fields
        private IList<Byte> FChannelTable;
        private IList<Byte> FPatternTable;
        private IList<IList<IList<S3MEvent>>> FPatterns;
        private IList<Instrument> FInstruments;

        private Byte initialSpeed;
        private Byte initialTempo;

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

        private class Instrument
        {
            public Byte Type = 0;
            public String FileName = "";
            public String DisplayName = "";

            // Other fields not useful for ModifiedAtl
        }

        private class S3MEvent
        {
            // Commented fields below not useful for ModifiedAtl
            public Int32 Channel;
            //public byte Note;
            //public byte Instrument;
            //public byte Volume;
            public Byte Command;
            public Byte Info;

            public void Reset()
            {
                Channel = 0;
                Command = 0;
                Info = 0;
            }
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        private void resetData()
        {
            // Reset variables
            duration = 0;
            bitrate = 0;

            FPatternTable = new List<Byte>();
            FChannelTable = new List<Byte>();

            FPatterns = new List<IList<IList<S3MEvent>>>();
            FInstruments = new List<Instrument>();

            trackerName = "";
            nbChannels = 0;

            ResetData();
        }

        public S3M(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // === PRIVATE METHODS ===

        private Double calculateDuration()
        {
            Double result = 0;

            // Jump and break control variables
            var currentPatternIndex = 0;    // Index in the pattern table
            var currentPattern = 0;         // Pattern number per se
            var currentRow = 0;
            var positionJump = false;
            var patternBreak = false;

            // Loop control variables
            var isInsideLoop = false;
            Double loopDuration = 0;

            IList<S3MEvent> row;

            Double speed = initialSpeed;
            Double tempo = initialTempo;
            var previousTempo = tempo;

            do // Patterns loop
            {
                do // Lines loop
                {
                    currentPattern = FPatternTable[currentPatternIndex];

                    while ((currentPattern > FPatterns.Count - 1) && (currentPatternIndex < FPatternTable.Count - 1))
                    {
                        if (currentPattern.Equals(255)) // End of song / sub-song
                        {
                            // Reset speed & tempo to file default (do not keep remaining values from previous sub-song)
                            speed = initialSpeed;
                            tempo = initialTempo;
                        }
                        currentPattern = FPatternTable[++currentPatternIndex];
                    }
                    if (currentPattern > FPatterns.Count - 1) return result;

                    row = FPatterns[currentPattern][currentRow];
                    foreach (var theEvent in row) // Events loop
                    {

                        if (theEvent.Command.Equals(EFFECT_SET_SPEED))
                        {
                            if (theEvent.Info > 0) speed = theEvent.Info;
                        }
                        else if (theEvent.Command.Equals(EFFECT_SET_BPM))
                        {
                            if (theEvent.Info > 0x20)
                            {
                                tempo = theEvent.Info;
                            }
                            else
                            {
                                if (theEvent.Info.Equals(0))
                                {
                                    tempo = previousTempo;
                                }
                                else
                                {
                                    previousTempo = tempo;
                                    if (theEvent.Info < 0x10)
                                    {
                                        tempo -= theEvent.Info;
                                    }
                                    else
                                    {
                                        tempo += (theEvent.Info - 0x10);
                                    }
                                }
                            }
                        }
                        else if (theEvent.Command.Equals(EFFECT_ORDER_JUMP))
                        {
                            // Processes position jump only if the jump is forward
                            // => Prevents processing "forced" song loops ad infinitum
                            if (theEvent.Info > currentPatternIndex)
                            {
                                currentPatternIndex = Math.Min(theEvent.Info, FPatternTable.Count - 1);
                                currentRow = 0;
                                positionJump = true;
                            }
                        }
                        else if (theEvent.Command.Equals(EFFECT_JUMP_TO_ROW))
                        {
                            currentPatternIndex++;
                            currentRow = Math.Min(theEvent.Info, (Byte)63);
                            patternBreak = true;
                        }
                        else if (theEvent.Command.Equals(EFFECT_EXTENDED))
                        {
                            if ((theEvent.Info >> 4).Equals(EFFECT_EXTENDED_LOOP))
                            {
                                if ((theEvent.Info & 0xF).Equals(0)) // Beginning of loop
                                {
                                    loopDuration = 0;
                                    isInsideLoop = true;
                                }
                                else // End of loop + nb. repeat indicator
                                {
                                    result += loopDuration * (theEvent.Info & 0xF);
                                    isInsideLoop = false;
                                }
                            }
                        }

                        if (positionJump || patternBreak) break;
                    } // end Events loop

                    result += 60 * (speed / (24 * tempo));
                    if (isInsideLoop) loopDuration += 60 * (speed / (24 * tempo));

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
                    currentPatternIndex++;
                    currentRow = 0;
                }
            } while (currentPatternIndex < FPatternTable.Count); // end patterns loop


            return result;
        }

        private String getTrackerName(UInt16 trackerVersion)
        {
            var result = "";

            switch ((trackerVersion & 0xF000) >> 12)
            {
                case 0x1: result = "ScreamTracker"; break;
                case 0x2: result = "Imago Orpheus"; break;
                case 0x3: result = "Impulse Tracker"; break;
                case 0x4: result = "Schism Tracker"; break;
                case 0x5: result = "OpenMPT"; break;
                case 0xC: result = "Camoto/libgamemusic"; break;
            }

            return result;
        }

        private void readInstruments(BufferedBinaryReader source, IList<UInt16> instrumentPointers)
        {
            foreach (var pos in instrumentPointers)
            {
                source.Seek(pos << 4, SeekOrigin.Begin);
                var instrument = new Instrument();
                instrument.Type = source.ReadByte();
                instrument.FileName = Utils.Latin1Encoding.GetString(source.ReadBytes(12)).Trim();
                instrument.FileName = instrument.FileName.Replace("\0", "");

                if (instrument.Type > 0) // Same offsets for PCM and AdLib display names
                {
                    source.Seek(35, SeekOrigin.Current);
                    instrument.DisplayName = StreamUtils.ReadNullTerminatedStringFixed(source, Encoding.ASCII, 28);
                    instrument.DisplayName = instrument.DisplayName.Replace("\0", "");
                    source.Seek(4, SeekOrigin.Current);
                }

                FInstruments.Add(instrument);
            }
        }

        private void readPatterns(BufferedBinaryReader source, IList<UInt16> patternPointers)
        {
            Byte rowNum;
            Byte what;
            IList<S3MEvent> aRow;
            IList<IList<S3MEvent>> aPattern;

            foreach (var pos in patternPointers)
            {
                aPattern = new List<IList<S3MEvent>>();

                source.Seek(pos << 4, SeekOrigin.Begin);
                aRow = new List<S3MEvent>();
                rowNum = 0;
                source.Seek(2, SeekOrigin.Current); // patternSize

                do
                {
                    what = source.ReadByte();

                    if (what > 0)
                    {
                        var theEvent = new S3MEvent();
                        theEvent.Channel = what & 0x1F;

                        if ((what & 0x20) > 0) source.Seek(2, SeekOrigin.Current); // Note & Instrument
                        if ((what & 0x40) > 0) source.Seek(1, SeekOrigin.Current); // Volume
                        if ((what & 0x80) > 0)
                        {
                            theEvent.Command = source.ReadByte();
                            theEvent.Info = source.ReadByte();
                        }

                        aRow.Add(theEvent);
                    }
                    else // what = 0 => end of row
                    {
                        aPattern.Add(aRow);
                        aRow = new List<S3MEvent>();
                        rowNum++;
                    }
                } while (rowNum < MAX_ROWS);

                FPatterns.Add(aPattern);
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

            UInt16 nbOrders = 0;
            UInt16 nbPatterns = 0;
            UInt16 nbInstruments = 0;

            UInt16 flags;
            UInt16 trackerVersion;

            var comment = new StringBuilder("");

            IList<UInt16> patternPointers = new List<UInt16>();
            IList<UInt16> instrumentPointers = new List<UInt16>();

            resetData();
            var bSource = new BufferedBinaryReader(source.BaseStream);

            // Title = first 28 chars
            var title = StreamUtils.ReadNullTerminatedStringFixed(bSource, System.Text.Encoding.ASCII, 28);
            if (readTagParams.PrepareForWriting)
            {
                structureHelper.AddZone(0, 28, new Byte[28] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ZONE_TITLE);
            }
            tagData.IntegrateValue(TagData.TAG_FIELD_TITLE, title.Trim());
            bSource.Seek(4, SeekOrigin.Current);

            nbOrders = bSource.ReadUInt16();
            nbInstruments = bSource.ReadUInt16();
            nbPatterns = bSource.ReadUInt16();

            flags = bSource.ReadUInt16();
            trackerVersion = bSource.ReadUInt16();

            trackerName = getTrackerName(trackerVersion);

            bSource.Seek(2, SeekOrigin.Current); // sampleType (16b)
            if (!S3M_SIGNATURE.Equals(Utils.Latin1Encoding.GetString(bSource.ReadBytes(4))))
            {
                result = false;
                throw new Exception("Invalid S3M file (file signature mismatch)");
            }
            bSource.Seek(1, SeekOrigin.Current); // globalVolume (8b)

            tagExists = true;

            initialSpeed = bSource.ReadByte();
            initialTempo = bSource.ReadByte();

            bSource.Seek(1, SeekOrigin.Current); // masterVolume (8b)
            bSource.Seek(1, SeekOrigin.Current); // ultraClickRemoval (8b)
            bSource.Seek(1, SeekOrigin.Current); // defaultPan (8b)
            bSource.Seek(8, SeekOrigin.Current); // defaultPan (64b)
            bSource.Seek(2, SeekOrigin.Current); // ptrSpecial (16b)

            // Channel table
            for (var i = 0; i < 32; i++)
            {
                FChannelTable.Add(bSource.ReadByte());
                if (FChannelTable[FChannelTable.Count - 1] < 30) nbChannels++;
            }

            // Pattern table
            for (var i = 0; i < nbOrders; i++)
            {
                FPatternTable.Add(bSource.ReadByte());
            }

            // Instruments pointers
            for (var i = 0; i < nbInstruments; i++)
            {
                instrumentPointers.Add(bSource.ReadUInt16());
            }

            // Patterns pointers
            for (var i = 0; i < nbPatterns; i++)
            {
                patternPointers.Add(bSource.ReadUInt16());
            }

            readInstruments(bSource, instrumentPointers);
            readPatterns(bSource, patternPointers);


            // == Computing track properties

            duration = calculateDuration() * 1000.0;

            foreach (var i in FInstruments)
            {
                var displayName = i.DisplayName.Trim();
                if (displayName.Length > 0) comment.Append(displayName).Append(Settings.InternalValueSeparator);
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
                if (title.Length > 28) title = title.Substring(0, 28);
                else if (title.Length < 28) title = Utils.BuildStrictLengthString(title, 28, '\0');
                w.Write(Utils.Latin1Encoding.GetBytes(title));
                result = 1;
            }

            return result;
        }
    }

}