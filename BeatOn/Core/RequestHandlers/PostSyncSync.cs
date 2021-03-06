﻿using System;
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
using QuestomAssets;

namespace BeatOn.Core.RequestHandlers
{

    /* THIS IS A TEST CLASS.  only here because i'm too lazy to hook up UI to test it */
    public class PostSyncSync : IHandleRequest
    {
        Func<SyncManager> _getSyncManager;
        public PostSyncSync(Func<SyncManager> getSyncManager)
        {
            _getSyncManager = getSyncManager;
        }

        public void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;
            try
            {
                _getSyncManager().Sync();
                resp.Ok();
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception handling get net info!", ex);
                resp.StatusCode = 500;
            }
        }
    }
}