using System;
using System.Text;

namespace ATL
{
    public static class Settings
    {
        public static Boolean ID3v2_useExtendedHeaderRestrictions = false;
        public static Boolean ID3v2_alwaysWriteCTOCFrame = true;           // Always write CTOC frame when metadata contain at least one chapter
        public static Boolean ASF_keepNonWMFieldsWhenRemovingTag = false;

        public static Int32 GYM_VGM_playbackRate = 0;                     // Playback rate (Hz) [0 = adjust to song properties]

        public static Boolean EnablePadding = false;                       // Used by OGG container; could be used by ID3v2 in the future

        public static readonly Char InternalValueSeparator = '˵';       // Some obscure unicode character that hopefully won't be used anywhere in an actual tag
        public static Char DisplayValueSeparator = ';';

        public static Boolean ReadAllMetaFrames = true; // If true, default Track behaviour reads all metadata frames, including those not described by IMetaDataIO

        public static Encoding DefaultTextEncoding = Encoding.UTF8;

        // Tag editing preferences : what tagging systems to use when audio file has no metadata ?
        // NB1 : If more than one item, _all_ of them will be written
        // NB2 : If Native tagging is not indicated here, it will _not_ be used
        public static Int32[] DefaultTagsWhenNoMetadata = { AudioData.MetaDataIOFactory.TAG_ID3V2, AudioData.MetaDataIOFactory.TAG_NATIVE };

        public static Boolean UseFileNameWhenNoTitle = true;               // If true, file name (without the extension) will go to the Title field if metadata contains no title
    }
}
