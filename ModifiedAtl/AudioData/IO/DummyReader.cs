using System;
using System.IO;

namespace ATL.AudioData.IO
{
	/// <summary>
	/// Dummy audio data provider
	/// </summary>
	public class DummyReader : IAudioDataIO
	{
        private String filePath = "";

        public DummyReader(String filePath)
        {
            Logging.LogDelegator.GetLogDelegate()(Logging.Log.LV_DEBUG, "Instancing a Dummy Audio Data Reader for " + filePath);
            this.filePath = filePath;
        }

        public String FileName => filePath;

        public Double BitRate => 0;

        public Double Duration => 0;

        public Int32 SampleRate => 0;

        public Boolean IsVBR => false;

        public Int32 CodecFamily => AudioDataIoFactory.CfLossy;

        public ID3v1 ID3v1 => new ID3v1();

        public ID3v2 ID3v2 => new ID3v2();

        public APEtag APEtag => new APEtag();

        public IMetaDataIO NativeTag => new DummyTag();

        public Boolean RemoveTagFromFile(Int32 tagType)
        {
            return true;
        }
        public Boolean AddTagToFile(Int32 tagType)
        {
            return true;
        }
        public Boolean UpdateTagInFile(TagData theTag, Int32 tagType)
        {
            return true;
        }
        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return true;
        }
        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams)
        {
            return true;
        }
    }
}
