using DeckLinkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using VL.Lib;
using VL.Lib.Collections;

namespace VL.Devices.DeckLink
{
    /// <summary>
    /// Dynamic enum of available DeckLink devices
    /// </summary>
    public class DeviceEnum : DynamicEnumDefinitionBase<DeviceEnum>
    {
        //return the current enum entries
        protected override IReadOnlyDictionary<string, object> GetEntries()
        {
            var devices = new Dictionary<string, object>();

            var iterator = new CDeckLinkIterator();
            var deviceList = new List<IDeckLink>();
            while (true)
            {
                iterator.Next(out var deckLink);
                if (deckLink is null)
                    break;

                if (deckLink is IDeckLinkInput deckLinkInput)
                {
                    deckLink.GetModelName(out var modelName);
                    deckLink.GetDisplayName(out var displayName);
                    devices.Add(displayName, deckLinkInput);
                }
            }

            // Add a default entry which makes it up to the system to select a device
            if (devices.Count > 0)
                devices.Add("Default", devices.Values.First());

            return devices;
        }

        //inform the system that the enum has changed
        protected override IObservable<object> GetEntriesChangedObservable()
        {
            return Observable.Create<IDeckLink>(observer =>
            {
                return new DeckLinkDeviceDiscovery(observer);
            });
        }

        //disable alphabetic sorting
        protected override bool AutoSortAlphabetically => false;

        class DeckLinkDeviceDiscovery : IDeckLinkDeviceNotificationCallback, IDisposable
        {
            private readonly IObserver<IDeckLink> Observer;
            private readonly IDeckLinkDiscovery DeckLinkDiscovery;

            public DeckLinkDeviceDiscovery(IObserver<IDeckLink> observer)
            {
                Observer = observer;
                DeckLinkDiscovery = new CDeckLinkDiscovery();
                DeckLinkDiscovery.InstallDeviceNotifications(this);
            }

            public void Dispose()
            {
                DeckLinkDiscovery.UninstallDeviceNotifications();
            }

            void IDeckLinkDeviceNotificationCallback.DeckLinkDeviceArrived(IDeckLink deckLinkDevice)
            {
                Observer.OnNext(deckLinkDevice);
            }

            void IDeckLinkDeviceNotificationCallback.DeckLinkDeviceRemoved(IDeckLink deckLinkDevice)
            {
                Observer.OnNext(deckLinkDevice);
            }
        }
    }
}
