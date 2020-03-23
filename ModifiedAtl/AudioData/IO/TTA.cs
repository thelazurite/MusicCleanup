using System;
using ATL.Logging;
using System.IO;
using static ATL.AudioData.AudioDataManager;
using Commons;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for True Audio files manipulation (extensions : .TTA)
    /// </summary>
	class TTA : IAudioDataIO
	{
        private const String TTA_SIGNATURE = "TTA1";

		// Private declarations
		private UInt32 audioFormat;
		private UInt32 channels;
		private UInt32 bitsPerSample;
		private UInt32 sampleRate;
		private UInt32 samplesSize;
		private UInt32 cRC32;

        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private readonly String filePath;


        // Public declarations    
        public UInt32 Channels => channels;

        public UInt32 Bits => bitsPerSample;

        public Double CompressionRatio => getCompressionRatio();

        public UInt32 Samples // Number of samples
	        =>
		        samplesSize;

        public UInt32 CRC32 => CRC32;

        public UInt32 AudioFormat => audioFormat;


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Int32 SampleRate => (Int32)sampleRate;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

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
            isValid = false;

            audioFormat = 0;
			channels = 0;
			bitsPerSample = 0;
			sampleRate = 0;
			samplesSize = 0;
			cRC32 = 0;
		}

		public TTA(String filePath)
        {
            this.filePath = filePath;
            resetData();
        }

        
        // ---------- SUPPORT METHODS

        private Double getCompressionRatio()
        {
            // Get compression ratio
            if (isValid)
                return (Double)sizeInfo.FileSize / (samplesSize * (channels * bitsPerSample / 8) + 44) * 100;
            else
                return 0;
        }

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
			var signatureChunk = new Char[4];

            this.sizeInfo = sizeInfo;
            resetData();
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            var result = false;
    
			if (TTA_SIGNATURE.Equals( Utils.Latin1Encoding.GetString(source.ReadBytes(4)) ))
			{
                isValid = true;

                audioFormat = source.ReadUInt16();
				channels = source.ReadUInt16();
				bitsPerSample = source.ReadUInt16();
				sampleRate = source.ReadUInt32();
				samplesSize = source.ReadUInt32();
				cRC32 = source.ReadUInt32();

				bitrate = (Double)(sizeInfo.FileSize - sizeInfo.TotalTagSize) * 8.0 / ((Double)samplesSize  * 1000.0 / sampleRate);
				duration = (Double)samplesSize * 1000.0 / sampleRate;

				result = true;
			}
  
			return result;
		}


	}
}
