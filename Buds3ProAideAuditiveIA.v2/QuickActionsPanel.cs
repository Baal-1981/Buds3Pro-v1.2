// QuickActionsPanel.cs - Correction IDE0017
#nullable enable
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Buds3ProAideAuditiveIA.v2
{
    public class QuickActionsPanel : LinearLayout
    {
        public QuickActionsPanel(Context context) : base(context) => Initialize();
        public QuickActionsPanel(Context context, IAttributeSet? attrs) : base(context, attrs) => Initialize();

        private void Initialize()
        {
            Orientation = Orientation.Horizontal;

            // CORRECTION IDE0017 : Utiliser l'initialisation d'objet
            var btn = new Button(Context)
            {
                Text = "Action",
                ContentDescription = "Bouton d'action",
                ImportantForAccessibility = Android.Views.ImportantForAccessibility.Yes
            };

            AddView(btn, new LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent));
        }

        /// <summary>
        /// Si la description vient du monde Java, passe ici pour conversion sûre.
        /// </summary>
        public void SetButtonDescriptionFromJava(Button button, Java.Lang.String javaDescription)
        {
            AccessibleUIComponents.SetContentDescription(button, javaDescription);
        }
    }
}