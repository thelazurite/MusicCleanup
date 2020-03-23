using System;
using System.IO;
using System.Text;
using System.Collections;
using ATL.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ATL.PlaylistReaders
{
    public abstract class PlaylistReader : IPlaylistReader
    {
        protected String FFileName; // Path of the playlist file
        protected String[] FileContents;

        public String Path
        {
            get => FFileName;
            set => FFileName = value;
        }

        public Boolean IsUri { get; set; }

        public virtual async Task Open()
        {
            if (!String.IsNullOrWhiteSpace(Path))
                FileContents = await File.ReadAllLinesAsync(Path);
        }

        protected IList<String> Files;

        public abstract IList<String> FilesList { get; }

        public async Task Save(String newPath = null)
        {
            //File.Delete(Path);
            await File.WriteAllLinesAsync(newPath ?? Path, FileContents);
            Path = newPath ?? Path;
        }

        public virtual void UpdateFile(String original, String updated)
        {
            var pos = 0;
            while (pos != -1)
            {
                pos = Array.IndexOf(FileContents, original);
                if (pos >= 0) FileContents[pos] = updated;
            }
        }
    }
}