using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using BeatOn.ClientModels;
using Newtonsoft.Json;
using QuestomAssets;

namespace BeatOn.Core.RequestHandlers
{
    public class PostConfigRestore : IHandleRequest
    {
        private GetQaeDelegate _getQae;
        private Func<QaeConfig> _getQaeConfig;
        private Action _triggerConfigChanged;
        private GetBeatOnConfigDelegate _getConfig;
        private BeatSaberModder _mod;
        public PostConfigRestore(GetQaeDelegate getQae, Func<QaeConfig> getQaeConfig, GetBeatOnConfigDelegate getConfig, Action triggerConfigChanged, BeatSaberModder mod)
        {
            _getQae = getQae;
            _getQaeConfig = getQaeConfig;
            _getConfig = getConfig;
            _triggerConfigChanged = triggerConfigChanged;
            _mod = mod;
        }

        public void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;

            try
            {
                if (string.IsNullOrWhiteSpace(req.Url.Query))
                {
                    resp.BadRequest("Expected configtype");
                    return;
                }
                string configtype = null;
                foreach (string kvp in req.Url.Query.TrimStart('?').Split("&"))
                {
                    var split = kvp.Split('=');
                    if (split.Count() < 1)
                        continue;
                    if (split[0].ToLower() == "configtype")
                    {
                        configtype = Java.Net.URLDecoder.Decode(split[1]);
                        break;
                    }
                }
                if (string.IsNullOrEmpty(configtype))
                {
                    resp.BadRequest("Expected configtype");
                    return;
                }
                bool committed = false;
                if (configtype.ToLower() == "committed")
                {
                    committed = true;
                }
                else if (configtype.ToLower() == "temp")
                {
                    committed = false;
                }
                else
                {
                    resp.BadRequest("config type should be 'committed' or 'temp'");
                    return;
                }
                var curConfig = _getConfig().Config;
                var qc = _getQaeConfig();
                List<string> existingSongIDs = curConfig.Playlists.SelectMany(x => x.SongList).Select(x => x.SongID).ToList();
                var configFile = committed ? Constants.LAST_COMMITTED_CONFIG : Constants.LAST_TEMP_CONFIG;
                if (!qc.RootFileProvider.FileExists(configFile))
                {
                    Log.LogErr($"Tried to restore config, but {configFile} did not exist.");
                    resp.BadRequest("Config file did not exist.");
                    return;
                }
                QuestomAssets.Models.BeatSaberQuestomConfig cfg;
                try
                {
                    cfg = JsonConvert.DeserializeObject<QuestomAssets.Models.BeatSaberQuestomConfig>(_getQaeConfig().RootFileProvider.ReadToString(configFile));
                    foreach (var pl in cfg.Playlists)
                    {
                        foreach (var s in pl.SongList)
                        {
                            if (!existingSongIDs.Contains(s.SongID))
                            {
                                if (string.IsNullOrWhiteSpace(s.CustomSongPath) && qc.RootFileProvider.FileExists(Constants.CUSTOM_SONGS_FOLDER_NAME.CombineFwdSlash(s.SongID)))
                                {
                                    s.CustomSongPath = Constants.CUSTOM_SONGS_FOLDER_NAME.CombineFwdSlash(s.SongID);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Config file {configFile} failed to deserialize!", ex);
                    resp.Error("Config file failed to deserialize.");
                    return;
                }
                foreach (var pl in cfg.Playlists)
                {
                    try
                    {
                        var covername = Constants.PLAYLISTS_FOLDER_NAME.CombineFwdSlash(pl.PlaylistID + ".png");
                        if (qc.RootFileProvider.FileExists(covername))
                        {
                            pl.CoverImageBytes = qc.RootFileProvider.Read(covername);
                        }
                        else
                        {
                            Log.LogErr($"Restoring playlist art failed, {covername} didn't exist.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Exception restoring playlist art for id {pl.PlaylistID}");
                    }
                }
                try
                {
                    _getQae().UpdateConfig(cfg);
                }
                catch (AssetOpsException aoe)
                {
                    Log.LogErr($"Updating config completed, but {aoe.FailedOps?.Count} operations failed.");
                }
                Log.LogMsg("Reload song folders sending change message");
                _getConfig().Config = _getQae().GetCurrentConfig();
                _triggerConfigChanged();
                Log.LogMsg("Restoring backed up PlayerData.dat");
                _mod.RestorePlayerData(true);
                Log.LogMsg("Reload song folders responding OK");
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception handling Post config restore!", ex);
                resp.Serialize(500, new { BeatSaberNeedsUninstall = false, ErrorMessage = ex.FullMessage() });
            }

        }
    }
}