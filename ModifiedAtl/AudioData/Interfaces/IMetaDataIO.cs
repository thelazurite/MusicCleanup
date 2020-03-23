using ATL.AudioData.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.AudioData
{
	/// <summary>
	/// This Interface defines an object aimed at giving audio metadata information
	/// </summary>
	public interface IMetaDataIO
	{
		/// <summary>
		/// Returns true if this kind of metadata exists in the file, false if not
		/// </summary>
		Boolean Exists
		{
			get;
		}
		/// <summary>
		/// Title of the track
		/// </summary>
		String Title
		{
			get;
		}
		/// <summary>
		/// Artist
		/// </summary>
		String Artist
		{
			get;
		}
        /// <summary>
        /// Composer
        /// </summary>
        String Composer
        {
            get;
        }
		/// <summary>
		/// Comments
		/// </summary>
		String Comment
		{
			get;
		}
		/// <summary>
		/// Genre
		/// </summary>
		String Genre
		{
			get;
		}
		/// <summary>
		/// Track number
		/// </summary>
		UInt16 Track
		{
			get;
		}
		/// <summary>
		/// Disc number
		/// </summary>
		UInt16 Disc
		{
			get;
		}
		/// <summary>
		/// Year
		/// </summary>
		String Year
		{
			get;
		}
		/// <summary>
		/// Title of the album
		/// </summary>
		String Album
		{
			get;
		}
        /// <summary>
        /// Rating of the track, from 1 to 5
        /// </summary>
        [Obsolete("Use Popularity")]
        UInt16 Rating
        {
            get;
        }
        /// <summary>
        /// Rating of the track, from 0% to 100%
        /// </summary>
        Single Popularity
        {
            get;
        }
        /// <summary>
        /// Copyright
        /// </summary>
        String Copyright
        {
            get;
        }
        /// <summary>
        /// Original artist
        /// </summary>
        String OriginalArtist
        {
            get;
        }
        /// <summary>
        /// Title of the original album
        /// </summary>
        String OriginalAlbum
        {
            get;
        }
        /// <summary>
        /// General description
        /// </summary>
        String GeneralDescription
        {
            get;
        }
        /// <summary>
        /// Publisher
        /// </summary>
        String Publisher
        {
            get;
        }
        /// <summary>
        /// Album Artist
        /// </summary>
        String AlbumArtist
        {
            get;
        }
        /// <summary>
        /// Conductor
        /// </summary>
        String Conductor
        {
            get;
        }

        /// <summary>
        /// List of picture IDs stored in the tag
        ///     PictureInfo.PIC_TYPE : internal, normalized picture type
        ///     PictureInfo.NativePicCode : native picture code (useful when exploiting the UNSUPPORTED picture type)
        ///     NB : PictureInfo.PictureData (raw binary picture data) is _not_ valued here; see EmbeddedPictures field
        /// </summary>
        IList<PictureInfo> PictureTokens
        {
            get;
        }
        /// <summary>
        /// Physical size of the tag (bytes)
        /// </summary>
        Int32 Size
        {
            get;
        }
        /// <summary>
        /// Contains any other metadata field that is not represented by a getter in the above interface
        /// </summary>
        IDictionary<String, String> AdditionalFields
        {
            get;
        }
        /// <summary>
        /// Contains any other metadata field that is not represented by a getter in the above interface
        /// </summary>
        IList<ChapterInfo> Chapters
        {
            get;
        }

        /// <summary>
        /// List of pictures stored in the tag
        /// NB : PictureInfo.PictureData (raw binary picture data) is valued
        /// </summary>
        IList<PictureInfo> EmbeddedPictures
        {
            get;
        }

        /// <summary>
        /// Set metadata to be written using the given embedder
        /// </summary>
        /// <param name="embedder">Metadata embedder to be used to write current metadata</param>
        void SetEmbedder(IMetaDataEmbedder embedder);

        /// <summary>
        /// Parse binary data read from the given stream
        /// </summary>
        /// <param name="source">Reader to parse data from</param>
        /// <param name="readTagParams">Tag reading parameters</param>
        /// <returns>true if the operation suceeded; false if not</returns>
        Boolean Read(BinaryReader source, MetaDataIO.ReadTagParams readTagParams);

        /// <summary>
        /// Add the specified information to current tag information :
        ///   - Any existing field is overwritten
        ///   - Any non-specified field is kept as is
        /// </summary>
        /// <param name="r">Reader to the resource to edit</param>
        /// <param name="w">Writer to the resource to edit</param>
        /// <param name="tag">Tag information to be added</param>
        /// <returns>true if the operation suceeded; false if not</returns>
        Boolean Write(BinaryReader r, BinaryWriter w, TagData tag);

        /// <summary>
        /// Remove current tag
        /// </summary>
        /// <param name="w">Writer to the resource to edit</param>
        /// <returns>true if the operation suceeded; false if not</returns>
        Boolean Remove(BinaryWriter w);

        /// <summary>
        /// Clears all metadata
        /// </summary>
        void Clear();
    }
}
