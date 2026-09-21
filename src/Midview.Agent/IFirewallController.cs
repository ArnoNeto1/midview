namespace Midview.Agent;

public interface IFirewallController
{
    bool ApplyBlock();
    void ApplyUnblock();
}
