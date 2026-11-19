using System.Windows.Input;

namespace PCL.Pages;

public partial class PageAbout
{
    public PageAbout()
    {
        InitializeComponent();
        LabVersion.Text = "版本: v" + PCL.Core.App.Basics.VersionName;
    }

    private void LinkClick(object sender, MouseButtonEventArgs e)
    {
        Common.OpenWebsite("https://afdian.com/a/LTCat");
    }
}
