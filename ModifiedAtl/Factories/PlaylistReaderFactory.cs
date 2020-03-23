using System;
using System.IO;
using ATL.PlaylistReaders.BinaryLogic;
using System.Collections.Generic;

namespace ATL.PlaylistReaders
{
    /// <summary>
    /// The playlist reader factory. 
    /// </summary>
    public class PlaylistReaderFactory : ReaderFactory
    {
        // Defines the supported formats
        public const Int32 PL_M3U = 0;
        public const Int32 PL_PLS = 1;
        public const Int32 PL_XSPF = 2;
        public const Int32 PL_SMIL = 3;
        public const Int32 PL_ASX = 4;
        public const Int32 PL_B4S = 5;

        // The instance of this factory
        private static PlaylistReaderFactory theFactory = null;


        public static PlaylistReaderFactory GetInstance()
        {
            if (null == theFactory)
            {
                theFactory = new PlaylistReaderFactory();
                theFactory.formatListByExt = new Dictionary<String, IList<Format>>();

                var tempFmt = new Format("PLS");
                tempFmt.ID = PL_PLS;
                tempFmt.AddExtension(".pls");
                theFactory.addFormat(tempFmt);

                tempFmt = new Format("M3U");
                tempFmt.ID = PL_M3U;
                tempFmt.AddExtension(".m3u");
                tempFmt.AddExtension(".m3u8");
                theFactory.addFormat(tempFmt);

                tempFmt = new Format("XSPF (spiff)");
                tempFmt.ID = PL_XSPF;
                tempFmt.AddExtension(".xspf");
                theFactory.addFormat(tempFmt);

                tempFmt = new Format("SMIL");
                tempFmt.ID = PL_SMIL;
                tempFmt.AddExtension(".smil");
                tempFmt.AddExtension(".smi");
                tempFmt.AddExtension(".zpl");
                tempFmt.AddExtension(".wpl");
                theFactory.addFormat(tempFmt);

                tempFmt = new Format("ASX");
                tempFmt.ID = PL_ASX;
                tempFmt.AddExtension(".asx");
                tempFmt.AddExtension(".wax");
                tempFmt.AddExtension(".wvx");
                theFactory.addFormat(tempFmt);

                tempFmt = new Format("B4S");
                tempFmt.ID = PL_B4S;
                tempFmt.AddExtension(".b4s");
                theFactory.addFormat(tempFmt);
            }

            return theFactory;
        }

        public IPlaylistReader GetPlaylistReader(String path, Int32 alternate = 0)
        {
            var formats = getFormatsFromPath(path);
            IPlaylistReader result;

            if (formats != null && formats.Count > alternate)
            {
                result = GetPlaylistReader(formats[alternate].ID);
            }
            else
            {
                result = GetPlaylistReader(NO_FORMAT);
            }

            result.Path = path;
            return result;
        }

        public IPlaylistReader GetPlaylistReader(Int32 formatId)
        {
            IPlaylistReader theReader = null;

            if (PL_PLS == formatId)
            {
                theReader = new PlsReader();
            }
            else if (PL_M3U == formatId)
            {
                theReader = new M3UReader();
            }
            else if (PL_XSPF == formatId)
            {
                theReader = new XspfReader();
            }
            else if (PL_SMIL == formatId)
            {
                theReader = new SmilReader();
            }
            else if (PL_ASX == formatId)
            {
                theReader = new AsxReader();
            }
            else if (PL_B4S == formatId)
            {
                theReader = new B4SReader();
            }

            if (null == theReader) theReader = new DummyReader();

            return theReader;
        }
    }
}