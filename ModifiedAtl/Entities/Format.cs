using System;
using System.Collections;
using System.Collections.Generic;

namespace ATL
{
	/// <summary>
	/// Describes a file format
	/// </summary>
	public class Format : IEnumerable
	{
		// Name of the format
		protected String fName;
		// ID of the format
		protected Int32 fID;
        // MIME type of the format
        protected IDictionary<String, Int32> mimeList;
        // List of file extensions proper to this format
        protected IDictionary<String,Int32> extList;
		// true if the format is readable by ModifiedAtl
		protected Boolean fReadable;

		
		public IEnumerable<String> Extensions => extList.Keys;
		
		public Format() { }

		public Format(String iName)
		{
            init(iName);
		}

        protected void init(String iName)
        {
            fName = iName;
			fReadable = true;
			extList = new Dictionary<String,Int32>();
            mimeList = new Dictionary<String, Int32>();
        }

        protected void copyFrom(Format iFormat)
        {
            fName = iFormat.fName;
            fID = iFormat.fID;
            fReadable = iFormat.fReadable;
            extList = new Dictionary<String,Int32>(iFormat.extList);
            mimeList = new Dictionary<String, Int32>(iFormat.mimeList);
        }

		public String Name
		{
			get => fName;
			set => fName = value;
		}

		public Int32 ID
		{
			get => fID;
			set => fID = value;
		}

		public Boolean Readable
		{
			get => fReadable;
			set => fReadable = value;
		}

        public ICollection<String> MimeList => mimeList.Keys;

        #region Code for IEnumerable implementation

        // NB : Same principle as in Collection		

        public IEnumerator GetEnumerator() 
		{
			return extList.Keys.GetEnumerator();
		}

        #endregion

        // Adds the extension ext to the extensions list of this Format
        public void AddMimeType(String mimeType)
        {
            if (!mimeList.ContainsKey(mimeType.ToLower()))
                mimeList.Add(mimeType.ToLower(), 0);
        }

        // Tests if the extension ext is a valid extension of the current Format
        public Boolean IsValidMimeType(String mimeType)
        {
            return mimeList.ContainsKey(mimeType.ToLower());
        }

        // Adds the extension ext to the extensions list of this Format
        public void AddExtension(String ext)
		{
			if ( !extList.ContainsKey(ext.ToLower()) )
				extList.Add(ext.ToLower(),0);
		}

		// Tests if the extension ext is a valid extension of the current Format
		public Boolean IsValidExtension(String ext)
		{
			return extList.ContainsKey(ext.ToLower());
		}
	}
}
