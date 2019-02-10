using System;

using UIKit;

namespace SearchTextField.Example
{
    public partial class ViewController : UIViewController
    {
        protected ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            var countries = CountryList.Names;

            txtSearchField.filterStrings(countries);
        }

    }
}
