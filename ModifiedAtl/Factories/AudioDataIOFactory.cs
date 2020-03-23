using System;
using System.Collections.Generic;

namespace ATL.AudioData
{
    /// <summary>
    /// Factory for audio data readers
    /// </summary>
    public class AudioDataIoFactory : ReaderFactory
    {
        // Codec families
        public const Int32 CfLossy = 0; // Streamed, lossy data
        public const Int32 CfLossless = 1; // Streamed, lossless data
        public const Int32 CfSeqWav = 2; // Sequenced with embedded sound library
        public const Int32 CfSeq = 3; // Sequenced with codec or hardware-dependent sound library

        public static Int32 NbCodecFamilies = 4;

        public const Int32 MaxAlternates = 10; // Max number of alternate formats having the same file extension

        // The instance of this factory
        private static AudioDataIoFactory _theFactory = null;

        // Codec IDs
        private const Int32 CidMp3 = 0;
        private const Int32 CidOgg = 1;
        private const Int32 CidMpc = 2;
        private const Int32 CidFlac = 3;
        private const Int32 CidApe = 4;
        private const Int32 CidWma = 5;
        private const Int32 CidMidi = 6;
        private const Int32 CidAac = 7;
        private const Int32 CidAc3 = 8;
        private const Int32 CidOfr = 9;
        private const Int32 CidWavpack = 10;
        private const Int32 CidWav = 11;
        private const Int32 CidPsf = 12;
        private const Int32 CidSpc = 13;
        private const Int32 CidDts = 14;
        private const Int32 CidVqf = 15;
        private const Int32 CidTta = 16;
        private const Int32 CidDsf = 17;
        private const Int32 CidTak = 18;
        private const Int32 CidMod = 19;
        private const Int32 CidS3M = 20;
        private const Int32 CidXm = 21;
        private const Int32 CidIt = 22;
        private const Int32 CidAiff = 23;
        private const Int32 CidVgm = 24;
        private const Int32 CidGym = 25;
        private const Int32 NbCodecs = 26;

        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the instance of this factory (Singleton pattern) 
        /// </summary>
        /// <returns>Instance of the AudioReaderFactory of the application</returns>
        public static AudioDataIoFactory GetInstance()
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("Big-endian based platforms are not supported by ModifiedAtl");

            if (null != _theFactory) return _theFactory;
            _theFactory = new AudioDataIoFactory
            {
                formatListByExt = new Dictionary<String, IList<Format>>(),
                formatListByMime = new Dictionary<String, IList<Format>>()
            };


