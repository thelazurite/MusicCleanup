using System;
using ATL.AudioData.IO;
using System.IO;

namespace ATL.AudioData
{
	/// <summary>
	/// This Interface defines an object aimed at giving audio "physical" data information
	/// </summary>
	public interface IAudioDataIO
	{
        /// <summary>
        /// Full access path of the underlying file
        /// </summary>
        String FileName
        {
            get;
        }
        /// <summary>
        /// Bitrate (kilobytes per second)
        /// </summary>
        Double BitRate
		{
			get;
		}
		/// <summary>
		/// Duration (milliseconds)
		/// </summary>
		Double Duration
		{
			get;
		}
        /// <summary>
		/// Sample rate (Hz)
		/// </summary>
		Int32 SampleRate
        {
            get;
        }
        /// <summary>
        /// Returns true if the bitrate is variable; false if not
        /// </summary>
        Boolean IsVBR
		{
			get;
		}
        /// <summary>
        /// Family of the audio codec (see AudioDataIOFactory for the list of codec families)
        /// </summary>
        Int32 CodecFamily
		{
			get;
		}
        
        /// <summary>
        /// Indicated wether the given metadata type is supported
        /// </summary>
        /// <param name="metaDataType">Metadata type to be tested (see list in MetaDataIOFactory)</param>
        /// <returns>True if current file supports the given metadata type; false if not</returns>
        Boolean IsMetaSupported(Int32 metaDataType);
        
        /// <summary>
        /// Reads audio data from the given stream.
        /// NB1 : Standard metadata (i.e. ID3v2, ID3v1 and APE) have to be read _before_ calling this method, and their size stored in sizeInfo
        /// NB2 : Stream is _not_ closed after reading; resource deallocation has to be done by the caller
        /// </summary>
        /// <param name="source">BinaryReader opened on the stream to read</param>
        /// <param name="sizeInfo">Description of the size of the undelying stream and associated metadata</param>
        /// <param name="readTagParams">Reading parameters and options</param>
        /// <returns>True if the stream has been successfuly read; false if not</returns>
        Boolean Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, MetaDataIO.ReadTagParams readTagParams);
    }
}
