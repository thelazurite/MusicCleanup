using ATL.AudioData.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.AudioData
{
    /// <summary>
    /// Wrapper for reading multiple tags according to a priority
    /// 
    /// Rule : The first non-empty field of the most prioritized tag becomes the "cross-detected" field
    /// There is no "field blending" across collections (pictures, additional fields) : the first non-empty collection is kept
    /// </summary>
    internal class CrossMetadataReader : IMetaDataIO
    {
        // Contains all IMetaDataIO objects to be read, in priority order (index [0] is the most important)
        private IList<IMetaDataIO> metaReaders = null;

        public CrossMetadataReader(AudioDataManager audioManager, Int32[] tagPriority)
        {
            metaReaders = new List<IMetaDataIO>();

            for (var i = 0; i < tagPriority.Length; i++)
            {
                if ((MetaDataIOFactory.TAG_NATIVE == tagPriority[i]) && (audioManager.HasNativeMeta()) &&
                    (audioManager.NativeTag != null))
                {
                    metaReaders.Add(audioManager.NativeTag);
                }

                if ((MetaDataIOFactory.TAG_ID3V1 == tagPriority[i]) && (audioManager.ID3v1.Exists))
                {
                    metaReaders.Add(audioManager.ID3v1);
                }

                if ((MetaDataIOFactory.TAG_ID3V2 == tagPriority[i]) && (audioManager.ID3v2.Exists))
                {
                    metaReaders.Add(audioManager.ID3v2);
                }

                if ((MetaDataIOFactory.TAG_APE == tagPriority[i]) && (audioManager.APEtag.Exists))
                {
                    metaReaders.Add(audioManager.APEtag);
                }
            }
        }

        /// <summary>
        /// Returns true if this kind of metadata exists in the file, false if not
        /// </summary>
        public Boolean Exists => (metaReaders.Count > 0);

        /// <summary>
        /// Title of the track
        /// </summary>
        public String Title
        {
            get
            {
                var title = "";
                foreach (var reader in metaReaders)
                {
                    title = reader.Title;
                    if (title != "") break;
                }

                return title;
            }
        }

        /// <summary>
        /// Artist
        /// </summary>
        public String Artist
        {
            get
            {
                var artist = "";
                foreach (var reader in metaReaders)
                {
                    artist = reader.Artist;
                    if (artist != "") break;
                }

                return artist;
            }
        }

        /// <summary>
        /// Composer
        /// </summary>
        public String Composer
        {
            get
            {
                var composer = "";
                foreach (var reader in metaReaders)
                {
                    composer = reader.Composer;
                    if (composer != "") break;
                }

                return composer;
            }
        }

        /// <summary>
        /// Comments
        /// </summary>
        public String Comment
        {
            get
            {
                var comment = "";
                foreach (var reader in metaReaders)
                {
                    comment = reader.Comment;
                    if (comment != "") break;
                }

                return comment;
            }
        }

        /// <summary>
        /// Genre
        /// </summary>
        public String Genre
        {
            get
            {
                var genre = "";
                foreach (var reader in metaReaders)
                {
                    genre = reader.Genre;
                    if (genre != "") break;
                }

                return genre;
            }
        }

        /// <summary>
        /// Track number
        /// </summary>
        public UInt16 Track
        {
            get
            {
                UInt16 track = 0;
                foreach (var reader in metaReaders)
                {
                    track = reader.Track;
                    if (track != 0) break;
                }

                return track;
            }
        }

        /// <summary>
        /// Disc number
        /// </summary>
        public UInt16 Disc
        {
            get
            {
                UInt16 disc = 0;
                foreach (var reader in metaReaders)
                {
                    disc = reader.Disc;
                    if (disc != 0) break;
                }

                return disc;
            }
        }

        /// <summary>
        /// Year
        /// </summary>
        public String Year
        {
            get
            {
                var year = "";
                foreach (var reader in metaReaders)
                {
                    year = reader.Year;
                    if (year != "") break;
                }

                return year;
            }
        }

        /// <summary>
        /// Title of the album
        /// </summary>
        public String Album
        {
            get
            {
                var album = "";
                foreach (var reader in metaReaders)
                {
                    album = reader.Album;
                    if (album != "") break;
                }

                return album;
            }
        }

        /// <summary>
        /// Copyright
        /// </summary>
        public String Copyright
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.Copyright;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// Album Arist
        /// </summary>
        public String AlbumArtist
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.AlbumArtist;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// Conductor
        /// </summary>
        public String Conductor
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.Conductor;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// Publisher
        /// </summary>
        public String Publisher
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.Publisher;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// General description
        /// </summary>
        public String GeneralDescription
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.GeneralDescription;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// Original artist
        /// </summary>
        public String OriginalArtist
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.OriginalArtist;
                    if (result != "") break;
                }

                return result;
            }
        }

        /// <summary>
        /// Original album
        /// </summary>
        public String OriginalAlbum
        {
            get
            {
                var result = "";
                foreach (var reader in metaReaders)
                {
                    result = reader.OriginalAlbum;
                    if (result != "") break;
                }

                return result;
            }
        }

        [Obsolete("Use Popularity")]
        public UInt16 Rating
        {
            get
            {
                UInt16 rating = 0;
                foreach (var reader in metaReaders)
                {
                    rating = reader.Rating;
                    if (rating != 0) break;
                }

                return rating;
            }
        }

        public Single Popularity
        {
            get
            {
                Single result = 0;
                foreach (var reader in metaReaders)
                {
                    result = reader.Popularity;
                    if (result != 0) break;
                }

                return result;
            }
        }

        /// <summary>
        /// List of picture IDs stored in the tag
        /// </summary>
        public IList<PictureInfo> PictureTokens
        {
            get
            {
                IList<PictureInfo> pictures = new List<PictureInfo>();
                foreach (var reader in metaReaders)
                {
                    if (reader.PictureTokens.Count <= 0) continue;
                    pictures = reader.PictureTokens;
                    break;
                }

                return pictures;
            }
        }

        /// <summary>
        /// Any other metadata field that is not represented among above getters
        /// </summary>
        public IDictionary<String, String> AdditionalFields
        {
            get
            {
                IDictionary<String, String> result = new Dictionary<String, String>();
                foreach (var reader in metaReaders)
                {
                    var readerAdditionalFields = reader.AdditionalFields;
                    if (readerAdditionalFields.Count > 0)
                    {
                        foreach (var s in readerAdditionalFields.Keys)
                        {
                            if (!result.ContainsKey(s)) result.Add(s, readerAdditionalFields[s]);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Chapters
        /// </summary>
        public IList<ChapterInfo> Chapters
        {
            get
            {
                IList<ChapterInfo> chapters = new List<ChapterInfo>();
                foreach (var reader in metaReaders)
                {
                    if (reader.Chapters != null && reader.Chapters.Count > 0)
                    {
                        foreach (var chapter in reader.Chapters)
                        {
                            chapters.Add(chapter);
                        }

                        break;
                    }
                }

                return chapters;
            }
        }

        /// <summary>
        /// Embedded pictures
        /// </summary>
        public IList<PictureInfo> EmbeddedPictures
        {
            get
            {
                IList<PictureInfo> pictures = new List<PictureInfo>();
                foreach (var reader in metaReaders)
                {
                    if (reader.EmbeddedPictures != null && reader.EmbeddedPictures.Count > 0)
                    {
                        foreach (var picture in reader.EmbeddedPictures)
                        {
                            pictures.Add(picture);
                        }

                        break;
                    }
                }

                return pictures;
            }
        }

        public Int32 Size => throw new NotImplementedException();

        public Boolean Read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams)
        {
            throw new NotImplementedException();
        }

        public Boolean Write(BinaryReader r, BinaryWriter w, TagData tag)
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