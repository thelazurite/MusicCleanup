using System;
using System.Collections.Generic;
using System.Linq;

namespace ATL
{
    /// <summary>
    /// Abstract factory for data readers, containing shared methods and members
    /// </summary>
    public abstract class ReaderFactory
    {
        // ID representing the absence of format
        public const Int32 NO_FORMAT = -1;

        // List of all formats supported by this kind of data reader
        // They are indexed by file extension to speed up matching
        protected IDictionary<String, IList<Format>> formatListByExt;

        // List of all formats supported by this kind of data reader
        // They are indexed by MIME-type to speed up matching
        protected IDictionary<String, IList<Format>> formatListByMime;


        /// <summary>
        /// Adds a format to the supported formats
        /// </summary>
        /// <param name="f">Format to be added</param>
        protected void addFormat(Format f)
        {
            IList<Format> matchingFormats;

            foreach (String ext in f)
            {
                if (!formatListByExt.ContainsKey(ext))
                {
                    matchingFormats = new List<Format>();
                    matchingFormats.Add(f);
                    formatListByExt.Add(ext, matchingFormats);
                }
                else
                {
                    matchingFormats = formatListByExt[ext];
                    matchingFormats.Add(f);
                }
            }

            foreach (var mimeType in f.MimeList)
            {
                if (!formatListByMime.ContainsKey(mimeType))
                {
                    matchingFormats = new List<Format>();
                    matchingFormats.Add(f);
                    formatListByMime.Add(mimeType, matchingFormats);
                }
                else
                {
                    matchingFormats = formatListByMime[mimeType];
                    matchingFormats.Add(f);
                }
            }
        }

        /// <summary>
        /// Gets the valid formats from the given file path, using the file extension as key
        /// </summary>
        /// <param name="path">Path of the file which format to recognize</param>
        /// <returns>List of the valid formats matching the extension of the given file, 
        /// or null if none recognized or the file does not exist</returns>
        protected IList<Format> getFormatsFromPath(String path)
        {
            IList<Format> result = null;
            var extension = path.Substring(path.LastIndexOf('.'), path.Length - path.LastIndexOf('.')).ToLower();

            if (formatListByExt.ContainsKey(extension))
            {
                var formats = formatListByExt[extension];
                if (formats != null && formats.Count > 0)
                {
                    result = formats;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the valid formats from the given MIME-type
        /// </summary>
        /// <param name="mimeType">MIME-type to recognize</param>
        /// <returns>List of the valid formats matching the MIME-type of the given file, 
        /// or null if none recognized</returns>
        protected IList<Format> getFormatsFromMimeType(String mimeType)
        {
            IList<Format> result = null;
            var mime = mimeType.ToLower();

            if (formatListByMime.ContainsKey(mime))
            {
                var formats = formatListByMime[mime];
                if (formats != null && formats.Count > 0)
                {
                    result = formats;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a list of all supported formats
        /// </summary>
        /// <returns>List of all supported formats</returns>
        public ICollection<Format> getFormats()
        {
            var result = new Dictionary<Int32, Format>();
            foreach (var formats in formatListByExt.Values)
            {
                foreach (var f in formats)
                {
                    // Filter duplicates "caused by" indexing formats by extension
                    if (!result.ContainsKey(f.ID)) result.Add(f.ID, f);
                }
            }

            return result.Values;
        }

        private IEnumerable<String> _fmtExtns;
        public IEnumerable<String> FormatExtensions => _fmtExtns ?? (_fmtExtns = Initialize().ToArray());

        private HashSet<String> Initialize()
        {
            var result = new HashSet<String>();
            foreach (var formats in formatListByExt.Values)
            {
                formats.Select(f => f.Extensions).ToList()
                    .ForEach(itm => itm.ToList().ForEach(f => result.Add(f)));
            }

            _fmtExtns = result.ToArray();
            return result;
        }
    }
}