// AccessibleUIComponents.cs
#nullable enable
using Android.Views;
using AndroidX.Core.View;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Utilitaires d’accessibilité (conversion String/CharSequence -> string,
    /// réglages heading/importance).
    /// </summary>
    public static class AccessibleUIComponents
    {
        /// <summary>
        /// Définit la description de contenu (screen reader) en acceptant string/Java.Lang.String/ICharSequence/etc.
        /// </summary>
        public static void SetContentDescription(View view, object? description)
        {
            // IMPORTANT : ordre des cas pour éviter CS8510
            string text = description switch
            {
                Java.Lang.String jls => jls.ToClrString(),
                Java.Lang.ICharSequence seq => seq.ToClrString(),
                null => string.Empty,
                _ => description?.ToString() ?? string.Empty
            };

            view.ContentDescription = text;
        }

        /// <summary>Marque la vue comme en-tête pour les lecteurs d’écran.</summary>
        public static void SetAsHeading(View view, bool isHeading = true)
        {
            ViewCompat.SetAccessibilityHeading(view, isHeading);
        }

        /// <summary>Règle l’importance de la vue pour l’accessibilité.</summary>
        public static void SetImportantForAccessibility(View view, bool important = true)
        {
            view.ImportantForAccessibility = important
                ? Android.Views.ImportantForAccessibility.Yes
                : Android.Views.ImportantForAccessibility.Auto;
        }
    }
}
