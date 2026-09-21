using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class SiteBlockCommandHandler
{
    private readonly SiteBlockState _state;
    private readonly string _statePath;
    private readonly string _hostsFilePath;
    private readonly IFirewallController _firewallController;

    public SiteBlockCommandHandler(
        SiteBlockState state, string statePath, string hostsFilePath, IFirewallController firewallController)
    {
        _state = state;
        _statePath = statePath;
        _hostsFilePath = hostsFilePath;
        _firewallController = firewallController;
    }

    public bool Handle(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.BlockInternet:
                if (!_firewallController.ApplyBlock())
                {
                    return false;
                }
                _state.InternetBlocked = true;
                _state.Save(_statePath);
                return true;

            case MessageType.UnblockInternet:
                _firewallController.ApplyUnblock();
                _state.InternetBlocked = false;
                _state.Save(_statePath);
                return true;

            case MessageType.BlockDomain:
                _state.BlockedDomains.Add(Encoding.UTF8.GetString(payload));
                HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
                _state.Save(_statePath);
                return true;

            case MessageType.UnblockDomain:
                _state.BlockedDomains.Remove(Encoding.UTF8.GetString(payload));
                HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
                _state.Save(_statePath);
                return true;

            default:
                return false;
        }
    }

    public void ReconcileAtStartup()
    {
        HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
        if (_state.InternetBlocked)
        {
            _firewallController.ApplyBlock();
        }
    }
}
