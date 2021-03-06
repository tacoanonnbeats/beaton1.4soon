﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Newtonsoft.Json;
using QuestomAssets;
using QuestomAssets.Models;

namespace BeatOn.Core.RequestHandlers
{
    public class PutConfig : IHandleRequest
    {
        private GetQaeDelegate _getQae;
        private Action _triggerConfigChanged;
        private GetBeatOnConfigDelegate _getConfig;
        public PutConfig( GetQaeDelegate getQae, GetBeatOnConfigDelegate getConfig, Action triggerConfigChanged)
        {
            _getQae = getQae;
            _getConfig = getConfig;
            _triggerConfigChanged = triggerConfigChanged;
        }

        public void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;
            lock (GetConfig.CONFIG_LOCK)
            {
                try
                {
                    BeatSaberQuestomConfig config;
                    //todo: check content type header
                    using (JsonTextReader jtr = new JsonTextReader(new StreamReader(req.InputStream)))
                    {
                        config = (new JsonSerializer()).Deserialize<BeatSaberQuestomConfig>(jtr);
                    }
                    Log.LogMsg("Read new config on put config");
                    if (config == null)
                    {
                        throw new Exception("Error deserializing config!");
                    }
                    try
                    {
                        _getQae().UpdateConfig(config);
                    }
                    catch (AssetOpsException aoe)
                    {
                        Log.LogErr($"Updating config completed, but {aoe.FailedOps?.Count} operations failed.");
                    }
                    Log.LogMsg("Reload song folders sending change message");
                    _getConfig().Config = _getQae().GetCurrentConfig();
                    _triggerConfigChanged();
                    Log.LogMsg("Reload song folders responding OK");
                }
                catch (Exception ex)
                {
                    Log.LogErr("Exception getting config!", ex);
                    resp.Error($"Exception occurred putting config: {ex.Message}");
                }
            }
        }
    }
}