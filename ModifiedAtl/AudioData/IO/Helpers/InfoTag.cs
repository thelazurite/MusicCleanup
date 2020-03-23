using System;
using Commons;
using System.Collections.Generic;
using System.IO;
using ATL.Logging;
using static ATL.AudioData.IO.MetaDataIO;

namespace ATL.AudioData.IO
{
    public static class InfoTag
    {
        public const String CHUNK_LIST = "LIST";
        public const String PURPOSE_INFO = "INFO";

        public static void FromStream(Stream source, MetaDataIO meta, ReadTagParams readTagParams, UInt32 chunkSize)
        {
            var position = source.Position;
            var initialPos = position;
            String key, value;
            Int32 size;
            var data = new Byte[256];

            while (source.Position < initialPos + chunkSize - 4) // 4 being the "INFO" purpose that belongs to the chunk
            {
                // Key
                source.Read(data, 0, 4);
                key = Utils.Latin1Encoding.GetString(data, 0, 4);
                // Size
                source.Read(data, 0, 4);
                size = StreamUtils.DecodeInt32(data);
                // Value
                value = StreamUtils.ReadNullTerminatedString(source, Utils.Latin1Encoding);

                if (value.Length > 0) meta.SetMetaField("info." + key, value, readTagParams.ReadAllMetaFrames);

                position = source.Position;
            }
        }

        public static Boolean IsDataEligible(MetaDataIO meta)
        {
            if (meta.Title.Length > 0) return true;
            if (meta.Artist.Length > 0) return true;
            if (meta.Comment.Length > 0) return true;
            if (meta.Genre.Length > 0) return true;
            if (meta.Copyright.Length > 0) return true;

            foreach (var key in meta.AdditionalFields.Keys)
            {
                if (key.StartsWith("info.")) return true;
            }

            return false;
        }

        public static Int32 ToStream(BinaryWriter w, Boolean isLittleEndian, MetaDataIO meta)
        {
            var additionalFields = meta.AdditionalFields;
            w.Write(Utils.Latin1Encoding.GetBytes(CHUNK_LIST));

            var sizePos = w.BaseStream.Position;
            w.Write((Int32)0); // Placeholder for chunk size that will be rewritten at the end of the method

            w.Write(Utils.Latin1Encoding.GetBytes(PURPOSE_INFO));

            // 'Classic' fields (NB : usually done within a loop by accessing MetaDataIO.tagData)
            IDictionary<String, String> writtenFields = new Dictionary<String, String>();
            // Title
            var value = Utils.ProtectValue(meta.Title);
            if (0 == value.Length && additionalFields.Keys.Contains("info.INAM")) value = additionalFields["info.INAM"];
            if (value.Length > 0) writeSizeAndNullTerminatedString("INAM", value, w, writtenFields);
            // Artist
            value = Utils.ProtectValue(meta.Artist);
            if (0 == value.Length && additionalFields.Keys.Contains("info.IART")) value = additionalFields["info.IART"];
            if (value.Length > 0) writeSizeAndNullTerminatedString("IART", value, w, writtenFields);
            // Copyright
            value = Utils.ProtectValue(meta.Copyright);
            if (0 == value.Length && additionalFields.Keys.Contains("info.ICOP")) value = additionalFields["info.ICOP"];
            if (value.Length > 0) writeSizeAndNullTerminatedString("ICOP", value, w, writtenFields);
            // Genre
            value = Utils.ProtectValue(meta.Genre);
            if (0 == value.Length && additionalFields.Keys.Contains("info.IGNR")) value = additionalFields["info.IGNR"];
            if (value.Length > 0) writeSizeAndNullTerminatedString("IGNR", value, w, writtenFields);
            // Comment
            value = Utils.ProtectValue(meta.Comment);
            if (0 == value.Length && additionalFields.Keys.Contains("info.ICMT")) value = additionalFields["info.ICMT"];
            if (value.Length > 0) writeSizeAndNullTerminatedString("ICMT", value, w, writtenFields);

            String shortKey;
            foreach(var key in additionalFields.Keys)
            {
                if (key.StartsWith("info."))
                {
                    shortKey = key.Substring(5, key.Length - 5).ToUpper();
                    if (!writtenFields.ContainsKey(key))
                    {
                        if (additionalFields[key].Length > 0) writeSizeAndNullTerminatedString(shortKey, additionalFields[key], w, writtenFields);
                    }
                }
            }

            var finalPos = w.BaseStream.Position;
            w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            if (isLittleEndian)
            {
                w.Write((Int32)(finalPos - sizePos - 4));
            }
            else
            {
                w.Write(StreamUtils.EncodeBEInt32((Int32)(finalPos - sizePos - 4)));
            }

            return 14;
        }

        private static void writeSizeAndNullTerminatedString(String key, String value, BinaryWriter w, IDictionary<String, String> writtenFields)
        {
            if (key.Length > 4)
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "'" + key + "' : LIST.INFO field key must be 4-characters long; cropping");
                key = Utils.BuildStrictLengthString(key, 4, ' ');
            } else if (key.Length < 4)
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "'" + key + "' : LIST.INFO field key must be 4-characters long; completing with whitespaces");
                key = Utils.BuildStrictLengthString(key, 4, ' ');
            }
            w.Write(Utils.Latin1Encoding.GetBytes(key));

            var buffer = Utils.Latin1Encoding.GetBytes(value);
            w.Write(buffer.Length);
            w.Write(buffer);
            w.Write((Byte)0); // String is null-terminated

            writtenFields.Add("info."+key, value);
        }
    }
}
