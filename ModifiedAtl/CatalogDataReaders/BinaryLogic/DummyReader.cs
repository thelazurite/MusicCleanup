using System;
using System.Collections.Generic;
using System.Text;

namespace ATL.CatalogDataReaders.BinaryLogic
{
    public class DummyReader : ICatalogDataReader
    {
        String path = "";

        public String Path
        {
            get => path;
            set => path = value;
        }

        public String Title => "";

        public String Artist => "";

        public String Comments => "";

        public IList<Track> Tracks => new List<Track>();
    }
}
