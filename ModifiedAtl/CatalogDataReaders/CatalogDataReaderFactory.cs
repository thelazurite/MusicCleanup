using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.CatalogDataReaders
{
	/// <summary>
	/// Factory for Catalog data readers
	/// </summary>
    public class CatalogDataReaderFactory : ReaderFactory
	{
		// Defines the supported formats
        public const Int32 CR_CUE     = 0;

		// The instance of this factory
		private static CatalogDataReaderFactory theFactory = null;


		public static CatalogDataReaderFactory GetInstance()
		{
			if (null == theFactory)
			{
				theFactory = new CatalogDataReaderFactory();
                theFactory.formatListByExt = new Dictionary<String, IList<Format>>();

                var tempFmt = new Format("CUE sheet");
                tempFmt.ID = CR_CUE;
                tempFmt.AddExtension(".cue");
                theFactory.addFormat(tempFmt);
			}

			return theFactory;
		}

        public ICatalogDataReader GetCatalogDataReader(String path, Int32 alternate = 0)
		{
            var formats = getFormatsFromPath(path);
            ICatalogDataReader result;

            if (formats != null && formats.Count > alternate)
            {
                result = GetCatalogDataReader(formats[alternate].ID, path);
            }
            else
            {
                result = GetCatalogDataReader(NO_FORMAT);
            }

            result.Path = path;
            return result;
        }

        private ICatalogDataReader GetCatalogDataReader(Int32 formatId, String path = "")
        {
            ICatalogDataReader theReader = null;

            if (CR_CUE == formatId)
            {
                theReader = new BinaryLogic.Cue(path); //new BinaryLogic.CueAdapter();
			}

            if (null == theReader) theReader = new BinaryLogic.DummyReader();

			return theReader;
		}
	}
}
