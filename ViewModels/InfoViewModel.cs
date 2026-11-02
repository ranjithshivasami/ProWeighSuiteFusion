namespace ProWeighSuitePlatforms.ViewModels
{
    public class InfoViewModel : BaseViewModel
    {
        public string Title => "About";

        public string CompanyName => "Tamil Automation";
        public string Description => "Crafted with Precision";
        public string Address => "Coimbatore, Tamil Nadu, India";
        public string Contact => "+91 7373333304";
        public string Email => "support@tamilautomation.com";
        public string Website => "https://www.tamilautomation.com/";
        public string ApplicationName => "ProWeigh Suite";
        public string ApplicationVersion => Helpers.AppVersionHelper.GetVersion();

        public InfoViewModel()
        {
        }
    }
}
