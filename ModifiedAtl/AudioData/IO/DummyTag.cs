using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Dummy metadata provider
    /// </summary>
    public class DummyTag : IMetaDataIO
    {
        public DummyTag()
        {
            Logging.LogDelegator.GetLogDelegate()(Logging.Log.LV_DEBUG, "Instancing a Dummy Meta Data Reader");
        }

        public Boolean Exists => true;

        public String Title => "";

        public String Artist => "";

        public String Composer => "";

        public String Comment => "";

        public String Genre => "";

        public UInt16 Track => 0;

        public UInt16 Disc => 0;

        public String Year => "";

        public String Album => "";

        public UInt16 Rating => 0;

        public Single Popularity => 0;

        public Int32 Size => 0;

        public IList<PictureInfo> PictureTokens => new List<PictureInfo>();

        public String Copyright => "";

        public String OriginalArtist => "";

        public String OriginalAlbum => "";

        public String GeneralDescription => "";

        public String Publisher => "";

        public String AlbumArtist => "";

        public String Conductor => "";

        public IDictionary<String, String> AdditionalFields => new Dictionary<String, String>();

        public IList<ChapterInfo> Chapters => new List<ChapterInfo>();

        public IList<PictureInfo> EmbeddedPictures => new List<PictureInfo>();

        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            return true;
        }

        public Boolean Read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            throw new NotImplementedException();
        }

        public Boolean Remove(BinaryWriter w)
        {
            throw new NotImplementedException();
        }

        public void SetEmbedder(IMetaDataEmbedder embedder)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }
    }
}