/*
 * CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
 * Copyright (C) 2026 RichTiTAN
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using Avalonia.Controls;

namespace CrimsonX.Localization
{
    public static class AppStrings
    {
        // ── Language Helpers ──

        public static bool IsPersian { get; private set; } = false;

        public static void SetLanguage(string lang)
        {
            IsPersian = lang == "PERSIAN";
        }


        public static void Apply(TextBlock? tb, string text, bool forceLtr = false)
        {
            if (tb == null) return;
            tb.Text = text;
            if (IsPersian)
            {
                tb.FontFamily = new global::Avalonia.Media.FontFamily("Segoe UI");
                tb.FlowDirection = forceLtr
                    ? global::Avalonia.Media.FlowDirection.LeftToRight
                    : global::Avalonia.Media.FlowDirection.RightToLeft;
            }
            else
            {
                tb.FontFamily = global::Avalonia.Media.FontFamily.Default;
                tb.FlowDirection = global::Avalonia.Media.FlowDirection.LeftToRight;
            }
            
            if (forceLtr && IsPersian)
            {
                tb.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            }
        }

        public static void ApplyBtn(Button? btn, string text)
        {
            if (btn == null) return;
            btn.Content = text;
            if (IsPersian)
            {
                btn.FontFamily = new global::Avalonia.Media.FontFamily("Segoe UI");
                btn.FlowDirection = global::Avalonia.Media.FlowDirection.RightToLeft;
            }
            else
            {
                btn.FontFamily = global::Avalonia.Media.FontFamily.Default;
                btn.FlowDirection = global::Avalonia.Media.FlowDirection.LeftToRight;
            }
        }

        public static void ApplyToolTip(Control? c, string text)
        {
            if (c == null) return;
            if (IsPersian)
            {
                var tb = new global::Avalonia.Controls.TextBlock
                {
                    Text            = text,
                    FlowDirection   = global::Avalonia.Media.FlowDirection.RightToLeft,
                    FontFamily      = new global::Avalonia.Media.FontFamily("Segoe UI"),
                    TextWrapping    = global::Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth        = 300
                };
                ToolTip.SetTip(c, tb);
            }
            else
            {
                ToolTip.SetTip(c, text);
            }
        }

        // ── Navigation ──

        public static string NavHome            => IsPersian ? "خانه" : "HOME";

        public static string NavSplitTunneling  => IsPersian ? "اسپلیت تانل" : "SPLIT TUNNELING";

        public static string NavSettings        => IsPersian ? "تنظیمات" : "SETTINGS";

        public static string NavAbout           => IsPersian ? "درباره ما" : "ABOUT";

        public static string NavThemes          => IsPersian ? "تم ها" : "THEMES";
        

        public static string NavStats           => IsPersian ? "آمار" : "STATS";

        public static string NavLogs            => IsPersian ? "گزارشات" : "LOGS";

        public static string NavAppsGames       => IsPersian ? "برنامه‌ها و بازی‌ها" : "APPS & GAMES";

        // ── Home (Connect & Quick Settings) ──

        public static string ConnectedFor       => IsPersian ? "متصل برای"         : "CONNECTED FOR";

        public static string ConnectedTo        => IsPersian ? "به"                 : "TO";

        public static string ProxyMode          => IsPersian ? "حالت پروکسی"       : "PROXY MODE";

        public static string VpnMode            => IsPersian ? "حالت VPN"          : "VPN MODE";

        public static string ClearProxy         => IsPersian ? "بدون پروکسی"       : "CLEAR PROXY";

        public static string LbPolicy           => IsPersian ? "سیاست تعادل بار"   : "LOAD-BALANCE";

        public static string QuickSettings      => IsPersian ? "تنظیمات سریع" : "QUICK SETTINGS";

        public static string Customize          => IsPersian ? "شخصی‌سازی" : "CUSTOMIZE";

        public static string StatusConnecting => IsPersian ? "در حال اتصال..." : "CONNECTING";

        public static string StatusConnected => IsPersian ? "متصل" : "CONNECTED";

        public static string StatusDisconnect => IsPersian ? "قطع اتصال" : "DISCONNECT";

        public static string StatusStopEngine => IsPersian ? "توقف موتور" : "STOP ENGINE";

        public static string StatusNotConnected => IsPersian ? "متصل نیست" : "NOT CONNECTED";

        public static string StatusConnect => IsPersian ? "اتصال" : "CONNECT";

        public static string ApplyChanges => IsPersian ? "اعمال تغییرات" : "APPLY CHANGES";

        public static string ToastRulesApplied => IsPersian ? "قوانین برنامه اعمال شد." : "App rules applied.";

        public static string ToastRulesApplyFailed => IsPersian ? "اعمال قوانین برنامه ناموفق بود؛ لطفاً دوباره متصل شوید." : "Failed to apply app rules.";

        public static string NoInternetTitle => IsPersian ? "عدم اتصال به اینترنت" : "No Internet Connection";

        public static string NoInternetMessage => IsPersian ? "به نظر می‌رسد اتصال اینترنت شما کار نمی‌کند. مطمئن هستید می‌خواهید متصل شوید؟" : "You don't seem to have a working internet connection. Are you sure you want to connect?";

        public static string StatusDisconnected => IsPersian ? "منتظر اتصال" : "Disconnected";

        // ── Stats & Logs ──

        public static string LogsStatus         => IsPersian ? "لاگ‌ها و وضعیت"   : "LOGS & STATUS";

        public static string XrayLogHeader      => IsPersian ? "اتصالات (لاگ Xray)" : "CONNECTIONS (XRAY LOG)";

        public static string OpenLocalPort      => IsPersian ? "پورت لوکال:"       : "LOCAL PORT:";

        public static string OpenLanPort        => IsPersian ? "پورت لن:"         : "LAN PORT:";

        public static string SessionLabel       => IsPersian ? "نشست:"             : "SESSION:";

        public static string LocationLabel      => IsPersian ? "موقعیت:"           : "LOCATION:";

        public static string PingLabel          => IsPersian ? "پینگ:"             : "PING:";

        public static string TotalLabel         => IsPersian ? "مجموع:"            : "TOTAL:";

        public static string DownloadLabel      => IsPersian ? "دانلود:"           : "DOWNLOAD:";

        public static string UploadLabel        => IsPersian ? "آپلود:"            : "UPLOAD:";

        public static string GeoTracing             => IsPersian ? "در حال جستجو..." : "Tracing...";

        public static string GeoTimeout             => IsPersian ? "ناموفق"          : "Timeout";

        // ── Split Tunneling ──

        public static string SplitTunnelDirectUDP => IsPersian ? "UDP مستقیم" : "DIRECT UDP";

        public static string SplitTunnelDirectUDPDesc => IsPersian ? "این گزینه می‌تواند به بازی‌های ویدیویی، چت صوتی دیسکورد یا سایر پلتفرم‌های وابسته به UDP کمک کند." : "This option can help with video games, discord voice or other udp dependant platforms.";

        public static string Disabled           => IsPersian ? "غیرفعال"           : "DISABLED";

        public static string Exclusive          => IsPersian ? "اختصاصی"           : "EXCLUSIVE";

        public static string Inclusive          => IsPersian ? "شامل"              : "INCLUSIVE";

        public static string DomainsAndIps      => IsPersian ? "دامنه‌ها، IPها و پورت‌ها" : "DOMAINS, IPs & PORTS";

        public static string Applications       => IsPersian ? "برنامه‌ها"          : "APPLICATIONS";

        public static string BlockedDomains     => IsPersian ? "دامنه‌ها، IPها و پورت‌های مسدود شده" : "BLOCKED DOMAINS, IPs & PORTS";

        public static string SplitExplanationExclusive => IsPersian ? "فقط برنامه ها، دامنه ها، آیپی ها و پورت های لیست شده در اینجا از پروکسی مستثنی می شوند." : "Only bypass the proxy for the apps, domains, IPs and ports listed below.";

        public static string SplitExplanationInclusive => IsPersian ? "فقط برنامه ها، دامنه ها، آیپی ها و پورت های لیست شده در اینجا از طریق پروکسی هدایت می شوند." : "Only route the apps, domains, IPs and ports listed below through the proxy.";

        public static string ExcludeLocationsTitle => IsPersian ? "مستثنی کردن قاره‌ها" : "EXCLUDE LOCATIONS";

        public static string ExcludeContinentAsia => IsPersian ? "آسیا" : "Asia";

        public static string ExcludeContinentEurope => IsPersian ? "اروپا" : "Europe";

        public static string ExcludeContinentNorthAmerica => IsPersian ? "آمریکای شمالی" : "North America";

        public static string ExcludeContinentSouthAmerica => IsPersian ? "آمریکای جنوبی" : "South America";

        public static string ExcludeContinentAfrica => IsPersian ? "آفریقا" : "Africa";

        public static string ExcludeContinentOceania => IsPersian ? "اقیانوسیه" : "Oceania";
    

        // ── Apps & Games Overlay ──

        public static string AppsGamesDisconnectWarning => IsPersian ? "ابتدا باید از VPN اصلی قطع شوید." : "You must disconnect from the main VPN first.";

        public static string AppsGamesDisconnectAndContinue => IsPersian ? "قطع اتصال و ادامه" : "DISCONNECT AND CONTINUE";

        public static string OverlayMode => IsPersian ? "حالت:" : "MODE:";

        public static string MasterRulesEnabled => IsPersian ? "فعال" : "ENABLED";

        public static string MasterRulesDisabled => IsPersian ? "غیرفعال" : "DISABLED";

        public static string AdvancedRulesVpnOnlyTitle => IsPersian ? "نیاز به حالت VPN" : "VPN MODE REQUIRED";

        public static string AdvancedRulesVpnOnlyMsg => IsPersian ? "قوانین پیشرفته فقط در حالت VPN قابل فعال‌سازی هستند. آیا مایل به تغییر به حالت VPN و ادامه هستید؟" : "ADVANCED RULES CAN ONLY BE ENABLED IN VPN MODE, DO YOU WISH TO SWITCH TO VPN MODE AND CONTINUE?";

        public static string FilterAll => IsPersian ? "همه" : "ALL";

        public static string FilterGames => IsPersian ? "بازی‌ها" : "GAMES";

        public static string FilterLaunchers => IsPersian ? "لانچرها" : "LAUNCHERS";

        public static string FilterOther => IsPersian ? "سایر" : "OTHER";

        public static string SearchPlaceholder => IsPersian ? "جستجو..." : "Search...";

        public static string FilterLabel => IsPersian ? "فیلتر:" : "FILTER:";

        public static string ConfigBadge => IsPersian ? "کانفیگ" : "CONFIG";

        public static string AddToggle => IsPersian ? "افزودن +" : "ADD +";

        public static string AddProgram => IsPersian ? "افزودن برنامه" : "ADD PROGRAM";

        public static string EditProgram => IsPersian ? "ویرایش برنامه" : "EDIT PROGRAM";

        public static string Update => IsPersian ? "به‌روزرسانی" : "UPDATE";

        public static string TypeLabel => IsPersian ? "نوع" : "TYPE";

        public static string Game => IsPersian ? "بازی" : "GAME";

        public static string Launcher => IsPersian ? "لانچر" : "LAUNCHER";

        public static string Other => IsPersian ? "سایر" : "OTHER";

        public static string AppLabel => IsPersian ? "برنامه" : "APP";

        public static string RoutingLabel => IsPersian ? "مسیریابی" : "ROUTING";

        public static string RoutingProxy => IsPersian ? "پروکسی" : "Proxy";

        public static string RoutingDirect => IsPersian ? "مستقیم" : "Direct";

        public static string AdapterLabel => IsPersian ? "آداپتور" : "ADAPTER";

        public static string AdapterDefault => IsPersian ? "پیش‌فرض" : "Default";

        public static string ConnectionRegionLabel => IsPersian ? "منطقه اتصال" : "CONNECTION REGION";
        public static string ConnectionRegionWarning => IsPersian ? "انتخاب یک منطقه ممکن است روی همه برنامه‌ها کار نکند." : "Choosing a region might not work on all programs.";

        public static string ConnRegionShort => IsPersian ? "منطقه اتصال" : "CONN-REGION";

        public static string MatchMakingRegion => IsPersian ? "منطقه مچ‌میکینگ" : "MATCH-MAKING REGION";

        public static string TcpAdapterLabel => IsPersian ? "آداپتور TCP" : "TCP ADAPTER";

        public static string UdpAdapterLabel => IsPersian ? "آداپتور UDP" : "UDP ADAPTER";

        public static string TcpRoutingLabel => IsPersian ? "مسیریابی TCP" : "TCP ROUTING";

        public static string UdpRoutingLabel => IsPersian ? "مسیریابی UDP" : "UDP ROUTING";

        public static string CountryEverywhere => IsPersian ? "همه‌جا" : "Everywhere";

        public static string CountryIran => IsPersian ? "ایران" : "Iran";

        public static string CountryUae => IsPersian ? "امارات" : "UAE";

        public static string RegionAll => IsPersian ? "همه" : "All";

        public static string RegionNorthAmerica => IsPersian ? "آمریکای شمالی" : "North America";

        public static string RegionSouthAmerica => IsPersian ? "آمریکای جنوبی" : "South America";

        public static string RegionEurope => IsPersian ? "اروپا" : "Europe";

        public static string RegionAsia => IsPersian ? "آسیا" : "Asia";

        public static string RegionAfrica => IsPersian ? "آفریقا" : "Africa";

        public static string RegionOceania => IsPersian ? "اقیانوسیه" : "Oceania";

        public static string OverlaySplitRegular => IsPersian ? "عادی" : "REGULAR";

        public static string Delete => IsPersian ? "حذف" : "Delete";

        public static string PinUnpin => IsPersian ? "سنجاق / برداشتن سنجاق" : "Pin / Unpin";

        public static string EditAdapters => IsPersian ? "ویرایش آداپتورها" : "Edit Adapters";

        // ── Settings - Start-up ──

        public static string SectionStartup     => IsPersian ? "اجرا"              : "START-UP";

        public static string LaunchOnStartup    => IsPersian ? "اجرا با ویندوز"    : "LAUNCH ON START-UP";

        public static string AutoConnect        => IsPersian ? "اتصال خودکار"      : "AUTO-CONNECT";

        public static string StartMinimized     => IsPersian ? "شروع کوچک‌شده"     : "START MINIMIZED";

        public static string MinimizeToTray     => IsPersian ? "کوچک کردن به tray" : "MINIMIZE TO TRAY";

        // ── Settings - Connection ──

        public static string SectionConnection  => IsPersian ? "اتصال"             : "CONNECTION";
        

        public static string CustomConfigsTitle => IsPersian ? "کانفیگ‌های دلخواه" : "CUSTOM CONFIGS";

        public static string AllowOneCustomConfig => IsPersian ? "اجازه اتصال با یک کانفیگ" : "Allow connecting with one config";

        public static string PingBtn => IsPersian ? "پینگ" : "PING";

        public static string ValidatingConfig => IsPersian ? "در حال بررسی..." : "VALIDATING...";

        public static string InvalidConfig => IsPersian ? "کانفیگ نامعتبر!" : "INVALID CONFIG!";

        public static string CustomXrayExit     => IsPersian ? "نود خروجی Xray" : "CUSTOM XRAY EXIT-NODE";

        public static string AdapterBinding     => IsPersian ? "اتصال به آداپتور" : "BIND ADAPTER";

        public static string ScanAdapters       => IsPersian ? "اسکن"            : "SCAN";

        public static string DnsSettings        => IsPersian ? "تنظیمات DNS"       : "DNS SETTINGS";

        public static string AdBlocker          => IsPersian ? "مسدودکننده تبلیغات و ردیاب" : "AD AND TRACKER BLOCKER";

        public static string AllowLan           => IsPersian ? "اجازه اتصالات LAN" : "ALLOW LAN CONNECTIONS";

        public static string UpstreamDohUrl     => IsPersian ? "آدرس DoH بالادست"  : "UPSTREAM DOH URL";

        public static string SystemDns          => IsPersian ? "DNS سیستم"          : "SYSTEM DNS";

        public static string SystemDnsPrimary   => IsPersian ? "DNS اول"            : "PRIMARY DNS";

        public static string SystemDnsSecondary => IsPersian ? "DNS دوم"            : "SECONDARY DNS";

        public static string ProxyType          => IsPersian ? "نوع"               : "TYPE";

        public static string AddressIp          => IsPersian ? "آدرس/IP"           : "ADDRESS/IP";

        public static string Port               => IsPersian ? "پورت"              : "PORT";

        public static string Authentication     => IsPersian ? "احراز هویت"        : "AUTHENTICATION";

        public static string Username           => IsPersian ? "نام کاربری"         : "USERNAME";

        public static string Password           => IsPersian ? "رمز عبور"           : "PASSWORD";

        public static string WarningCaseSensitive => IsPersian ? "هشدار: به حروف بزرگ و کوچک حساس است" : "Warning: Case sensitive";

        public static string XtlsVisionNotSupported => IsPersian ? "کانفیگ های XTLS Vision به عنوان نود خروجی پشتیبانی نمی‌شوند." : "XTLS Vision configs cannot be used as a Custom Exit Node.";

        // ── Settings - System ──

        public static string DisableBackgroundChecks => IsPersian ? "غیرفعال کردن بررسی پس‌زمینه" : "DISABLE BACKGROUND CHECK";

        public static string DisableRefreshTimer     => IsPersian ? "غیرفعال کردن تعویض یکپارچه" : "DISABLE SEAMLESS SWAP";

        public static string SectionSystem      => IsPersian ? "سیستم"             : "SYSTEM";

        public static string LanguageSetting    => IsPersian ? "زبان"              : "LANGUAGE";

        public static string DebugMode          => IsPersian ? "حالت دیباگ"        : "DEBUG MODE";

        public static string DesktopShortcut    => IsPersian ? "میانبر دسکتاپ"     : "DESKTOP SHORTCUT";

        public static string StartMenuShortcut  => IsPersian ? "میانبر منوی استارت" : "START MENU SHORTCUT";

        public static string LblLanguageName => IsPersian ? "فارسی" : "ENGLISH";

        public static string ClearWorkingCache  => IsPersian ? "پاکسازی دیتای تست شده" : "CLEAR WORKING CACHE";

        public static string ClearFetchedCache  => IsPersian ? "پاکسازی دیتای دریافتی" : "CLEAR FETCHED CACHE";

        public static string ToastCacheCleared  => IsPersian ? "کش پاک شد." : "Cache cleared.";

        public static string ToastCacheClearFailed => IsPersian ? "پاک کردن کش ناموفق بود." : "Failed to clear cache.";

        // ── System Tray ──

        public static string TrayClose          => IsPersian ? "بستن برنامه"       : "CLOSE THE APP";

        public static string TrayShowWindow     => IsPersian ? "نمایش پنجره"       : "SHOW WINDOW";

        // ── Themes ──

        public static string ChooseAColour      => IsPersian ? "یک رنگ انتخاب کنید" : "CHOOSE A COLOUR";

        public static string ColorCrimson       => IsPersian ? "قرمز شرابی" : "CRIMSON";

        public static string ColorBlue          => IsPersian ? "آبی" : "BLUE";

        public static string ColorPurple        => IsPersian ? "بنفش" : "PURPLE";

        public static string ColorGreen         => IsPersian ? "سبز" : "GREEN";

        public static string ColorPink          => IsPersian ? "صورتی" : "PINK";

        public static string ColorYellow        => IsPersian ? "زرد" : "YELLOW";
        

        public static string ThemePauseGlows => IsPersian ? "توقف" : "PAUSE";

        public static string ThemeDisableGlows => IsPersian ? "غیرفعال" : "DISABLE";

        public static string ThemeManageGlow => IsPersian ? "مدیریت پس‌زمینه" : "MANAGE GLOW";

        // ── About ──

        public static string AboutVersion       => IsPersian ? "نسخه"              : "VERSION";

        public static string CheckForUpdates    => IsPersian ? "بررسی برای آپدیت"  : "CHECK FOR UPDATES";

        public static string UpdateChecking     => IsPersian ? "در حال بررسی برای آپدیت..." : "CHECKING FOR UPDATES...";

        public static string UpdateLatest       => IsPersian ? "آخرین نسخه نصب شده است" : "LATEST VERSION INSTALLED";

        public static string UpdateAutoTitle    => IsPersian ? "آپدیت موجود است" : "UPDATE AVAILABLE";

        public static string UpdateAutoMsg      => IsPersian ? "نسخه جدید (v{0}) آماده نصب است! مایلید الان آپدیت کنید؟" : "A new version of CrimsonX (v{0}) is ready to install! Would you like to update now?";

        public static string UpdateManualTitle  => IsPersian ? "نیاز به آپدیت دستی" : "MANUAL UPDATE REQUIRED";

        public static string UpdateManualMsg    => IsPersian ? "نسخه v{0} موجود است! نسخه فعلی شما برای آپدیت خودکار خیلی قدیمی است. لطفا آخرین نسخه را از گیت‌هاب دانلود کنید." : "CrimsonX v{0} is available! Your current version is too old to safely auto-update. Please download the latest release from GitHub.";
        

        public static string BtnUpdateNow       => IsPersian ? "همین الان آپدیت کن" : "UPDATE NOW";

        public static string BtnDownloadGithub  => IsPersian ? "دانلود از گیت‌هاب" : "DOWNLOAD FROM GITHUB";

        public static string BtnChangeLog       => IsPersian ? "تغییرات" : "CHANGE LOG";

        public static string AboutCreator => IsPersian ? "سازنده: RichTitan" : "Creator: @RichTitan";

        public static string AboutLicense => IsPersian ? "لایسنس: GPL-3.0 license" : "License: GPL-3.0 license";

        public static string DonationsTitle     => IsPersian ? "حمایت مالی"         : "DONATIONS";

        public static string DonationsDesc      => IsPersian ? "اگر می‌خواهید از من یا پروژه حمایت کنید، می‌توانید با ارسال مبلغ دلخواه به یکی از آدرس‌های کیف پول زیر این کار را انجام دهید،" : "if u want to support me or the project you can do so by sending your desired amount to one of these wallet addresses,";

        public static string OtherApps          => IsPersian ? "سایر برنامه‌ها" : "OTHER APPS";

        // ── Common UI Buttons & Actions ──

        public static string Save               => IsPersian ? "ذخیره"             : "SAVE";

        public static string Cancel             => IsPersian ? "لغو"               : "CANCEL";

        public static string Submit             => IsPersian ? "ثبت"               : "SUBMIT";

        public static string Clear              => IsPersian ? "پاک کردن"          : "CLEAR";
        

        public static string Create             => IsPersian ? "ایجاد"             : "CREATE";

        public static string Add                => IsPersian ? "افزودن"             : "ADD";

        public static string Edit               => IsPersian ? "ویرایش"             : "EDIT";

        public static string Browse             => IsPersian ? "مرور"               : "BROWSE";

        public static string Yes => IsPersian ? "بله" : "YES";

        public static string No => IsPersian ? "خیر" : "NO";

        // ── Toasts ──

        public static string ToastUpdateCancelled => IsPersian ? "آپدیت لغو شد." : "Update cancelled.";

        public static string ToastLatestVersion => IsPersian ? "شما از قبل آخرین نسخه را دارید!" : "You are already on the latest version!";

        public static string ToastPortsSupported => IsPersian ? "تنها پورت های 80 و 443 پشتیبانی می شوند." : "Only port 80 and 443 are supported.";

        public static string ToastXrayRejected => IsPersian ? "کانفیگ Xray رد شد. " : "Xray config rejected. ";

        public static string ToastInvalidJson => IsPersian ? "سینتکس نامعتبر JSON مربوط به Xray!" : "Invalid Xray JSON syntax!";

        public static string ToastLinkConverted => IsPersian ? "لینک به صورت خودکار به JSON تبدیل شد!" : "Link auto-converted to JSON!";

        public static string ToastFailedImport => IsPersian ? "وارد کردن JSON ناموفق بود." : "Failed to import JSON.";

        public static string ToastTaskFailed => IsPersian ? "عملیات ناموفق بود: " : "Task failed: ";

        public static string ToastShortcutCreated => IsPersian ? "شورتکات با موفقیت ایجاد شد!" : "Shortcut created successfully!";

        public static string ToastShortcutFailed => IsPersian ? "ایجاد شورتکات ناموفق بود." : "Failed to create shortcut.";

        public static string ToastAllExcluded => IsPersian ? "پس نمی‌خوای به هیچ‌جا وصل شی؟" : "So you don't want to connect to anywhere?";

        public static string ToastReconnectChanges  => IsPersian ? "لطفا برای اعمال تغییرات مجددا متصل شوید." : "Please reconnect to apply the changes.";

        public static string ToastReconnectSafely   => ToastReconnectChanges;

        public static string ToastReconnectDns      => ToastReconnectChanges;

        public static string ToastAddressCopied     => IsPersian ? "آدرس در کلیپ بورد کپی شد!" : "Address copied to clipboard!";

        public static string ToastSavedApplied      => IsPersian ? "ذخیره و اعمال شد!" : "Saved and applied!";

        public static string ToastChangesApplied    => IsPersian ? "تغییرات اعمال شد!" : "Changes applied!";

        public static string ToastSaved             => IsPersian ? "ذخیره شد!" : "Saved!";

        public static string ToastSavedReconnect    => IsPersian ? "ذخیره شد! برای اعمال تغییرات اتصال را مجدد برقرار کنید." : "Saved! Reconnect to apply changes.";

        public static string ToastCopiedToClipboard => IsPersian ? "کپی شد!" : "Copied to clipboard!";

        public static string ToastAdapterNotAvailable => IsPersian ? "آداپتور انتخاب شده در دسترس نیست!" : "Selected adapter is not available!";

        public static string ToastAdapterNoLongerAvail => IsPersian ? "آداپتور شبکه قبلی شما دیگر در دسترس نیست." : "Your previously selected network adapter is no longer available.";

        public static string ToastDirectUdpAdapterFallback => IsPersian ? "آداپتور UDP مستقیم قبلی شما دیگر در دسترس نیست. با آداپتور پیش‌فرض ادامه می‌دهیم." : "Your Direct UDP adapter is no longer available. Continuing with the default adapter.";

        public static string ToastEngineStartFailedPrefix => IsPersian ? "خطا در شروع موتور: " : "Engine start failed: ";

        public static string ToastInvalidDnsPrimary => IsPersian ? "لطفاً یک آدرس IPv4 معتبر برای DNS اول وارد کنید." : "Please enter a valid IPv4 address for the primary DNS.";

        public static string ToastInvalidDnsSecondary => IsPersian ? "لطفاً یک آدرس IPv4 معتبر برای DNS دوم وارد کنید." : "Please enter a valid IPv4 address for the secondary DNS.";

        public static string ToastUsernameEmpty => IsPersian ? "لطفاً نام کاربری را وارد کنید." : "Please enter a username.";

        public static string ToastCredentialsSaved => IsPersian ? "اطلاعات ورود ذخیره شد." : "Credentials saved.";

        public static string ToastUpdateCheckTimeout => IsPersian ? "اتصال هنگام بررسی بروزرسانی قطع شد." : "Connection timed out while checking for updates.";

        public static string ToastUpdateDownloadTimeout => IsPersian ? "اتصال هنگام دانلود بروزرسانی قطع شد." : "Connection timed out while downloading the update.";

        public static string ToastUpdateFailedPrefix => IsPersian ? "خطا در بروزرسانی: " : "Failed to update: ";

        public static string ToastNewUpdateAvailable => IsPersian ? "بروزرسانی جدید در دسترس است" : "NEW UPDATE AVAILABLE";

        public static string ToastVpnInUse => IsPersian ? "آداپتور VPN از قبل توسط برنامه دیگری در حال استفاده است!" : "VPN adapter is already in use by another program!";

        public static string ToastModeSwitchFailed => IsPersian ? "تعویض حالت ناموفق بود." : "Mode switch failed.";

        public static string ToastWriteVpnFailed => IsPersian ? "نوشتن تنظیمات VPN ناموفق بود." : "Failed to write VPN config.";

        public static string ToastStartVpnFailed => IsPersian ? "راه‌اندازی VPN ناموفق بود." : "Failed to start VPN.";

        // ── Tooltips ──

        public static string TtLbPolicy         => IsPersian ? "کنترل نحوه توزیع اتصالات توسط Xray بین پروکسی‌های شما." : "Controls how Xray distributes connections across your proxy nodes.";

        public static string TtLbLeastLoad      => IsPersian ? "هر اتصال جدید را به کم‌بارترین پروکسی هدایت می‌کند. بهترین برای ترافیک ترکیبی با حجم‌های متغیر." : "Distributes each new connection to the least-loaded proxy node. Best for mixed traffic with varying connection size.";

        public static string TtLbRoundRobin     => IsPersian ? "اتصالات را به طور مساوی و به ترتیب بین پروکسی‌ها توزیع می‌کند. مناسب برای توزیع پایدار و یکنواخت." : "Distributes connections evenly across all proxy nodes in order, cycling through them one by one. Good for consistent, equal distribution.";

        public static string TtLbLeastPing      => IsPersian ? "پروکسی با کمترین پینگ اخیر را انتخاب می‌کند. بهترین برای ترافیک حساس به تأخیر." : "Picks the proxy node with the lowest recent ping. Best for latency-sensitive traffic.";

        public static string TtLbRandom         => IsPersian ? "برای هر اتصال یک پروکسی تصادفی انتخاب می‌کند. در طول زمان از نظر آماری یکنواخت است اما نسبت به حالت چرخشی واریانس بیشتری دارد." : "Picks a proxy node at random for each new connection. Statistically even over time but with more variance than Round Robin.";

        public static string TtLanAuth          => IsPersian
            ? "اگر فعال باشد، دستگاه‌های روی شبکه باید نام کاربری و رمز عبور وارد کنند تا از این پروکسی استفاده کنند. فقط در حالت پروکسی و Clear Proxy اعمال می‌شود."
            : "When enabled, devices on the network must supply a username and password to use this proxy. Only applies in Proxy and Clear Proxy mode.";

        public static string SplitTunnelDirectUDPTooltip => IsPersian ? "این گزینه می‌تواند به بازی‌های ویدیویی، چت صوتی دیسکورد یا سایر پلتفرم‌های وابسته به UDP کمک کند. ترافیک UDP را مستقیم و بدون عبور از شبکه پروکسی به اینترنت ارسال می‌کند. این ترافیک تونل نخواهد شد، بنابراین این گزینه ناشناس بودن را کاهش می‌دهد." : "This option could help with Games or Discord Voice Chat or other UDP dependant platforms. Bypass the proxy and route all UDP traffic directly to the internet adapter. UDP traffic will not be tunneled, so this option reduces anonymity.";

        public static string TtCustomXray   => IsPersian ? "یک نود پروکسی شخصی تنظیم کنید تا به عنوان نود خروجی شما عمل کند. ترافیک شما از طریق این سرور عبور کرده و وب‌سایت‌ها آدرس IP این سرور را خواهند دید. یک خروجی JSON جایگذاری کنید یا یک لینک اشتراک‌گذاری (VLESS, VMess, Trojan, SS-2022) وارد کنید." : "Configure a custom proxy node to act as your exit node. Your traffic will be routed through this server, and websites will see its IP address. Paste an outbound JSON or import a share link (VLESS, VMess, Trojan, SS-2022).";

        public static string TtAdapterBinding => IsPersian ? "کل ترافیک پروکسی را مجبور می‌کند منحصراً از طریق آداپتور شبکه انتخاب شده خارج شود. " : "Forces all proxy traffic to exclusively exit through the selected network adapter. ";

        public static string TtDnsSettings  => IsPersian ? "تنظیمات DNS رمزگذاری‌شده را کنترل می‌کند. DoH: DNS را از طریق HTTPS رمزگذاری می‌کند تا نشت و سانسور کاهش یابد؛ برای حالت پروکسی (Xray) و VPN (sing-box) اعمال می‌شود. DNS سیستم: DNS آداپتور شبکه اصلی ویندوز را هنگام اتصال تغییر می‌دهد تا پروکسی بتواند بوت‌استرپ کند؛ پس از قطع اتصال یا بستن برنامه بازگردانده می‌شود." : "Controls encrypted DNS settings. DoH: resolves DNS over HTTPS to reduce leaks and censorship; applies in proxy mode (Xray) and VPN mode (sing-box). System Proxy DNS: changes the Windows DNS on your main adapter at connect time so the proxy can bootstrap; restored on disconnect or app close.";

        public static string TtAdBlocker    => IsPersian ? "مسدود کردن درخواست‌ها به دامنه‌های شناخته‌شده تبلیغات و ردیاب‌ها قبل از خروج از رایانه شما. Xray دامنه‌های مطابق را به یک خروجی نامعتبر (blackhole) هدایت می‌کند. فقط بر ترافیک عبوری از پروکسی محلی تأثیر می‌گذارد، نه برنامه‌هایی که از اسپلیت تانل عبور نمی‌کنند." : "Drop requests to known ad and tracker domains before they leave your PC. Xray routes matching domains to a blackhole outbound. Only affects traffic going through the local proxy—not apps on split-tunnel bypass.";

        public static string TtAllowLan     => IsPersian ? "به دستگاه‌های دیگر در شبکه خود اجازه دهید از این رایانه به عنوان پروکسی استفاده کنند. وقتی روشن است، پروکسی محلی روی تمام رابط‌ها (0.0.0.0) گوش می‌دهد؛ وقتی خاموش است، فقط همین دستگاه (127.0.0.1) می‌تواند متصل شود. فقط در شبکه‌هایی که به آنها اعتماد دارید روشن کنید." : "Let other devices on your network use this PC as a proxy. When on, the local proxy listens on all interfaces (0.0.0.0); when off, only this machine (127.0.0.1) can connect. Turn on only on networks you trust.";

        public static string TtLanguage     => IsPersian ? "تغییر زبان برنامه. برای اعمال کامل تغییرات ممکن است نیاز به باز کردن مجدد برنامه باشد." : "Change the application language. Reopening the app may be required for all changes to take effect.";

        public static string TtDebugMode    => IsPersian ? "گزارش‌های زنده Xray و sing-box را در لحظه ضبط می‌کند. برای عیب‌یابی در هنگام قطع اتصال مفید است. تأثیری بر مسیریابی یا امنیت ندارد." : "Captures live logs from Xray and sing-box. Helpful for diagnosing issues when something fails to connect. Does not change routing or security.";

        public static string TtSystemDns    => IsPersian
            ? "DNS ویندوز آداپتور شبکه اصلی را هنگام اتصال تغییر می‌دهد تا پروکسی بتواند از آن استفاده کند. پس از قطع اتصال یا بستن برنامه، DNS قبلی بازگردانده می‌شود."
            : "Changes the Windows DNS of your main network adapter when you connect, so the proxy bootstrap benefits from it. Restored to original on disconnect or app close.";
        

        public static string TtProxyMode => IsPersian ? "ترافیک سیستم را از طریق یک پروکسی محلی هدایت میکند. ایده آل برای عبور از فیلترینگ بدون تغییر مسیر کل سیستم." : "Routes system traffic through a local proxy. Ideal for bypassing censorship without changing global system routing.";

        public static string TtVpnMode => IsPersian ? "تمام ترافیک سیستم را به یک کارت شبکه مجازی هدایت میکند تا به اجبار همه برنامه ها از پروکسی عبور کنند." : "Routes all system traffic through a virtual network interface (TUN), forcing all applications to use the proxy.";

        public static string TtClearProxy => IsPersian ? "پروکسی سیستم را غیرفعال میکند اما پورت محلی را باز نگه میدارد، بنابراین میتوانید برنامه ها را به صورت دستی تنظیم کنید تا از پروکسی استفاده کنند." : "Disables the system proxy but keeps the local port open, so you can manually configure specific applications to use the proxy.";
        

        public static string TtSplitDis     => IsPersian ? "اسپلیت تانل غیرفعال است." : "Split tunneling is disabled.";

        public static string TtLaunchOnStartup => IsPersian ? "اجرای خودکار برنامه هنگام ورود به ویندوز." : "Automatically launch the application when Windows starts.";

        public static string TtAutoConnect => IsPersian ? "اتصال خودکار به شبکه پروکسی هنگام اجرای برنامه." : "Automatically connect to the proxy network when the application is launched.";

        public static string TtStartMinimized => IsPersian ? "اجرای برنامه به صورت کوچک شده (مخفی)." : "Start the application minimized in the background.";

        public static string TtMinimizeToTray => IsPersian ? "کوچک کردن برنامه در سینی سیستم به جای نوار وظیفه." : "Minimize the application to the system tray instead of the taskbar.";

        public static string TtPingRefresh => IsPersian ? "برای به‌روزرسانی پینگ کلیک کنید" : "Click to refresh ping";
        

        public static string TtCustomConfigs => IsPersian
            ? "کانفیگ‌های شخصی خود را برای اتصال مستقیم وارد کنید. اگر دو کانفیگ معتبر باشند، برنامه مستقیم با هر دو وصل می‌شود. اگر یک کانفیگ معتبر باشد و «اجازه اتصال با یک کانفیگ» فعال باشد، فقط با همان یکی وصل می‌شود. در غیر این صورت، برنامه بهترین کانفیگ سرور را پیدا کرده و کنار کانفیگ شما از آن استفاده می‌کند. کانفیگ‌های شخصی در صورت خرابی به‌صورت خودکار جایگزین نمی‌شوند."
            : "Enter your own proxy configs for direct connection. If both configs are valid, the app connects with them immediately. If one is valid and 'Allow connecting with one config' is on, it connects with just that one. Otherwise the app finds the fastest server config and pairs it with yours. Custom configs will not be replaced automatically if they fail a connection health check.";

        public static string TtDisableBackgroundChecks => IsPersian
            ? "بررسی پس‌زمینه به‌طور مداوم کانفیگ‌های جدید را آزمایش می‌کند و آنها را در استخر ذخیره می‌کند تا در صورت قطع اتصال جایگزین سریع‌تری در دسترس باشد. با فعال کردن این گزینه، استخر ذخیره بین‌جلسه‌ای به‌روز نمی‌شود."
            : "Background checks continuously test new configs and store them in a reserve pool for faster failover if a connection drops. Enabling this stops the between-session reserve pool from being updated.";

        public static string TtDisableRefreshTimer => IsPersian
            ? "هر ۱ ساعت یک‌بار، برنامه کانفیگ‌های جدید را دانلود کرده و در صورت نیاز آن‌ها را بدون قطع شدن اتصال شما به‌صورت یکپارچه جایگزین می‌کند. با فعال کردن این گزینه، این تعویض هوشمند بین‌جلسه‌ای غیرفعال می‌شود."
            : "Every 1 hour the app fetches updated configs and seamlessly swaps them in the background without dropping your connection. Enabling this stops the mid-session seamless swap.";

        public static string ExcludeLocationsTooltip => IsPersian ? "شما به کانفیگ‌های قاره‌های انتخاب شده متصل نخواهید شد." : "You won't be connected to configs from the selected continents.";

        public static string OneConfigTooltip => IsPersian ? "برنامه معمولاً با دو کانفیگ متصل می‌شود تا در صورت خرابی بتواند جابجا شود. با فعال کردن این گزینه، برنامه تنها با استفاده از کانفیگ ارائه‌شده در اینجا متصل می‌شود و کانفیگ دیگری را دریافت نمی‌کند." : "The app normally connects with two configs so it can switch over in the case of a failure. By enabling this option the app will connect using the single config provided here and will not fetch another config.";

        public static string OverlaySplitRegularTooltip => IsPersian ? "هدایت تمام ترافیک از طریق VPN، با رعایت قوانین تنظیم‌شده در این تب. این گزینه با انتخاب هر دو گزینه \"غیرفعال\" و \"اختصاصی\" در تب \"اسپلیت تانل\" فعال می‌شود." : "Route all traffic through the VPN, with respect to the rules set here. This option can be enabled with both \"DISABLED\" and \"EXCLUSIVE\" options in the \"Split Tunneling\" tab.";

        public static string OverlaySplitInclusiveTooltip => IsPersian ? "تنها برنامه‌های لیست شده در پایین از طریق پروکسی عبور داده می‌شوند." : "Only route the apps listed below through the proxy.";

        public static string MasterRulesTooltip => IsPersian ? "فعال یا غیرفعال کردن همه قوانین مسیریابی این تب." : "Enable or disable all the routing rules in this tab.";

}
}
