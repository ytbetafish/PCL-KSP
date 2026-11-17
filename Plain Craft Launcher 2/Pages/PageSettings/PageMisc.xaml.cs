using PCL.Core.App;

namespace PCL.Pages;

public partial class PageMisc
{
    private bool _loading;

    public PageMisc()
    {
        InitializeComponent();
        _loading = true;
        CheckHwAccel.Checked = Config.System.DisableHardwareAcceleration;
        CheckDoH.Checked = Config.Network.EnableDoH;
        CheckDebugMode.Checked = Config.Debug.Enabled;
        SliderAnim.Value = Config.Debug.AnimationSpeed;
        CheckDebugDelay.Checked = Config.Debug.AddRandomDelay;
        _loading = false;
    }

    private void CheckHwAccel_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.System.DisableHardwareAcceleration = CheckHwAccel.Checked == true;
    }

    private void CheckDoH_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Network.EnableDoH = CheckDoH.Checked == true;
    }

    private void CheckDebugMode_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Debug.Enabled = CheckDebugMode.Checked == true;
    }

    private void SliderAnim_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Debug.AnimationSpeed = SliderAnim.Value;
    }

    private void CheckDebugDelay_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Debug.AddRandomDelay = CheckDebugDelay.Checked == true;
    }
}