            var tempFmt = new Format("MPEG Audio Layer") {ID = CidMp3};
            tempFmt.AddMimeType("audio/mp3");
            tempFmt.AddMimeType("audio/mpeg");
            tempFmt.AddMimeType("audio/x-mpeg");
            tempFmt.AddExtension(".mp1");
            tempFmt.AddExtension(".mp2");
            tempFmt.AddExtension(".mp3");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("OGG : Vorbis, Opus") {ID = CidOgg};
            tempFmt.AddMimeType("audio/ogg");
            tempFmt.AddMimeType("audio/vorbis");
            tempFmt.AddMimeType("audio/opus");
            tempFmt.AddMimeType("audio/ogg;codecs=opus");
            tempFmt.AddExtension(".ogg");
            tempFmt.AddExtension(".opus");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Musepack / MPEGplus") {ID = CidMpc};
            tempFmt.AddMimeType("audio/x-musepack");
            tempFmt.AddMimeType("audio/musepack");
            tempFmt.AddExtension(".mp+");
            tempFmt.AddExtension(".mpc");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Windows Media Audio") {ID = CidWma};
            tempFmt.AddMimeType("audio/x-ms-wma");
            tempFmt.AddMimeType("video/x-ms-asf");
            tempFmt.AddExtension(".asf");
            tempFmt.AddExtension(".wma");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Advanced Audio Coding") {ID = CidAac};
            tempFmt.AddMimeType("audio/mp4");
            tempFmt.AddMimeType("audio/aac");
            tempFmt.AddMimeType("audio/mp4a-latm");
            tempFmt.AddExtension(".aac");
            tempFmt.AddExtension(".mp4");
            tempFmt.AddExtension(".m4a");
            tempFmt.AddExtension(".m4v");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Dolby Digital") {ID = CidAc3};
            tempFmt.AddMimeType("audio/ac3");
            tempFmt.AddExtension(".ac3");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Digital Theatre System") {ID = CidDts};
            tempFmt.AddMimeType("audio/vnd.dts");
            tempFmt.AddMimeType("audio/vnd.dts.hd");
            tempFmt.AddExtension(".dts");
            tempFmt.Readable = false;
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("TwinVQ") {ID = CidVqf};
            tempFmt.AddExtension(".vqf");
            tempFmt.AddMimeType("audio/x-twinvq");
            tempFmt.Readable = false;
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Free Lossless Audio Codec") {ID = CidFlac};
            tempFmt.AddMimeType("audio/x-flac");
            tempFmt.AddExtension(".flac");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Monkey's Audio") {ID = CidApe};
            tempFmt.AddMimeType("audio/ape");
            tempFmt.AddMimeType("audio/x-ape");
            tempFmt.AddExtension(".ape");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("OptimFROG") {ID = CidOfr};
            tempFmt.AddMimeType("audio/ofr");
            tempFmt.AddMimeType("audio/x-ofr");
            tempFmt.AddExtension(".ofr");
            tempFmt.AddExtension(".ofs");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("WAVPack") {ID = CidWavpack};
            tempFmt.AddMimeType("audio/x-wavpack");
            tempFmt.AddMimeType("audio/wavpack");
            tempFmt.AddExtension(".wv");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("PCM (uncompressed audio)") {ID = CidWav};
            tempFmt.AddMimeType("audio/x-wav");
            tempFmt.AddMimeType("audio/wav");
            tempFmt.AddExtension(".wav");
            tempFmt.AddExtension(".bwf");
            tempFmt.AddExtension(".bwav");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Musical Instruments Digital Interface") {ID = CidMidi};
            tempFmt.AddMimeType("audio/mid");
            tempFmt.AddExtension(".mid");
            tempFmt.AddExtension(".midi");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Direct Stream Digital") {ID = CidDsf};
            tempFmt.AddMimeType("audio/dsf");
            tempFmt.AddMimeType("audio/x-dsf");
            tempFmt.AddMimeType("audio/dsd");
            tempFmt.AddMimeType("audio/x-dsd");
            tempFmt.AddExtension(".dsf");
            tempFmt.AddExtension(".dsd");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Portable Sound Format") {ID = CidPsf};
            tempFmt.AddMimeType("audio/psf"); // Unofficial
            tempFmt.AddMimeType("audio/x-psf"); // Unofficial
            tempFmt.AddExtension(".psf");
            tempFmt.AddExtension(".psf1");
            tempFmt.AddExtension(".minipsf");
            tempFmt.AddExtension(".minipsf1");
            tempFmt.AddExtension(".psf2");
            tempFmt.AddExtension(".minipsf2");
            tempFmt.AddExtension(".ssf");
            tempFmt.AddExtension(".minissf");
            tempFmt.AddExtension(".dsf");
            tempFmt.AddExtension(".minidsf");
            tempFmt.AddExtension(".gsf");
            tempFmt.AddExtension(".minigsf");
            tempFmt.AddExtension(".qsf");
            tempFmt.AddExtension(".miniqsf");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("SPC700 Sound Files") {ID = CidSpc};
            tempFmt.AddMimeType("audio/spc"); // Unofficial
            tempFmt.AddMimeType("audio/x-spc"); // Unofficial
            tempFmt.AddExtension(".spc");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("True Audio") {ID = CidTta};
            tempFmt.AddMimeType("audio/tta");
            tempFmt.AddMimeType("audio/x-tta");
            tempFmt.AddExtension(".tta");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Tom's lossless Audio Kompressor (TAK)") {ID = CidTak};
            tempFmt.AddMimeType("audio/tak"); // Unofficial
            tempFmt.AddMimeType("audio/x-tak"); // Unofficial
            tempFmt.AddExtension(".tak");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Noisetracker/Soundtracker/Protracker Module");
            tempFmt.ID = CidMod;
            tempFmt.AddMimeType("audio/x-mod");
            tempFmt.AddExtension(".mod");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("ScreamTracker Module");
            tempFmt.ID = CidS3M;
            tempFmt.AddMimeType("audio/s3m");
            tempFmt.AddMimeType("audio/x-s3m");
            tempFmt.AddExtension(".s3m");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Extended Module");
            tempFmt.ID = CidXm;
            tempFmt.AddMimeType("audio/xm");
            tempFmt.AddMimeType("audio/x-xm");
            tempFmt.AddExtension(".xm");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Impulse Tracker");
            tempFmt.ID = CidIt;
            tempFmt.AddMimeType("audio/it");
            tempFmt.AddExtension(".it");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Audio Interchange File Format: (Audio IFF)");
            tempFmt.ID = CidAiff;
            tempFmt.AddMimeType("audio/x-aiff");
            tempFmt.AddExtension(".aif");
            tempFmt.AddExtension(".aiff");
            tempFmt.AddExtension(".aifc");
            tempFmt.AddExtension(".snd");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Video Game Music");
            tempFmt.ID = CidVgm;
            tempFmt.AddMimeType("audio/vgm"); // Unofficial
            tempFmt.AddMimeType("audio/x-vgm"); // Unofficial
            tempFmt.AddExtension(".vgm");
            tempFmt.AddExtension(".vgz");
            _theFactory.addFormat(tempFmt);

