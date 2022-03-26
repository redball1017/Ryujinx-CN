using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Nifm.StaticService.GeneralService;
using Ryujinx.HLE.HOS.Services.Nifm.StaticService.Types;
using Ryujinx.HLE.Utilities;
using System;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Nifm.StaticService
{
    class IGeneralService : DisposableIpcService
    {
        private GeneralServiceDetail _generalServiceDetail;

        private IPInterfaceProperties _targetPropertiesCache = null;
        private UnicastIPAddressInformation _targetAddressInfoCache = null;

        public IGeneralService()
        {
            _generalServiceDetail = new GeneralServiceDetail
            {
                ClientId                     = GeneralServiceManager.Count,
                IsAnyInternetRequestAccepted = true // NOTE: Why not accept any internet request?
            };

            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(LocalInterfaceCacheHandler);

            GeneralServiceManager.Add(_generalServiceDetail);
        }

        [CommandHipc(1)]
        // GetClientId() -> buffer<nn::nifm::ClientId, 0x1a, 4>
        public ResultCode GetClientId(ServiceCtx context)
        {
            ulong position = context.Request.RecvListBuff[0].Position;

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(sizeof(int));

            context.Memory.Write(position, _generalServiceDetail.ClientId);

            return ResultCode.Success;
        }

        [CommandHipc(4)]
        // CreateRequest(u32 version) -> object<nn::nifm::detail::IRequest>
        public ResultCode CreateRequest(ServiceCtx context)
        {
            uint version = context.RequestData.ReadUInt32();

            MakeObject(context, new IRequest(context.Device.System, version));

            // Doesn't occur in our case.
            // return ResultCode.ObjectIsNull;

            Logger.Stub?.PrintStub(LogClass.ServiceNifm, new { version });

            return ResultCode.Success;
        }

        [CommandHipc(5)]
        // GetCurrentNetworkProfile() -> buffer<nn::nifm::detail::sf::NetworkProfileData, 0x1a, 0x17c>
        public ResultCode GetCurrentNetworkProfile(ServiceCtx context)
        {
            ulong networkProfileDataPosition = context.Request.RecvListBuff[0].Position;

            (IPInterfaceProperties interfaceProperties, UnicastIPAddressInformation unicastAddress) = GetLocalInterface();

            if (interfaceProperties == null || unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((uint)Unsafe.SizeOf<NetworkProfileData>());

            NetworkProfileData networkProfile = new NetworkProfileData
            {
                Uuid = new UInt128(Guid.NewGuid().ToByteArray())
            };

            networkProfile.IpSettingData.IpAddressSetting = new IpAddressSetting(interfaceProperties, unicastAddress);
            networkProfile.IpSettingData.DnsSetting       = new DnsSetting(interfaceProperties);

            Encoding.ASCII.GetBytes("RyujinxNetwork").CopyTo(networkProfile.Name.ToSpan());

            context.Memory.Write(networkProfileDataPosition, networkProfile);

            return ResultCode.Success;
        }

        [CommandHipc(12)]
        // GetCurrentIpAddress() -> nn::nifm::IpV4Address
        public ResultCode GetCurrentIpAddress(ServiceCtx context)
        {
            (_, UnicastIPAddressInformation unicastAddress) = GetLocalInterface();

            if (unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            context.ResponseData.WriteStruct(new IpV4Address(unicastAddress.Address));

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            return ResultCode.Success;
        }

        [CommandHipc(15)]
        // GetCurrentIpConfigInfo() -> (nn::nifm::IpAddressSetting, nn::nifm::DnsSetting)
        public ResultCode GetCurrentIpConfigInfo(ServiceCtx context)
        {
            (IPInterfaceProperties interfaceProperties, UnicastIPAddressInformation unicastAddress) = GetLocalInterface();

            if (interfaceProperties == null || unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            context.ResponseData.WriteStruct(new IpAddressSetting(interfaceProperties, unicastAddress));
            context.ResponseData.WriteStruct(new DnsSetting(interfaceProperties));

            return ResultCode.Success;
        }

        [CommandHipc(18)]
        // GetInternetConnectionStatus() -> nn::nifm::detail::sf::InternetConnectionStatus
        public ResultCode GetInternetConnectionStatus(ServiceCtx context)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return ResultCode.NoInternetConnection;
            }

            InternetConnectionStatus internetConnectionStatus = new InternetConnectionStatus
            {
                Type         = InternetConnectionType.WiFi,
                WifiStrength = 3,
                State        = InternetConnectionState.Connected,
            };

            context.ResponseData.WriteStruct(internetConnectionStatus);

            return ResultCode.Success;
        }

        [CommandHipc(21)]
        // IsAnyInternetRequestAccepted(buffer<nn::nifm::ClientId, 0x19, 4>) -> bool
        public ResultCode IsAnyInternetRequestAccepted(ServiceCtx context)
        {
            ulong position = context.Request.PtrBuff[0].Position;
            ulong size     = context.Request.PtrBuff[0].Size;

            int clientId = context.Memory.Read<int>(position);

            context.ResponseData.Write(GeneralServiceManager.Get(clientId).IsAnyInternetRequestAccepted);

            return ResultCode.Success;
        }

        private (IPInterfaceProperties, UnicastIPAddressInformation) GetLocalInterface()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return (null, null);
            }

            if (_targetPropertiesCache != null && _targetAddressInfoCache != null)
            {
                return (_targetPropertiesCache, _targetAddressInfoCache);
            }

            IPInterfaceProperties       targetProperties  = null;
            UnicastIPAddressInformation targetAddressInfo = null;

            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface adapter in interfaces)
            {
                // Ignore loopback and non IPv4 capable interface.
                if (targetProperties == null && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback && adapter.Supports(NetworkInterfaceComponent.IPv4))
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();

                    if (properties.GatewayAddresses.Count > 0 && properties.DnsAddresses.Count > 0)
                    {
                        foreach (UnicastIPAddressInformation info in properties.UnicastAddresses)
                        {
                            // Only accept an IPv4 address
                            if (info.Address.GetAddressBytes().Length == 4)
                            {
                                targetProperties  = properties;
                                targetAddressInfo = info;

                                break;
                            }
                        }
                    }
                }
            }

            _targetPropertiesCache  = targetProperties;
            _targetAddressInfoCache = targetAddressInfo;

            return (targetProperties, targetAddressInfo);
        }

        private void LocalInterfaceCacheHandler(object sender, EventArgs e)
        {
            Logger.Info?.Print(LogClass.ServiceNifm, $"NetworkAddress changed, invalidating cached data.");

            _targetPropertiesCache  = null;
            _targetAddressInfoCache = null;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                NetworkChange.NetworkAddressChanged -= LocalInterfaceCacheHandler;

                GeneralServiceManager.Remove(_generalServiceDetail.ClientId);
            }
        }
    }
}