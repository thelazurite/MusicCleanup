using ATL.AudioData.IO;
using ATL.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.AudioData
{
    /// <summary>
    /// This class is the one which is _really_ called when encountering a file.
    /// It calls AudioReaderFactory and queries AudioDataReader/MetaDataReader to provide physical 
    /// _and_ meta information about the given file.
    /// </summary>
    internal class AudioFileIo : IMetaDataIO, IAudioDataIO
    {
        private readonly IAudioDataIO _audioData; // Audio data reader used for this file
        private readonly IMetaDataIO _metaData; // Metadata reader used for this file
        private readonly AudioDataManager _audioManager;

        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="path">Path of the file to be parsed</param>
        /// <param name="readEmbeddedPictures">Embedded pictures will be read if true; ignored if false</param>
        /// <param name="readAllMetaFrames">All metadata frames (including unmapped ones) will be read if true; ignored if false</param>
        public AudioFileIo(String path, Boolean readEmbeddedPictures, Boolean readAllMetaFrames = false)
        {
            Byte alternate = 0;
            var found = false;

            _audioData = AudioDataIoFactory.GetInstance().GetFromPath(path, alternate);
            _audioManager = new AudioDataManager(_audioData);


            found = _audioManager.ReadFromFile(readEmbeddedPictures, readAllMetaFrames);
            while (!found && alternate < AudioDataIoFactory.MaxAlternates)
            {
                alternate++;
                _audioData = AudioDataIoFactory.GetInstance().GetFromPath(path, alternate);
                _audioManager = new AudioDataManager(_audioData);
                found = _audioManager.ReadFromFile(readEmbeddedPictures, readAllMetaFrames);
            }


            _metaData = MetaDataIOFactory.GetInstance().GetMetaReader(_audioManager);

            if (_metaData is DummyTag && (0 == _audioManager.getAvailableMetas().Count))
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "Could not find any metadata");
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="stream">Stream to access in-memory data to be parsed</param>
        /// <param name="mimeType">the file's mime type.</param>
        /// <param name="readEmbeddedPictures">Embedded pictures will be read if true; ignored if false</param>
        /// <param name="readAllMetaFrames">All metadata frames (including unmapped ones) will be read if true; ignored if false</param>
        public AudioFileIo(Stream stream, String mimeType, Boolean readEmbeddedPictures, Boolean readAllMetaFrames = false)
        {
            Byte alternate = 0;
            var found = false;

            _audioData = AudioDataIoFactory.GetInstance().GetFromMimeType(mimeType, "In-memory", alternate);

            _audioManager = new AudioDataManager(_audioData, stream);
            found = _audioManager.ReadFromFile(readEmbeddedPictures, readAllMetaFrames);

            while (!found && alternate < AudioDataIoFactory.MaxAlternates)
            {
                alternate++;
                _audioData = AudioDataIoFactory.GetInstance().GetFromMimeType(mimeType, "In-memory", alternate);
                _audioManager = new AudioDataManager(_audioData, stream);
                found = _audioManager.ReadFromFile(readEmbeddedPictures, readAllMetaFrames);
            }

            _metaData = MetaDataIOFactory.GetInstance().GetMetaReader(_audioManager);

            if (_metaData is DummyTag && (0 == _audioManager.getAvailableMetas().Count))
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "Could not find any metadata");
        }

        public void Save(TagData data)
        {
            var availableMetas = _audioManager.getAvailableMetas();

            if (0 == availableMetas.Count)
            {
                foreach (var i in Settings.DefaultTagsWhenNoMetadata)
                {
                    availableMetas.Add(i);
                }
            }

            foreach (var meta in availableMetas)
            {
                _audioManager.UpdateTagInFile(data, meta);
            }
        }

        public void Remove(Int32 tagType = MetaDataIOFactory.TAG_ANY)
        {
            var metasToRemove = MetaDataIOFactory.TAG_ANY == tagType ? _audioManager.getAvailableMetas() : new List<Int32>() {tagType};

            foreach (var meta in metasToRemove)
            {
                _audioManager.RemoveTagFromFile(meta);
            }
        }

        // ============ FIELD ACCESSORS

        private static String ProcessString(String value)
        {
            return value.Replace(Settings.InternalValueSeparator, Settings.DisplayValueSeparator);
        }

        /// <summary>
        /// Audio file name
        /// </summary>
        public String FileName => _audioData.FileName;

        /// <summary>
        /// Title of the track
        /// </summary>
        public String Title => ProcessString(_metaData.Title);

        /// <summary>
        /// Artist
        /// </summary>
        public String Artist => ProcessString(_metaData.Artist);

        /// <summary>
        /// Composer
        /// </summary>
        public String Composer => ProcessString(_metaData.Composer);

        /// <summary>
        /// Publisher
        /// </summary>
        public String Publisher => ProcessString(_metaData.Publisher);

        /// <summary>
        /// Conductor
        /// </summary>
        public String Conductor => ProcessString(_metaData.Conductor);

        /// <summary>
        /// Album Artist
        /// </summary>
        public String AlbumArtist => ProcessString(_metaData.AlbumArtist);

        /// <summary>
        /// General description
        /// </summary>
        public String GeneralDescription => ProcessString(_metaData.GeneralDescription);

        /// <summary>
        /// Copyright
        /// </summary>
        public String Copyright => ProcessString(_metaData.Copyright);

        /// <summary>
        /// Original artist
        /// </summary>
        public String OriginalArtist => ProcessString(_metaData.OriginalArtist);

        /// <summary>
        /// Original album
        /// </summary>
        public String OriginalAlbum => ProcessString(_metaData.OriginalAlbum);

        /// <summary>
        /// Comments
        /// </summary>
        public String Comment => ProcessString(_metaData.Comment);

        /// <summary>
        /// Flag indicating the presence of embedded pictures
        /// </summary>
        public IList<PictureInfo> PictureTokens => _metaData.PictureTokens;

        /// <summary>
        /// Genre
        /// </summary>
        public String Genre => ProcessString(_metaData.Genre);

        /// <summary>
        /// Track number
        /// </summary>
        public UInt16 Track => _metaData.Track;

        /// <summary>
        /// Disc number
        /// </summary>
        public UInt16 Disc => _metaData.Disc;

        /// <summary>
        /// Year, converted to int
        /// </summary>
        public Int32 IntYear => TrackUtils.ExtractIntYear(_metaData.Year);

        /// <summary>
        /// Album title
        /// </summary>
        public String Album => ProcessString(_metaData.Album);

        /// <summary>
        /// Track duration (seconds), rounded
        /// </summary>
        public Int32 IntDuration => (Int32) Math.Round(_audioData.Duration);

        /// <summary>
        /// Track bitrate (KBit/s), rounded
        /// </summary>
        public Int32 IntBitRate => (Int32) Math.Round(_audioData.BitRate);

        /// <summary>
        /// Track rating
        /// </summary>
        [Obsolete("Use popularity")]
        public UInt16 Rating => _metaData.Rating;

        /// <summary>
        /// Track rating
        /// </summary>
        public Single Popularity => _metaData.Popularity;

        /// <summary>
        /// Codec family
        /// </summary>
        public Int32 CodecFamily => _audioData.CodecFamily;

        /// <summary>
        /// Indicates whether the audio stream is in VBR
        /// </summary>
        public Boolean IsVBR => _audioData.IsVBR;

        /// <summary>
        /// Does the tag exist ?
        /// </summary>
        public Boolean Exists => _metaData.Exists;

        /// <summary>
        /// Year, in its original form
        /// </summary>
        public String Year => _metaData.Year;

        /// <summary>
        /// Track bitrate (Kbit/s)
        /// </summary>
        public Double BitRate => _audioData.BitRate;

        /// <summary>
        /// Sample rate (Hz)
        /// </summary>
        public Int32 SampleRate => _audioData.SampleRate;

        /// <summary>
        /// Track duration (milliseconds)
        /// </summary>
        public Double Duration => _audioData.Duration;

        /// <summary>
        /// Metadata size (bytes)
        /// </summary>
        public Int32 Size => _metaData.Size;

        public IDictionary<String, String> AdditionalFields => _metaData.AdditionalFields;

        public IList<ChapterInfo> Chapters => _metaData.Chapters;

        public IList<PictureInfo> EmbeddedPictures => _metaData.EmbeddedPictures;

        public Boolean IsMetaSupported(Int32 metaDataType)
        {
            return _audioData.IsMetaSupported(metaDataType);
        }

        public Boolean Read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            return _metaData.Read(source, readTagParams);
        }

        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            return _metaData.Write(r, w, tag);
        }

        public Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo,
            MetaDataIO.ReadTagParams readTagParams)
        {
            return _audioData.Read(source, sizeInfo, readTagParams);
        }

        public Boolean Remove(BinaryWriter w)
        {
            return _metaData.Remove(w);
        }

        public void SetEmbedder(IMetaDataEmbedder embedder)
        {
            _metaData.SetEmbedder(embedder);
        }

        public void Clear()
        {
            _metaData.Clear();
        }
    }
}