            tempFmt = new Format("Genesis YM2612");
            tempFmt.ID = CidGym;
            tempFmt.AddMimeType("audio/gym"); // Unofficial
            tempFmt.AddMimeType("audio/x-gym"); // Unofficial
            tempFmt.AddExtension(".gym");
            _theFactory.addFormat(tempFmt);

            return _theFactory;
        }

        public IAudioDataIO GetFromPath(String path, Int32 alternate = 0)
        {
            var formats = getFormatsFromPath(path);
            var formatId = NO_FORMAT;

            if (formats != null && formats.Count > alternate)
            {
                formatId = formats[alternate].ID;
            }

            IAudioDataIO theDataReader = null;

            switch (formatId)
            {
                case CidMp3:
                    theDataReader = new IO.MPEGaudio(path);
                    break;
                case CidAac:
                    theDataReader = new IO.AAC(path);
                    break;
                case CidWma:
                    theDataReader = new IO.WMA(path);
                    break;
                case CidOgg:
                    theDataReader = new IO.Ogg(path);
                    break;
                case CidFlac:
                    theDataReader = new IO.FLAC(path);
                    break;
                case CidMpc:
                    theDataReader = new IO.MPEGplus(path);
                    break;
                case CidAc3:
                    theDataReader = new IO.AC3(path);
                    break;
                case CidDsf:
                    theDataReader = new IO.DSF(path);
                    break;
                case CidDts:
                    theDataReader = new IO.DTS(path);
                    break;
                case CidIt:
                    theDataReader = new IO.IT(path);
                    break;
                case CidMidi:
                    theDataReader = new IO.Midi(path);
                    break;
                case CidMod:
                    theDataReader = new IO.MOD(path);
                    break;
                case CidApe:
                    theDataReader = new IO.APE(path);
                    break;
                case CidOfr:
                    theDataReader = new IO.OptimFrog(path);
                    break;
                case CidWavpack:
                    theDataReader = new IO.WAVPack(path);
                    break;
                case CidWav:
                    theDataReader = new IO.WAV(path);
                    break;
                case CidPsf:
                    theDataReader = new IO.PSF(path);
                    break;
                case CidSpc:
                    theDataReader = new IO.SPC(path);
                    break;
                case CidTak:
                    theDataReader = new IO.TAK(path);
                    break;
                case CidS3M:
                    theDataReader = new IO.S3M(path);
                    break;
                case CidXm:
                    theDataReader = new IO.XM(path);
                    break;
                case CidTta:
                    theDataReader = new IO.TTA(path);
                    break;
                case CidVqf:
                    theDataReader = new IO.TwinVQ(path);
                    break;
                case CidAiff:
                    theDataReader = new IO.AIFF(path);
                    break;
                case CidVgm:
                    theDataReader = new IO.VGM(path);
                    break;
                case CidGym:
                    theDataReader = new IO.GYM(path);
                    break;
                default:
                    theDataReader = new IO.DummyReader(path);
                    break;
            }

            return theDataReader;
        }

