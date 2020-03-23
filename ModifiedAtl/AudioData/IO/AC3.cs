using System;
using System.IO;
using static ATL.AudioData.AudioDataManager;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Dolby Digital files manipulation (extension : .AC3)
    /// </summary>
	class AC3 : IAudioDataIO
    {
        // Standard bitrates (KBit/s)
		private static readonly Int32[] BITRATES = new Int32[19] { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160,
														192, 224, 256, 320, 384, 448, 512, 576, 640 };
 
		// Private declarations 
        /* Unused for now
		private uint channels;
		private uint bits;
        */
		private UInt32 sampleRate;

        private Double bitrate;
        private Double duration;

        private SizeInfo sizeInfo;
        private readonly String filePath;


        // Public declarations 
        /* Unused for now
        public uint Channels
		{
			get { return channels; }
		}
		public uint Bits
		{
			get { return bits; }
		}
        public double CompressionRatio
        {
            get { return getCompressionRatio(); }
        }
        */


        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public String FileName => filePath;

        public Double BitRate => bitrate;

        public Double Duration => duration;

        public Int32 SampleRate => (Int32)sampleRate;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_APE);
        }
        public Int64 HasEmbeddedID3v2 => -2;


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
		{
            /*
			channels = 0;
			bits = 0;
            */
			sampleRate = 0;
            duration = 0;
            bitrate = 0;
		}

		public AC3(String filePath)
		{
            this.filePath = filePath;
            resetData();
        }


        // ---------- SUPPORT METHODS

        /* Unused for now
        private double getCompressionRatio()
        {
            // Get compression ratio 
            if (isValid)
                return (double)sizeInfo.FileSize / ((duration * sampleRate) * (channels * bits / 8) + 44) * 100;
            else
                return 0;
        }
        */

        public Boolean Read(BinaryReader source, SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            UInt16 signatureChunk;
			Byte aByte;
            this.sizeInfo = sizeInfo;
            resetData();

            var result = false;

            source.BaseStream.Seek(0, SeekOrigin.Begin);
            signatureChunk = source.ReadUInt16();
            
			if (30475 == signatureChunk )
			{
				aByte = 0;
		
				source.BaseStream.Seek(2, SeekOrigin.Current);
				aByte = source.ReadByte();

				switch (aByte & 0xC0)
				{
					case 0: sampleRate = 48000; break;
					case 0x40: sampleRate = 44100; break;
					case 0x80: sampleRate = 32000; break;
					default : sampleRate = 0; break;
				}

				bitrate = BITRATES[(aByte & 0x3F) >> 1];

				aByte = 0;

                source.BaseStream.Seek(1, SeekOrigin.Current);
				aByte = source.ReadByte();
                
                /* unused for now
				switch (aByte & 0xE0)
				{
					case 0: channels = 2; break;
					case 0x20: channels = 1; break;
					case 0x40: channels = 2; break;
					case 0x60: channels = 3; break;
					case 0x80: channels = 3; break;
					case 0xA0: channels = 4; break;
					case 0xC0: channels = 4; break;
					case 0xE0: channels = 5; break;
					default : channels = 0; break;
				}

				bits = 16;
                */

				duration = sizeInfo.FileSize * 8.0 / bitrate;

				result = true;
			}
  
			return result;
		}
	}
}