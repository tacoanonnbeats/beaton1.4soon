using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using BeatOn.ClientModels;
using QuestomAssets;

namespace BeatOn.Core.RequestHandlers
{
    public class GetStartupStatus : IHandleRequest
    {
        private Func<SyncInfo> _getSyncInfo;
        private GetBeatOnConfigDelegate _getConfig;
        private BeatSaberModder _mod;
        private Func<QaeConfig> _getQaeConfig;
        private GetQaeDelegate _getQae;
        public GetStartupStatus(Func<SyncInfo> getSyncInfo, GetBeatOnConfigDelegate getConfig, BeatSaberModder mod, Func<QaeConfig> getQaeConfig, GetQaeDelegate getQae)
        {
            _getSyncInfo = getSyncInfo;
            _getConfig = getConfig;
            _mod = mod;
            _getQaeConfig = getQaeConfig;
            _getQae = getQae;
        }

        public void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;
            try
            {
                var ss = new StartupStatus();
                ss.UpgradeRestoreAvailable = false;
                if (_mod.IsBeatSaberInstalled && _mod.IsInstalledBeatSaberModded)
                {
                    var qaeConfig = _getQaeConfig();
                    if (qaeConfig.RootFileProvider.FileExists(Constants.LAST_COMMITTED_CONFIG))
                    {
                        var si = _getSyncInfo();
                        var bsVer = _mod.GetBeatSaberVersion();
                        if (si?.LastSyncedBeatSaberVersion != bsVer)
                        {
                            var qae = _getQae();
                            if (!qae.OpManager.IsProcessing)
                                ss.UpgradeRestoreAvailable = true;
                        }                            
                    }
                }
                resp.SerializeOk(ss);
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception handling mod status!", ex);
                resp.StatusCode = 500;
            }
        }
    }
}