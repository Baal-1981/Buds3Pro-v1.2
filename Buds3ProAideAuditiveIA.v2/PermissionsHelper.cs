using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Buds3ProAideAuditiveIA.v2
{
    public static class PermissionsHelper
    {
        public const int ReqAudio = 1001;
        public const int ReqBt = 1002;

        public static bool HasRecordAudio(Activity a) =>
            a.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted;

        public static void EnsureRecordAudio(Activity a)
        {
            if (!HasRecordAudio(a))
                a.RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
        }

        public static bool NeedsBtConnect() => Build.VERSION.SdkInt >= BuildVersionCodes.S;

        public static bool HasBtConnect(Activity a)
        {
            if (!NeedsBtConnect()) return true;
            return a.CheckSelfPermission(Manifest.Permission.BluetoothConnect) == Permission.Granted;
        }

        public static void EnsureBtConnect(Activity a)
        {
            if (NeedsBtConnect() && !HasBtConnect(a))
                a.RequestPermissions(new[] { Manifest.Permission.BluetoothConnect }, ReqBt);
        }
    }
}
