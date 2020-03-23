using System;
using ATL.Logging;
using Commons;
using HashDepot;

namespace ATL
{
    public class PictureInfo
    {
        public enum PIC_TYPE { Unsupported = 99, Generic = 1, Front = 2, Back = 3, CD = 4 };

        /// <summary>
        /// Normalized picture type (see enum)
        /// </summary>
        public PIC_TYPE PicType;
        /// <summary>
        /// Native image format
        /// </summary>
        public ImageFormat NativeFormat;
        /// <summary>
        /// Position of the picture among pictures of the same generic type / native code (default 1 if the picture is one of its kind)
        /// </summary>
        public Int32 Position;

        /// <summary>
        /// Tag type where the picture originates from (see MetaDataIOFactory)
        /// </summary>
        public Int32 TagType;
        /// <summary>
        /// Native picture code according to TagType convention (numeric : e.g. ID3v2)
        /// </summary>
        public Int32 NativePicCode;
        /// <summary>
        /// Native picture code according to TagType convention (string : e.g. APEtag)
        /// </summary>
        public String NativePicCodeStr;

        /// <summary>
        /// Picture description
        /// </summary>
        public String Description = "";

        /// <summary>
        /// Binary picture data
        /// </summary>
        public Byte[] PictureData;
        /// <summary>
        /// Hash of binary picture data
        /// </summary>
        public UInt32 PictureHash;

        /// <summary>
        /// True if the field has to be deleted in the next IMetaDataIO.Write operation
        /// </summary>
        public Boolean MarkedForDeletion = false;
        /// <summary>
        /// Freeform value to be used by other parts of the library
        /// </summary>
        public Int32 Flag;

        // ---------------- CONSTRUCTORS

        public PictureInfo(PictureInfo picInfo, Boolean copyPictureData = true)
        {
            PicType = picInfo.PicType;
            NativeFormat = picInfo.NativeFormat;
            Position = picInfo.Position;
            TagType = picInfo.TagType;
            NativePicCode = picInfo.NativePicCode;
            NativePicCodeStr = picInfo.NativePicCodeStr;
            Description = picInfo.Description;
            if (copyPictureData && picInfo.PictureData != null)
            {
                PictureData = new Byte[picInfo.PictureData.Length];
                picInfo.PictureData.CopyTo(PictureData, 0);
            }
            PictureHash = picInfo.PictureHash;
            MarkedForDeletion = picInfo.MarkedForDeletion;
            Flag = picInfo.Flag;
        }
        public PictureInfo(ImageFormat nativeFormat, PIC_TYPE picType, Int32 tagType, Object nativePicCode, Int32 position = 1)
        {
            PicType = picType; NativeFormat = nativeFormat; TagType = tagType; Position = position;
            if (nativePicCode is String)
            {
                NativePicCodeStr = (String)nativePicCode;
                NativePicCode = -1;
            }
            else if (nativePicCode is Byte)
            {
                NativePicCode = (Byte)nativePicCode;
            }
            else if (nativePicCode is Int32)
            {
                NativePicCode = (Int32)nativePicCode;
            }
            else
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "nativePicCode type is not supported; expected byte, int or string; found " + nativePicCode.GetType().Name);
            }
        }
        public PictureInfo(ImageFormat nativeFormat, PIC_TYPE picType, Int32 position = 1) { PicType = picType; NativeFormat = nativeFormat; Position = position; }
        public PictureInfo(ImageFormat nativeFormat, Int32 tagType, Object nativePicCode, Int32 position = 1)
        {
            PicType = PIC_TYPE.Unsupported; NativeFormat = nativeFormat; TagType = tagType; Position = position;
            if (nativePicCode is String)
            {
                NativePicCodeStr = (String)nativePicCode;
                NativePicCode = -1;
            }
            else if (nativePicCode is Byte)
            {
                NativePicCode = (Byte)nativePicCode;
            }
            else if (nativePicCode is Int32)
            {
                NativePicCode = (Int32)nativePicCode;
            }
            else
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING, "nativePicCode type is not supported; expected byte, int or string; found " + nativePicCode.GetType().Name);
            }
        }
        public PictureInfo(ImageFormat nativeFormat, Int32 tagType, Byte nativePicCode, Int32 position = 1) { PicType = PIC_TYPE.Unsupported; NativePicCode = nativePicCode; NativeFormat = nativeFormat; TagType = tagType; Position = position; }
        public PictureInfo(ImageFormat nativeFormat, Int32 tagType, String nativePicCode, Int32 position = 1) { PicType = PIC_TYPE.Unsupported; NativePicCodeStr = nativePicCode; NativePicCode = -1; NativeFormat = nativeFormat; TagType = tagType; Position = position; }


        // ---------------- OVERRIDES FOR DICTIONARY STORING & UTILS

        public override String ToString()
        {
            var result = Utils.BuildStrictLengthString(Position.ToString(), 2, '0', false) + Utils.BuildStrictLengthString(((Int32)PicType).ToString(), 2, '0', false);

            if (PicType.Equals(PIC_TYPE.Unsupported))
            {
                if (NativePicCode > 0)
                    result = result + ((10000000 * TagType) + NativePicCode).ToString();
                else if ((NativePicCodeStr != null) && (NativePicCodeStr.Length > 0))
                    result = result + (10000000 * TagType).ToString() + NativePicCodeStr;
                else
                    LogDelegator.GetLogDelegate()(Log.LV_WARNING, "Non-supported picture detected, but no native picture code found");
            }

            return result;
        }

        public override Int32 GetHashCode()
        {
            return (Int32)Fnv1a.Hash32(Utils.Latin1Encoding.GetBytes(ToString()));
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

        public UInt32 ComputePicHash()
        {
            PictureHash = Fnv1a.Hash32(PictureData);
            return PictureHash;
        }
    }
}