        public IAudioDataIO GetFromMimeType(String mimeType, String path, Int32 alternate = 0)
        {
            IList<Format> formats;
            if (mimeType.StartsWith(".")) formats = getFormatsFromPath(mimeType);
            else formats = getFormatsFromMimeType(mimeType);

            var formatId = NO_FORMAT;

            if (formats != null && formats.Count > alternate)
            {
                formatId = formats[alternate].ID;
            }

            IAudioDataIO theDataReader = null;

            switch (formatId)
            {
                case CidMp3:
                    theDataReader = new IO.MPEGaudio(path);
                    break;
                case CidAac:
                    theDataReader = new IO.AAC(path);
                    break;
                case CidWma:
                    theDataReader = new IO.WMA(path);
                    break;
                case CidOgg:
                    theDataReader = new IO.Ogg(path);
                    break;
                case CidFlac:
                    theDataReader = new IO.FLAC(path);
                    break;
                case CidMpc:
                    theDataReader = new IO.MPEGplus(path);
                    break;
                case CidAc3:
                    theDataReader = new IO.AC3(path);
                    break;
                case CidDsf:
                    theDataReader = new IO.DSF(path);
                    break;
                case CidDts:
                    theDataReader = new IO.DTS(path);
                    break;
                case CidIt:
                    theDataReader = new IO.IT(path);
                    break;
                case CidMidi:
                    theDataReader = new IO.Midi(path);
                    break;
                case CidMod:
                    theDataReader = new IO.MOD(path);
                    break;
                case CidApe:
                    theDataReader = new IO.APE(path);
                    break;
                case CidOfr:
                    theDataReader = new IO.OptimFrog(path);
                    break;
                case CidWavpack:
                    theDataReader = new IO.WAVPack(path);
                    break;
                case CidWav:
                    theDataReader = new IO.WAV(path);
                    break;
                case CidPsf:
                    theDataReader = new IO.PSF(path);
                    break;
                case CidSpc:
                    theDataReader = new IO.SPC(path);
                    break;
                case CidTak:
                    theDataReader = new IO.TAK(path);
                    break;
                case CidS3M:
                    theDataReader = new IO.S3M(path);
                    break;
                case CidXm:
                    theDataReader = new IO.XM(path);
                    break;
                case CidTta:
                    theDataReader = new IO.TTA(path);
                    break;
                case CidVqf:
                    theDataReader = new IO.TwinVQ(path);
                    break;
                case CidAiff:
                    theDataReader = new IO.AIFF(path);
                    break;
                case CidVgm:
                    theDataReader = new IO.VGM(path);
                    break;
                case CidGym:
                    theDataReader = new IO.GYM(path);
                    break;
                default:
                    theDataReader = new IO.DummyReader(path);
                    break;
            }

            return theDataReader;
        }
    }
}