using System;
using ATL.AudioData;
using Commons;
using HashDepot;

namespace ATL
{
    public class MetaFieldInfo
    {
        public enum ORIGIN
        {
            Unknown = 0,            // Not valued
            Standard = 1,           // Standard field
            UnmappedStandard = 2,   // Unmapped standard field (e.g. ID3v2 "Mood" field TMOO)
            Comment = 3,            // Comment field with extended property parsed as field code (e.g. ID3v2 COMM)
            CustomStandard = 4,     // Custom field through standard "custom" field (e.g. ID3v2 TXXX)
            Custom = 5              // Custom non-standard field (i.e. any other fancy value written regardless of standard)
        };


        public Int32 TagType;                             // Tag type where the picture originates from
        public String NativeFieldCode;                  // Native field code according to TagType convention
        public UInt16 StreamNumber;                     // Index of the stream the field is attached to (if applicable, i.e. for multi-stream files)
        public String Language;                         // Language the value is written in

        public String Value;                            // Field value
        public String Zone;                             // File zone where the value is supposed to appear (ASF format I'm looking at you...)

        public ORIGIN Origin = ORIGIN.Unknown;          // Origin of field

        public Object SpecificData;                     // Attached data specific to the native format (e.g. AIFx Timestamp and Marker ID)

        public Boolean MarkedForDeletion = false;          // True if the field has to be deleted in the next IMetaDataIO.Write operation

        // ---------------- CONSTRUCTORS

        public MetaFieldInfo(Int32 tagType, String nativeFieldCode, String value = "", UInt16 streamNumber = 0, String language = "", String zone = "")
        {
            TagType = tagType; NativeFieldCode = nativeFieldCode; Value = value; StreamNumber = streamNumber; Language = language; Zone = zone;
        }

        public MetaFieldInfo(MetaFieldInfo info)
        {
            TagType = info.TagType; NativeFieldCode = info.NativeFieldCode; Value = info.Value; StreamNumber = info.StreamNumber; Language = info.Language; Zone = info.Zone; Origin = info.Origin;
        }

        // ---------------- OVERRIDES FOR DICTIONARY STORING & UTILS

        public String ToStringWithoutZone()
        {
            return (100 + TagType).ToString() + NativeFieldCode + Utils.BuildStrictLengthString(StreamNumber.ToString(), 5, '0', false) + Language;
        }

        public override String ToString()
        {
            return (100 + TagType).ToString() + NativeFieldCode + Utils.BuildStrictLengthString(StreamNumber.ToString(), 5, '0', false) + Language + Zone;
        }

        public override Int32 GetHashCode()
        {
            return (Int32)Fnv1a.Hash32(Utils.Latin1Encoding.GetBytes(ToString()));
        }

        public Boolean EqualsWithoutZone(Object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;

            // Actually check the type, should not throw exception from Equals override
            if (obj.GetType() != GetType()) return false;

            // Call the implementation from IEquatable
            return ToStringWithoutZone().Equals(((MetaFieldInfo)obj).ToStringWithoutZone());
        }

        public Boolean EqualsApproximate(MetaFieldInfo obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;

            var result = (MetaDataIOFactory.TAG_ANY == obj.TagType && obj.NativeFieldCode.Equals(NativeFieldCode));
            if (obj.StreamNumber > 0) result = result && (obj.StreamNumber == StreamNumber);
            if (obj.Language.Length > 0) result = result && obj.Language.Equals(Language);

            return result;
        }

        public override Boolean Equals(Object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;

            // Actually check the type, should not throw exception from Equals override
            if (obj.GetType() != GetType()) return false;

            // Call the implementation from IEquatable
            return ToString().Equals(obj.ToString());
        }
    }
}
