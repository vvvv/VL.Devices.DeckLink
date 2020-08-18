using DeckLinkAPI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VL.Devices.DeckLink
{
    sealed class BGRAVideoOutputFrame : IDeckLinkVideoFrame, IDisposable
    {
        readonly int Width, Height;
        readonly IntPtr Bytes;

        public BGRAVideoOutputFrame(int width, int height)
        {
            Width = width;
            Height = height;
            Bytes = Marshal.AllocHGlobal(width * height * 4);
        }

        public int GetWidth() => Width;

        public int GetHeight() => Height;

        public int GetRowBytes() => Width * 4;

        public _BMDPixelFormat GetPixelFormat() => _BMDPixelFormat.bmdFormat8BitBGRA;

        public _BMDFrameFlags GetFlags() => _BMDFrameFlags.bmdFrameFlagDefault;

        public void GetBytes(out IntPtr buffer)
        {
            buffer = Bytes;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Bytes);
        }

        public void GetTimecode(_BMDTimecodeFormat format, out IDeckLinkTimecode timecode)
        {
            throw new NotImplementedException();
        }

        public void GetAncillaryData(out IDeckLinkVideoFrameAncillary ancillary)
        {
            throw new NotImplementedException();
        }
    }
}
