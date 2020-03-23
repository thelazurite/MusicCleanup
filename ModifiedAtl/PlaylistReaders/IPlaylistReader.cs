using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ATL.PlaylistReaders
{
    /// <summary>
    /// Reads all file paths registered in a playlist
    /// </summary>
    public interface IPlaylistReader
    {
        /// <summary>
        /// Absolute path of the playlist file
        /// </summary>
        String Path { get; set; }

        Boolean IsUri { get; set; }

        /// <summary>
        /// Opens the playlist file. 
        /// </summary>
        /// <returns>The result of the asynchronous action</returns>
        Task Open();

        /// <summary>
        /// Gets the absolute paths of all files registered in a playlist
        /// NB : The existence of the files is not checked
        /// </summary>
        /// <returns>An array containing all paths</returns>
        IList<String> FilesList { get; }

        /// <summary>
        /// Saves an updated playlist file
        /// </summary>
        /// <returns></returns>
        Task Save(String newPath = null);

        /// <summary>
        /// Updates the path of a track within the playlist file.
        /// </summary>
        /// <param name="original">the original path, to replace</param>
        /// <param name="updated">the updated path</param>
        void UpdateFile(String original, String updated);
    }
}