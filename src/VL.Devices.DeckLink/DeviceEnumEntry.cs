using System;
using System.Collections.Generic;
using System.Text;
using VL.Lib.Collections;

namespace VL.Devices.DeckLink
{
    [Serializable]
    public class DeviceEnumEntry : DynamicEnumBase<DeviceEnumEntry, DeviceEnum>
    {
        public DeviceEnumEntry(string value) : base(value)
        {
        }

        //this method needs to be imported in VL to set the default
        public static DeviceEnumEntry CreateDefault() => CreateDefaultBase("No video capture device found");
    }
}
