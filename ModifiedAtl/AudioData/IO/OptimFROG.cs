using ATL.Logging;
using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;
using Commons;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for OptimFROG files manipulation (extensions : .OFR, .OFS)
    /// </summary>
	class OptimFrog : IAudioDataIO
	{
        private const String OFR_SIGNATURE = "OFR ";

		private static readonly String[] OFR_COMPRESSION = new String[10] 
		{
			"fast", "normal", "high", "extra",
			"best", "ultra", "insane", "highnew", "extranew", "bestnew"
        };

		private static readonly SByte[] OFR_BITS = new SByte[11] 
	    {
		    8, 8, 16, 16, 24, 24, 32, 32,
		    -32, -32, -32 }; //negative value corresponds to floating point type.

		private static readonly String[] OFR_CHANNELMODE = new String[2] {"Mono", "Stereo"};

					
		// Real structure of OptimFROG header
		public class TOfrHeader
		{
			public Char[] ID = new Char[4];                      // Always 'OFR '
			public UInt32 Size;
			public UInt32 Length;
			public UInt16 HiLength;
			public Byte SampleType;
			public Byte ChannelMode;
			public Int32 SampleRate;
			public UInt16 EncoderID;
			public Byte CompressionID;
			public void Reset()
			{
				Array.Clear(ID,0,ID.Length);
				Size = 0;
				Length = 0;
				HiLength = 0;
				SampleType = 0;
				ChannelMode = 0;
				SampleRate = 0;
				EncoderID = 0;
				CompressionID = 0;
			}
		}

    
		private TOfrHeader header = new TOfrHeader();

        private Double bitrate;
        private Double duration;

        private SizeInfo sizeInfo;
        private readonly String filePath;

        /* Unused for now
        public TOfrHeader Header // OptimFROG header
		{
			get { return this.FHeader; }
		}	

        public String Version // Encoder version
		{
			get { return this.getVersion(); }
		}									   
		public String Compression // Compression level
		{
			get { return this.getCompression(); }
		}	
		public String ChannelMode // Channel mode
		{
			get { return this.getChannelMode(); }
		}
		public sbyte Bits // Bits per sample
		{
			get { return this.getBits(); }
		}		  		
		public long Samples // Number of samples
		{
			get { return this.getSamples(); }
		}
        public double CompressionRatio // Compression ratio (%)
        {
            get { return this.getCompressionRatio(); }
        }
        */


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Int32 SampleRate // Sample rate (Hz)
	        =>
		        getSampleRate();

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossless;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_APE) || (metaDataType == MetaDataIOFactory.TAG_ID3V1) || (metaDataType == MetaDataIOFactory.TAG_ID3V2);
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        private void resetData()
        {
            duration = 0;
            bitrate = 0;

            header.Reset();
		}

        public OptimFrog(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }


        // ---------- SUPPORT METHODS

        /* Unused for now
    private bool getValid()
    {
        return (
            StreamUtils.StringEqualsArr("OFR ", FHeader.ID) &&
            (FHeader.SampleRate > 0) &&
            ((0 <= FHeader.SampleType) && (FHeader.SampleType <= 10)) &&
            ((0 <= FHeader.ChannelMode) &&(FHeader.ChannelMode <= 1)) &&
            ((0 <= FHeader.CompressionID >> 3) && (FHeader.CompressionID >> 3 <= 9)) );
    }

    private String getVersion()
    {
        // Get encoder version
        return  ( ((FHeader.EncoderID >> 4) + 4500) / 1000 ).ToString().Substring(0,5); // Pas exactement...
    }

    private String getCompression()
    {
        // Get compression level
        return OFR_COMPRESSION[FHeader.CompressionID >> 3];
    }

    private sbyte getBits()
    {
        // Get number of bits per sample
        return OFR_BITS[FHeader.SampleType];
    }

    private String getChannelMode()
    {
        // Get channel mode
        return OFR_CHANNELMODE[FHeader.ChannelMode];
    }

    private double getCompressionRatio()
    {
        // Get compression ratio
        if (getValid())
            return sizeInfo.FileSize /
                (getSamples() * (header.ChannelMode+1) * Math.Abs(getBits()) / 8 + 44) * 100;
        else
            return 0;
    }

    */

        // Get number of samples
        private Int64 getSamples()
		{
			return ( ((header.Length >> header.ChannelMode) * 0x00000001) +
				((header.HiLength >> header.ChannelMode) * 0x00010000) );
		}

        // Get song duration
        private Double getDuration()
		{
			if (header.SampleRate > 0)
				return (Double)getSamples() * 1000.0 / header.SampleRate;
			else
				return 0;
		}

		private Int32 getSampleRate()
		{
			return header.SampleRate;
		}

        private Double getBitrate()
        {
            return ((sizeInfo.FileSize - header.Size - sizeInfo.TotalTagSize) * 8 / Duration);
        }

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            var result = false;
            this.sizeInfo = sizeInfo;
            resetData();

			// Read header data
			source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

			header.ID = source.ReadChars(4);
			header.Size = source.ReadUInt32();
			header.Length = source.ReadUInt32();
			header.HiLength = source.ReadUInt16();
			header.SampleType = source.ReadByte();
			header.ChannelMode = source.ReadByte();
			header.SampleRate = source.ReadInt32();
			header.EncoderID = source.ReadUInt16();
			header.CompressionID = source.ReadByte();

            if (StreamUtils.StringEqualsArr(OFR_SIGNATURE, header.ID))
            {
                result = true;
                duration = getDuration();
                bitrate = getBitrate();
            }

			return result;
		}

	}
}