using System;
using System.Collections.Generic;
using System.Text;
using DeckLinkAPI;
using Stride.Graphics;

namespace VL.Devices.DeckLink
{
    static class EnumExtensions
    {
        public static PixelFormat ToPixelFormat(this _BMDPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case _BMDPixelFormat.bmdFormatUnspecified:
                    return PixelFormat.None;
                case _BMDPixelFormat.bmdFormat8BitYUV:
                    break;
                case _BMDPixelFormat.bmdFormat10BitYUV:
                    break;
                case _BMDPixelFormat.bmdFormat8BitARGB:
                    return PixelFormat.B8G8R8A8_UNorm_SRgb;
                case _BMDPixelFormat.bmdFormat8BitBGRA:
                    return PixelFormat.B8G8R8A8_UNorm_SRgb;
                case _BMDPixelFormat.bmdFormat10BitRGB:
                    break;
                case _BMDPixelFormat.bmdFormat12BitRGB:
                    break;
                case _BMDPixelFormat.bmdFormat12BitRGBLE:
                    break;
                case _BMDPixelFormat.bmdFormat10BitXYZ:
                    break;
                case _BMDPixelFormat.bmdFormat8BitRGBA:
                    return PixelFormat.R8G8B8A8_UNorm_SRgb;
                case _BMDPixelFormat.bmdFormat10BitRGBXLE:
                    break;
                case _BMDPixelFormat.bmdFormat10BitRGBX:
                    break;
                case _BMDPixelFormat.bmdFormat10BitRGBXLE_FULL:
                    break;
                case _BMDPixelFormat.bmdFormat10BitRGBX_FULL:
                    break;
                case _BMDPixelFormat.bmdFormatProRes4444XQ:
                    break;
                case _BMDPixelFormat.bmdFormatProRes4444:
                    break;
                case _BMDPixelFormat.bmdFormatProResHQ:
                    break;
                case _BMDPixelFormat.bmdFormatProRes:
                    break;
                case _BMDPixelFormat.bmdFormatProResLT:
                    break;
                case _BMDPixelFormat.bmdFormatProResProxy:
                    break;
                case _BMDPixelFormat.bmdFormatH265:
                    break;
                case _BMDPixelFormat.bmdFormatDNxHD:
                    break;
                case _BMDPixelFormat.bmdFormatDNxHR:
                    break;
                case _BMDPixelFormat.bmdFormat12BitRAWGRBG:
                    break;
                case _BMDPixelFormat.bmdFormat12BitRAWJPEG:
                    break;
                default:
                    break;
            }
            return PixelFormat.None;
        }
    }
}
