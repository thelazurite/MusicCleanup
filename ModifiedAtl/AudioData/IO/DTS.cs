using System;
using ATL.Logging;
using System.IO;
using static ATL.AudioData.AudioDataManager;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Digital Theatre System files manipulation (extension : .DTS)
    /// </summary>
	class DTS : IAudioDataIO
	{
        // Standard bitrates (KBit/s)
		private static readonly Int32[] BITRATES = new Int32[32] { 32, 56, 64, 96, 112, 128, 192, 224, 256,
														320, 384, 448, 512, 576, 640, 768, 960,
														1024, 1152, 1280, 1344, 1408, 1411, 1472,
														1536, 1920, 2048, 3072, 3840, 0, -1, 1 };

		// Private declarations
		private UInt32 channels;
		private UInt32 bits;
		private UInt32 sampleRate;

        private Double bitrate;
        private Double duration;
        private Boolean isValid;

        private SizeInfo sizeInfo;
        private readonly String filePath;


        // Public declarations
        public UInt32 Channels => channels;

        public UInt32 Bits => bits;

        public Double CompressionRatio => getCompressionRatio();


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public Int32 SampleRate => (Int32)sampleRate;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return false;
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
		{
			channels = 0;
			bits = 0;
			sampleRate = 0;
            bitrate = 0;
            duration = 0;
            isValid = false;
		}

		public DTS(String filePath)
		{
            this.filePath = filePath;
			resetData();
		}

        
        // ---------- SUPPORT METHODS

        private Double getCompressionRatio()
        {
            // Get compression ratio
            if (isValid)
                return (Double)sizeInfo.FileSize / ((duration / 1000.0 * sampleRate) * (channels * bits / 8) + 44) * 100;
            else
                return 0;
        }

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            UInt32 signatureChunk;
            UInt16 aWord;
            Byte[] specDTS;
            var result = false;

            this.sizeInfo = sizeInfo;

            resetData();
       	
			signatureChunk = source.ReadUInt32();
			if ( /*0x7FFE8001*/ 25230975 == signatureChunk ) 
			{
				source.BaseStream.Seek(3, SeekOrigin.Current);
                specDTS = source.ReadBytes(8);

				isValid = true;

				aWord = (UInt16)(specDTS[1] | (specDTS[0] << 8));
		
				switch ((aWord & 0x0FC0) >> 6)
				{
					case 0: channels = 1; break;
					case 1:
					case 2:
					case 3:
					case 4: channels = 2; break;
					case 5:
					case 6: channels = 3; break;
					case 7:
					case 8: channels = 4; break;
					case 9: channels = 5; break;
					case 10:
					case 11:
					case 12: channels = 6; break;
					case 13: channels = 7; break;
					case 14:
					case 15: channels = 8; break;
					default: channels = 0; break;
				}

				switch ((aWord & 0x3C) >> 2)
				{
					case 1: sampleRate = 8000; break;
					case 2: sampleRate = 16000; break;
					case 3: sampleRate = 32000; break;
					case 6: sampleRate = 11025; break;
					case 7: sampleRate = 22050; break;
					case 8: sampleRate = 44100; break;
					case 11: sampleRate = 12000; break;
					case 12: sampleRate = 24000; break;
					case 13: sampleRate = 48000; break;
					default: sampleRate = 0; break;
				}

				aWord = 0;
				aWord = (UInt16)( specDTS[2] | (specDTS[1] << 8) );

				bitrate = (UInt16)BITRATES[(aWord & 0x03E0) >> 5];

				aWord = 0;
				aWord = (UInt16)( specDTS[7] | (specDTS[6] << 8) );

				switch ((aWord & 0x01C0) >> 6) 
				{
					case 0:
					case 1: bits = 16; break;
					case 2:
					case 3: bits = 20; break;
					case 4:
					case 5: bits = 24; break;
					default: bits = 16; break;
				}

				duration = sizeInfo.FileSize * 8.0 / bitrate;

				result = true;
			}    

			return result;
		}

	}
}