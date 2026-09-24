#import <UIKit/UIKit.h>

// Whether iOS is in dark mode, for DeviceInfo.PrefersDark().
extern "C" bool _HelpwingPrefersDark(void)
{
    if (@available(iOS 13.0, *)) {
        return UIScreen.mainScreen.traitCollection.userInterfaceStyle == UIUserInterfaceStyleDark;
    }
    return false;
}
