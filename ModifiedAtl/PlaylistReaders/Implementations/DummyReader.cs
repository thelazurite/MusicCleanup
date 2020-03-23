using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ATL.PlaylistReaders.BinaryLogic
{
    public class DummyReader : IPlaylistReader
    {
        public String Path { get; set; }
        public Boolean IsUri { get; set; }

        public Task Open()
        {
            throw new NotImplementedException();
        }

        public IList<String> FilesList { get; }

        public Task Save(String newPath = null)
        {
            throw new NotImplementedException();
        }

        public void UpdateFile(String original, String updated)
        {
            throw new NotImplementedException();
        }
    }
}
