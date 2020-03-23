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
    /// Class for Impulse Tracker Module files manipulation (extensions : .IT)
    /// </summary>
    class IT : MetaDataIO, IAudioDataIO
    {
        private const String IT_SIGNATURE = "IMPM";

        private const String ZONE_TITLE = "title";

        // Effects
        private const Byte EFFECT_SET_SPEED = 0x01;
        private const Byte EFFECT_ORDER_JUMP = 0x02;
        private const Byte EFFECT_JUMP_TO_ROW = 0x03;
        private const Byte EFFECT_EXTENDED = 0x13;
        private const Byte EFFECT_SET_BPM = 0x14;

        private const Byte EFFECT_EXTENDED_LOOP = 0xB;


        // Standard fields
        private IList<Byte> ordersTable;
        private IList<Byte> patternTable;
        private IList<Byte> sampleTable;
        private IList<IList<IList<Event>>> patterns;
        private IList<Instrument> instruments;

        private Byte initialSpeed;
        private Byte initialTempo;

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


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            duration = 0;
            bitrate = 0;

            ordersTable = new List<Byte>();
            patternTable = new List<Byte>();
            sampleTable = new List<Byte>();

            patterns = new List<IList<IList<Event>>>();
            instruments = new List<Instrument>();

            ResetData();
        }

        public IT(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // === PRIVATE STRUCTURES/SUBCLASSES ===

        private class Instrument
        {
            public Byte Type = 0;
            public String FileName = "";
            public String DisplayName = "";

            // Other fields not useful for ModifiedAtl
        }

        private class Event
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

            IList<Event> row;

            Double speed = initialSpeed;
            Double tempo = initialTempo;
            var previousTempo = tempo;

            do // Patterns loop
            {
                do // Lines loop
                {
                    currentPattern = patternTable[currentPatternIndex];

                    while ((currentPattern > patterns.Count - 1) && (currentPatternIndex < patternTable.Count - 1))
                    {
                        if (currentPattern.Equals(255)) // End of song / sub-song
                        {
                            // Reset speed & tempo to file default (do not keep remaining values from previous sub-song)
                            speed = initialSpeed;
                            tempo = initialTempo;
                        }
                        currentPattern = patternTable[++currentPatternIndex];
                    }
                    if (currentPattern > patterns.Count - 1) return result;

                    row = patterns[currentPattern][currentRow];
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
                                currentPatternIndex = Math.Min(theEvent.Info, patternTable.Count - 1);
                                currentRow = 0;
                                positionJump = true;
                            }
                        }
                        else if (theEvent.Command.Equals(EFFECT_JUMP_TO_ROW))
                        {
                            currentPatternIndex++;
                            currentRow = Math.Min(theEvent.Info, patterns[currentPattern].Count);
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
                } while (currentRow < patterns[currentPattern].Count);

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
            } while (currentPatternIndex < patternTable.Count); // end patterns loop


            return result;
        }

        private void readSamples(BufferedBinaryReader source, IList<UInt32> samplePointers)
        {
            foreach (var pos in samplePointers)
            {
                source.Seek(pos, SeekOrigin.Begin);
                var instrument = new Instrument();

                source.Seek(4, SeekOrigin.Current); // Signature
                instrument.FileName = Utils.Latin1Encoding.GetString(source.ReadBytes(12)).Trim();
                instrument.FileName = instrument.FileName.Replace("\0", "");

                source.Seek(4, SeekOrigin.Current); // Data not relevant for ModifiedAtl

                instrument.DisplayName = StreamUtils.ReadNullTerminatedStringFixed(source, Utils.Latin1Encoding, 26);
                instrument.DisplayName = instrument.DisplayName.Replace("\0", "");

                instruments.Add(instrument);
            }
        }

        private void readInstruments(BufferedBinaryReader source, IList<UInt32> instrumentPointers)
        {
            foreach (var pos in instrumentPointers)
            {
                source.Seek(pos, SeekOrigin.Begin);
                var instrument = new Instrument();

                source.Seek(4, SeekOrigin.Current); // Signature
                instrument.FileName = Utils.Latin1Encoding.GetString(source.ReadBytes(12)).Trim();
                instrument.FileName = instrument.FileName.Replace("\0", "");

                source.Seek(16, SeekOrigin.Current); // Data not relevant for ModifiedAtl

                instrument.DisplayName = StreamUtils.ReadNullTerminatedStringFixed(source, Utils.Latin1Encoding, 26);
                instrument.DisplayName = instrument.DisplayName.Replace("\0", "");

                instruments.Add(instrument);
            }
        }

        private void readInstrumentsOld(BufferedBinaryReader source, IList<UInt32> instrumentPointers)
        {
            // The fileName and displayName fields have the same offset in the new and old format
            readInstruments(source, instrumentPointers);
        }

        private void readPatterns(BufferedBinaryReader source, IList<UInt32> patternPointers)
        {
            UInt16 nbRows;
            Byte rowNum;
            Byte what;
            Byte maskVariable = 0;
            IList<Event> aRow;
            IList<IList<Event>> aPattern;
            IDictionary<Int32, Byte> maskVariables = new Dictionary<Int32, Byte>();

            foreach (var pos in patternPointers)
            {
                aPattern = new List<IList<Event>>();
                if (pos > 0)
                {
                    source.Seek(pos, SeekOrigin.Begin);
                    aRow = new List<Event>();
                    rowNum = 0;
                    source.Seek(2, SeekOrigin.Current); // patternSize
                    nbRows = source.ReadUInt16();
                    source.Seek(4, SeekOrigin.Current); // unused data

                    do
                    {
                        what = source.ReadByte();

                        if (what > 0)
                        {
                            var theEvent = new Event();
                            theEvent.Channel = (what - 1) & 63;
                            if ((what & 128) > 0)
                            {
                                maskVariable = source.ReadByte();
                                maskVariables[theEvent.Channel] = maskVariable;
                            }
                            else if (maskVariables.ContainsKey(theEvent.Channel))
                            {
                                maskVariable = maskVariables[theEvent.Channel];
                            }
                            else
                            {
                                maskVariable = 0;
                            }

                            if ((maskVariable & 1) > 0) source.Seek(1, SeekOrigin.Current); // Note
                            if ((maskVariable & 2) > 0) source.Seek(1, SeekOrigin.Current); // Instrument
                            if ((maskVariable & 4) > 0) source.Seek(1, SeekOrigin.Current); // Volume/panning
                            if ((maskVariable & 8) > 0)
                            {
                                theEvent.Command = source.ReadByte();
                                theEvent.Info = source.ReadByte();
                            }

                            aRow.Add(theEvent);
                        }
                        else // what = 0 => end of row
                        {
                            aPattern.Add(aRow);
                            aRow = new List<Event>();
                            rowNum++;
                        }
                    } while (rowNum < nbRows);
                }

                patterns.Add(aPattern);
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
            UInt16 nbSamples = 0;
            UInt16 nbInstruments = 0;

            UInt16 flags;
            UInt16 special;
            UInt16 trackerVersion;
            UInt16 trackerVersionCompatibility;

            var useSamplesAsInstruments = false;

            UInt16 messageLength;
            UInt32 messageOffset;
            var message = "";

            IList<UInt32> patternPointers = new List<UInt32>();
            IList<UInt32> instrumentPointers = new List<UInt32>();
            IList<UInt32> samplePointers = new List<UInt32>();

            resetData();
            var bSource = new BufferedBinaryReader(source.BaseStream);


            if (!IT_SIGNATURE.Equals(Utils.Latin1Encoding.GetString(bSource.ReadBytes(4))))
            {
                result = false;
                throw new Exception(sizeInfo.FileSize + " : Invalid IT file (file signature mismatch)"); // TODO - might be a compressed file -> PK header
            }

            tagExists = true;

            // Title = max first 26 chars after file signature; null-terminated
            var title = StreamUtils.ReadNullTerminatedStringFixed(bSource, Utils.Latin1Encoding, 26);
            if (readTagParams.PrepareForWriting)
            {
                structureHelper.AddZone(4, 26, new Byte[26] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ZONE_TITLE);
            }
            tagData.IntegrateValue(TagData.TAG_FIELD_TITLE, title.Trim());
            bSource.Seek(2, SeekOrigin.Current); // Pattern row highlight information

            nbOrders = bSource.ReadUInt16();
            nbInstruments = bSource.ReadUInt16();
            nbSamples = bSource.ReadUInt16();
            nbPatterns = bSource.ReadUInt16();

            trackerVersion = bSource.ReadUInt16();
            trackerVersionCompatibility = bSource.ReadUInt16();

            flags = bSource.ReadUInt16();

            useSamplesAsInstruments = !((flags & 0x04) > 0);

            special = bSource.ReadUInt16();

            //            trackerName = "Impulse tracker"; // TODO use TrackerVersion to add version

            bSource.Seek(2, SeekOrigin.Current); // globalVolume (8b), masterVolume (8b)

            initialSpeed = bSource.ReadByte();
            initialTempo = bSource.ReadByte();

            bSource.Seek(2, SeekOrigin.Current); // panningSeparation (8b), pitchWheelDepth (8b)

            messageLength = bSource.ReadUInt16();
            messageOffset = bSource.ReadUInt32();
            bSource.Seek(132, SeekOrigin.Current); // reserved (32b), channel Pan (64B), channel Vol (64B)

            // Orders table
            for (var i = 0; i < nbOrders; i++)
            {
                patternTable.Add(bSource.ReadByte());
            }

            // Instruments pointers
            for (var i = 0; i < nbInstruments; i++)
            {
                instrumentPointers.Add(bSource.ReadUInt32());
            }

            // Samples pointers
            for (var i = 0; i < nbSamples; i++)
            {
                samplePointers.Add(bSource.ReadUInt32());
            }

            // Patterns pointers
            for (var i = 0; i < nbPatterns; i++)
            {
                patternPointers.Add(bSource.ReadUInt32());
            }

            if ((!useSamplesAsInstruments) && (instrumentPointers.Count > 0))
            {
                if (trackerVersionCompatibility < 0x200)
                {
                    readInstrumentsOld(bSource, instrumentPointers);
                }
                else
                {
                    readInstruments(bSource, instrumentPointers);
                }
            }
            else
            {
                readSamples(bSource, samplePointers);
            }
            readPatterns(bSource, patternPointers);

            // IT Message
            if ((special & 0x1) > 0)
            {
                bSource.Seek(messageOffset, SeekOrigin.Begin);
                //message = new String( StreamUtils.ReadOneByteChars(source, messageLength) );
                message = StreamUtils.ReadNullTerminatedStringFixed(bSource, Utils.Latin1Encoding, messageLength);
            }


            // == Computing track properties

            duration = calculateDuration() * 1000.0;

            String commentStr;
            if (messageLength > 0) // Get Comment from the "IT message" field
            {
                commentStr = message;
            }
            else // Get Comment from all the instrument names (common practice in the tracker community)
            {
                var comment = new StringBuilder("");
                // NB : Whatever the value of useSamplesAsInstruments, FInstruments contain the right data
                foreach (var i in instruments)
                {
                    if (i.DisplayName.Length > 0) comment.Append(i.DisplayName).Append(Settings.InternalValueSeparator);
                }
                if (comment.Length > 0) comment.Remove(comment.Length - 1, 1);
                commentStr = comment.ToString();
            }
            tagData.IntegrateValue(TagData.TAG_FIELD_COMMENT, commentStr);

            bitrate = (Double)sizeInfo.FileSize / duration;

            return result;
        }

        protected override Int32 write(TagData tag, BinaryWriter w, String zone)
        {
            var result = 0;

            if (ZONE_TITLE.Equals(zone))
            {
                var title = tag.Title;
                if (title.Length > 26) title = title.Substring(0, 26);
                else if (title.Length < 26) title = Utils.BuildStrictLengthString(title, 26, '\0');
                w.Write(Utils.Latin1Encoding.GetBytes(title));
                result = 1;
            }

            return result;
        }
    }

}