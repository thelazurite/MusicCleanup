using System;

namespace ATL.AudioData
{
    public class AudioDataCorruptionException : Exception
    {
        public AudioDataCorruptionException(String message, Exception innerException):
            base(message, innerException)
        {
        }
    }
}