using Android.Content;
using Android.Content.Res;
using Android.OS;
using Java.Util;

namespace Buds3ProAideAuditivelA.v2
{
    public static class LocaleManager
    {
        private const string PrefsName = "locale_prefs";
        private const string KeyLang   = "lang";

        public static Context Wrap(Context context)
        {
            var lang = GetSavedLanguage(context) ?? Locale.Default.Language;
            return UpdateResources(context, lang);
        }

        public static void SetLocale(Context context, string lang)
        {
            SaveLanguage(context, lang);
            UpdateResources(context, lang);
        }

        public static string? GetSavedLanguage(Context context)
        {
            var p = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            return p.GetString(KeyLang, null);
        }

        private static void SaveLanguage(Context context, string lang)
        {
            var p = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            using var e = p.Edit();
            e.PutString(KeyLang, lang);
            e.Commit();
        }

        private static Context UpdateResources(Context context, string lang)
        {
            var locale = new Locale(lang);
            Locale.Default = locale;

            var res = context.Resources;
            var cfg = new Configuration(res.Configuration);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                cfg.SetLocale(locale);
                return context.CreateConfigurationContext(cfg);
            }
            else
            {
#pragma warning disable CS0618
                cfg.Locale = locale;
                res.UpdateConfiguration(cfg, res.DisplayMetrics);
#pragma warning restore CS0618
                return context;
            }
        }
    }
}
