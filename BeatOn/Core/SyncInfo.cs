using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace BeatOn.Core
{
    public class SyncInfo
    {
        public string LastSyncedBeatSaberVersion { get; set; }
        public string LastSyncedBeatOnVersion { get; set; }
        public DateTime LastSyncDateTime { get; set; }
    }
}