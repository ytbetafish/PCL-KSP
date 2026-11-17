namespace PCL.Pages;

public partial class PageAbout
{
    public PageAbout()
    {
        InitializeComponent();
        LabVersion.Text = "版本: v" + PCL.Core.App.Basics.VersionName;
    }
}
