using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using BeatOn.ClientModels;
using BeatOn.Core.MessageHandlers;
using BeatOn.Core.RequestHandlers;
using Newtonsoft.Json;
using QuestomAssets;
using QuestomAssets.AssetOps;
using QuestomAssets.Models;
using QuestomAssets.Utils;
using static Android.App.ActivityManager;

namespace BeatOn.Core
{
    public class BeatOnCore : IDisposable
    {
        private Context _context;
        public ImportManager ImportManager { get; private set; }
        public SyncManager SyncManager { get; private set; }
        public bool ModWasInstalled { get; private set; }
        public BeatOnCore(Context context, Action<string> triggerPackageInstall, Action<string> triggerPackageUninstall, Action<string> triggerStopPackage)
        {
            _context = context;
            _mod = new BeatSaberModder(_context, triggerPackageInstall, triggerPackageUninstall);
            _mod.StatusUpdated += _mod_StatusUpdated;
            
            ImportManager = new ImportManager(_qaeConfig, () => CurrentConfig, () => Engine, ShowToast, () => _SongDownloadManager, SendConfigChangeMessage);
            _SongDownloadManager = new DownloadManager(ImportManager);
            _SongDownloadManager.StatusChanged += _SongDownloadManager_StatusChanged;
            _triggerStopPackage = triggerStopPackage;
            ImageUtils.Instance = new ImageUtilsDroid();
            SyncManager = new SyncManager(_qaeConfig, _SongDownloadManager, () => CurrentConfig, () => Engine, ShowToast);
            KillBeatSaber();
            if (_mod.IsBeatSaberInstalled && _mod.IsInstalledBeatSaberModded)
                ModWasInstalled = true;
        }

        Action<string> _triggerStopPackage;

        private SyncInfo GetSyncInfo()
        {
            try
            {
                if (!_qaeConfig.RootFileProvider.FileExists(Constants.SYNC_INFO))
                    return null;
                return JsonConvert.DeserializeObject<SyncInfo>(_qaeConfig.RootFileProvider.ReadToString(Constants.SYNC_INFO));
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to load sync info.", ex);
                return null;
            }
        }

        private bool CheckReimportSongs()
        {
            try
            {
                
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to check whether songs should be reimported", ex);
                throw;
            }
            return false;
        }

        private void KillBackgroundProcess(string packageName)
        {
            try
            {
                ActivityManager am = (ActivityManager)_context.GetSystemService(Context.ActivityService);
                am.KillBackgroundProcesses(packageName);
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to kill background process for beatsaber.", ex);
            }
        }
        private void KillBeatSaber()
        {
            KillBackgroundProcess("com.beatgames.beatsaber");
        }

        public void Start()
        {
            SetupWebApp();
        }

        private void Cleanup()
        {
            try
            {
                _webServer.Dispose();
            }
            catch
            { }
            
            _webServer = null;
            if (_qae != null)
            {
                try
                {
                    _qae.Dispose();
                }
                catch
                { }
                _qae = null;
            }
            if (_currentConfig != null)
            {
                ClearCurrentConfig();
            }
        }

        private DownloadManager _SongDownloadManager;
        private WebServer _webServer;
        private BeatSaberModder _mod;
        private QuestomAssetsEngine _qae;
        private BeatOnConfig _currentConfig;
        private List<AssetOp> _trackedOps = new List<AssetOp>();

        private bool _checkedBackup = false;

        private QuestomAssetsEngine Engine
        {
            get
            {
                if (_qae == null)
                {
                    if (!_checkedBackup)
                    {
                        _mod.CheckCreateModdedBackup();
                        _checkedBackup = true;
                    }
                    _qae = new QuestomAssetsEngine(_qaeConfig);
                    _qae.OpManager.OpStatusChanged += OpManager_OpStatusChanged;
                    //not the ideal place for this, but if we get an engine, files and things exist, and it's created infrequently enough this should work
                    _mod.CleanupTempApk();
                    if (_mod.IsBeatSaberInstalled && _mod.IsInstalledBeatSaberModded)
                        ModWasInstalled = true;
                }
                return _qae;
            }
        }

        public void BackupPlayerData()
        {
            _mod?.BackupPlayerData(true, true);
        }

        private void ClearCurrentConfig()
        {
            if (_currentConfig != null)
            {
                _currentConfig.PropertyChanged -= CurrentConfig_PropertyChanged;
                _currentConfig = null;
            }
        }

        private void DisposeEngine()
        {
            ClearCurrentConfig();
            if (_qae != null)
            {
                _qae.Dispose();
            }
        }

        private void RolloverConfigBackups()
        {
            try
            {
                for (int x = 3; x >= 0; x--)
                {
                    string sourceName = Constants.LAST_COMMITTED_CONFIG;
                    if (x != 0)
                    {
                        sourceName += $".backup.{x - 1}";
                    }
                    string targetName = Constants.LAST_COMMITTED_CONFIG + $".backup.{x}";
                    Log.LogMsg($"Rolling config backup {sourceName} to {targetName}");
                    if (_qaeConfig.RootFileProvider.FileExists(sourceName))
                    {
                        byte[] b = _qaeConfig.RootFileProvider.Read(sourceName);
                        _qaeConfig.RootFileProvider.Write(targetName, b, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to rollover config backups", ex);
            }
        }
        /// <summary>
        /// Saves the current committed config to disk.  Returns false if the current config isn't committed
        /// </summary>
        private bool SaveCommittedConfigToDisk()
        {
            RolloverConfigBackups();
            //todo: this is SO the wrong place for this.  Suppose I'll move it after I put it here for testing.  probably should save instantly on update and not be part of sync
            SyncManager.Save();


            if (!CurrentConfig.IsCommitted || Engine.HasChanges)
                return false;
            lock (_configFileLock)
            {
                _mod.BackupPlayerData(onlyIfNewer:true, onlyIfBigger: true);
                try
                {
                    try
                    {
                        SyncInfo si = new SyncInfo()
                        {
                            LastSyncDateTime = DateTime.Now,
                            LastSyncedBeatOnVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                            LastSyncedBeatSaberVersion = _mod.GetBeatSaberVersion()
                        };
                        var siString = JsonConvert.SerializeObject(si);
                        _qaeConfig.RootFileProvider.Write(Constants.SYNC_INFO, System.Text.Encoding.UTF8.GetBytes(siString), true);
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Exception trying to save last sync version info.");
                    }
                    var configString = JsonConvert.SerializeObject(CurrentConfig.Config);
                    _qaeConfig.RootFileProvider.Write(Constants.LAST_COMMITTED_CONFIG, System.Text.Encoding.UTF8.GetBytes(configString), true);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Exception saving committed config to disk.", ex);
                    return false;
                }
            }
        }
        private object _configTempSaveDebounceLock = new object();
        private Debouncey<object> _configTempSaveDebounce;
        private void SaveTempConfigToDisk()
        {
            //this isn't needed at the moment.
            return;
            lock (_configTempSaveDebounceLock)
            {
                if (_configTempSaveDebounce == null)
                {
                    _configTempSaveDebounce = new Debouncey<object>(5000, false);
                    _configTempSaveDebounce.Debounced += (e, a) =>
                    {
                        try
                        {
                            var configString = JsonConvert.SerializeObject(CurrentConfig.Config);
                            _qaeConfig.RootFileProvider.Write(Constants.LAST_TEMP_CONFIG, System.Text.Encoding.UTF8.GetBytes(configString), true);
                        }
                        catch (Exception ex)
                        {
                            Log.LogErr($"Exception saving temp config to disk.", ex);
                        }
                    };
                }
            }
        }
        

        private void SendPackageLaunch(string packageName)
        {
            var intent = _context.PackageManager.GetLaunchIntentForPackage(packageName);
            intent.PutExtra("intent_cmd", "");
            intent.PutExtra("intent_pkg", "com.oculus.vrshell");
            _context.StartActivity(intent);
            //Intent intent = new Intent("android.intent.action.VIEW");
            //intent.SetComponent(new ComponentName("com.oculus.vrshell", "com.oculus.vrshell.MainActivity"));
            //intent.SetData(Android.Net.Uri.Parse("apk://"+ packageName));
            //_context.StartActivity(intent);
        }

        //private void UpdateConfig(BeatSaberQuestomConfig config)
        //{
        //    var currentCfg = Engine.GetCurrentConfig();
        //    ClearCurrentConfig();
        //    bool matches = config.Matches(currentCfg);

        //    if (!matches)
        //    {
        //        Engine.UpdateConfig(config);
        //        config = Engine.GetCurrentConfig();
        //    }
        //    else
        //    {
        //        config = currentCfg;
        //    }
        //    _currentConfig = new BeatOnConfig()
        //    {
        //        Config = config,
        //        IsCommitted = matches || Engine.HasChanges
        //    };
        //    _currentConfig.PropertyChanged += CurrentConfig_PropertyChanged;
        //    SendConfigChangeMessage();
        //}

        private BeatOnConfig CurrentConfig
        {
            get
            {
                if (_currentConfig == null)
                {
                    try
                    {
                        var engineNull = _qae == null;
                        var config = Engine.GetCurrentConfig();

                        _currentConfig = new BeatOnConfig()
                        {
                            Config = config,
                            SyncConfig = SyncManager.SyncConfig
                        };
                        if (_mod.IsBeatSaberInstalled && _mod.IsInstalledBeatSaberModded)
                        {
                            _currentConfig.BeatSaberVersion = _mod.GetBeatSaberVersion();
                        }
                        _currentConfig.IsCommitted = !Engine.HasChanges;
                        _currentConfig.PropertyChanged += CurrentConfig_PropertyChanged;
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr("Critical exception loading current config", ex);
                        ShowToast("Critical Error", "Something has gone wrong and Beat On can't function.  Try Reset Assets in the tools menu.", ToastType.Error, 60);
                    }
                }
                return _currentConfig;
            }
        }

        public void DownloadUrl(string url, string mimeType)
        {
            //if (mimeType != "application/zip" && !url.ToLower().EndsWith("json") && )
            //{
            //    ShowToast("Unable to Download", "File isn't a zip file!  Not downloading it.", ToastType.Error, 8);
            //    return;
            //}
            var uri = new Uri(url);
            //ShowToast("Starting Download...", uri.ToString(), ToastType.Info, 2);

            //var fileName = Path.GetFileNameWithoutExtension(uri.LocalPath);
            //if (_qaeConfig.RootFileProvider.FileExists(Path.Combine(_qaeConfig.SongsPath, fileName)))
            //{
            //    ShowToast("Unable to Download", "A custom song folder with the name of this zip already exists.  Not downloading it.", ToastType.Error, 8);
            //    return;
            //}
            _SongDownloadManager.DownloadFile(url);
        }
 
        private void CurrentConfig_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SendConfigChangeMessage();
        }

        private QaeConfig _qaeConfig
        {
            get
            {
                var q = new QaeConfig()
                {
                    RootFileProvider = new FolderFileProvider(Constants.ROOT_BEAT_ON_DATA_PATH, false),
                    PlaylistArtPath = "Art",
                    AssetsPath = Constants.BEATSABER_ASSETS_FOLDER_NAME,
                    ModsSourcePath = Constants.MODS_FOLDER_NAME,
                    SongsPath = Constants.CUSTOM_SONGS_FOLDER_NAME,
                    ModLibsFileProvider = new FolderFileProvider(Constants.MODLOADER_MODS_PATH, false, false),
                    ModsStatusFile = Constants.MOD_STATUS_FILE,
                    BackupApkFileAbsolutePath = Constants.BEATSABER_APK_BACKUP_FILE,
                    ModdedFallbackBackupPath = Constants.BEATSABER_APK_MODDED_BACKUP_FILE,
                    PlaylistsPath = Constants.PLAYLISTS_FOLDER_NAME,
                    EmbeddedResourcesFileProvider = new ResourceFileProvider(_context.Assets, "resources")
                };
                q.SongFileProvider = q.RootFileProvider;
                return q;
            }
        }

        private object _configFileLock = new object();

        private void SendStatusMessage(string message)
        {
            SendMessageToClient(new HostSetupEvent() { SetupEvent = SetupEventType.StatusMessage, Message = message });
        }

        private object _configChangeMsgDebounceLock = new object();
        private Debouncey<object> _configChangeMsgDebounce;
        private bool _suppressConfigChangeMessage = false;
        private void SendConfigChangeMessage()
        {
            lock (_configChangeMsgDebounceLock)
            {
                if (_configChangeMsgDebounce == null)
                {
                    _configChangeMsgDebounce = new Debouncey<object>(100, true);
                    _configChangeMsgDebounce.Debounced += (e, a) =>
                     {
                         if (!_suppressConfigChangeMessage)
                             _webServer.SendMessage(new HostConfigChangeEvent() { UpdatedConfig = CurrentConfig });
                     };
                }
            }
            //todo: see if this is needed or makes sense here.  Could cause noise on the messages.
            CurrentConfig.IsCommitted = CurrentConfig.IsCommitted && (!Engine.HasChanges);
            _configChangeMsgDebounce.EventRaised(this, null);
            SaveTempConfigToDisk();
        }

        private object _sendClientOpsChangedLock = new object();
        private Debouncey<HostOpStatus> _sendClientOpsChanged;
        private void OpManager_OpStatusChanged(object sender, QuestomAssets.AssetOps.AssetOp e)
        {
            List<AssetOp> opCopy;
            lock (_trackedOps)
            {
                if (!_trackedOps.Any(x=> x.ID == e.ID))
                    _trackedOps.Add(e);

                _trackedOps.RemoveAll(x => x.Status == OpStatus.Complete);

                opCopy = _trackedOps.ToList();
            }

            HostOpStatus opstat = new HostOpStatus();

            foreach (var op in opCopy)
            {
                string exmsg = null;
                //if (op.Exception != null)
                //{
                //    exmsg = $"{op.Exception.Message} {op.Exception.StackTrace}";
                //    var ex = op.Exception.InnerException;
                //    while (ex != null)
                //    {
                //        exmsg += $"\nInnerException: {ex.Message} {ex.StackTrace}";
                //        ex = ex.InnerException;
                //    }
                //}
                opstat.Ops.Add(new HostOp() { ID = op.ID, OpDescription = op.GetType().Name, Status = op.Status, Error = op.Exception?.Message });
            }
            lock(_sendClientOpsChangedLock)
            {
                if (_sendClientOpsChanged == null)
                {
                    _sendClientOpsChanged = new Debouncey<HostOpStatus>(400, false);
                    _sendClientOpsChanged.Debounced += (e2, a) =>
                    {
                        lock (lastSendOpsLock)
                        {
                            lastOpsSendHadOps = (a.Ops.Count > 0);
                            SendMessageToClient(a);
                        }
                    };
                }
            }
            if (e.Status == OpStatus.Complete && e.IsWriteOp)
            {
                CurrentConfig.IsCommitted = CurrentConfig.IsCommitted && (!Engine.HasChanges);
                SendConfigChangeMessage();
            }
            //if the last send didn't have ops and this one does, send the message immediately in addition to debouncing it.
            //  this will send a duplicate message because the debounce is called in addition to the message being sent, but it'll assure keeping things in sync with the client
            lock(lastSendOpsLock)
            {
                if ((opstat.Ops.Count > 0) && !lastOpsSendHadOps)
                {
                    lastOpsSendHadOps = true;
                    SendMessageToClient(opstat);
                }
            }
            _sendClientOpsChanged.EventRaised(this, opstat);
        }
        private object lastSendOpsLock = new object();
        private bool lastOpsSendHadOps = false;

        private void SendPackageStop(string packageName)
        {
            try
            {
                Intent intent = new Intent("com.oculus.vrshell.intent.action.LAUNCH");
                intent.SetPackage("com.oculus.vrshell");
                intent.PutExtra("intent_data", Android.Net.Uri.Parse("systemux://home"));
                intent.PutExtra("blackscreen", false);
                //var intent = new Intent("com.oculus.system_activity");
                //intent.SetPackage(packageName);

                //intent.PutExtra("intent_pkg", "com.oculus.vrshell");
                //intent.PutExtra("intent_cmd", "{\"Command\":\"exitToHome\", \"PlatformUIVersion\":3, \"ToPackage\":\"" + packageName + "\"}");
                //_context.SendBroadcast(intent);
                //intent.PutExtra("intent_cmd", "{\"Command\":\"returnToLauncher\", \"PlatformUIVersion\":3, \"ToPackage\":\"" + packageName + "\"}");
                _context.SendBroadcast(intent);
                Task.Delay(3000).ContinueWith(t => { KillBackgroundProcess(packageName); });
            } catch (Exception e)
            {
                Log.LogErr("Exception trying to send package exittohome messages", e);
            }
        }

        private void _SongDownloadManager_StatusChanged(object sender, DownloadStatusChangeArgs e)
        {
            var dl = sender as Download;
            if (e.UpdateType == DownloadStatusChangeArgs.DownloadStatusUpdateType.StatusChange)
            {
                switch (e.Status)
                {
                    case DownloadStatus.Downloading:
                        //ShowToast("Downloading file...", dl.DownloadUrl.ToString(), ToastType.Info, 3);
                        break;
                    case DownloadStatus.Aborted:
                        break;
                    case DownloadStatus.Failed:
                        if (!dl.SuppressToast)
                            ShowToast("Download failed", dl.DownloadUrl.ToString(), ToastType.Error, 5);
                        break;
                    case DownloadStatus.Processed:
                        //ShowToast("Download Processed", dl.DownloadUrl.ToString(), ToastType.Success, 3);
                        break;
                }
                var hds = new HostDownloadStatus();
                var dls = _SongDownloadManager.Downloads;
                dls.ForEach(x => hds.Downloads.Add(new HostDownload() { ID = x.ID, PercentageComplete = x.PercentageComplete, Status = x.Status, Url = x.DownloadUrl.ToString() }));
                SendMessageToClient(hds);
            }
        }

        private void _mod_StatusUpdated(object sender, string e)
        {
            SendStatusMessage(e);
        }

        private void SendMessageToClient(ClientModels.MessageBase message)
        {
            _webServer.SendMessage(message);
        }
        private void ShowToast(string title, string message, ToastType type = ToastType.Info, float durationSec = 3.0F)
        {
            SendMessageToClient(new HostShowToast() { Title = title, Message = message, ToastType = type, Timeout = (int)(durationSec * 1000) });
        }

        private void FullEngineReset()
        {
            _currentConfig = null;
            if (_qae != null)
            {
                _qae.Dispose();
                _qae = null;
            }
        }
        private bool CanSync()
        {
            //quick little double check to make sure there's no small gap in between ops getting queued.
            if (Engine.OpManager.IsProcessing || _SongDownloadManager.InProgress > 0)
            {
                return false;
            }
            System.Threading.Thread.Sleep(200);
            if (Engine.OpManager.IsProcessing || _SongDownloadManager.InProgress > 0)
            {
                return false;
            }
            if (Engine.IsManagerLocked())
            {
                return false;
            }

            return true;
        }

        public event EventHandler HardQuitTriggered;
        private void HardQuit()
        {
            HardQuitTriggered?.Invoke(this, new EventArgs());
        }

        private void SetupWebApp()
        {
            _webServer = new WebServer(_context.Assets, "www");
            _webServer.Router.AddRoute("GET", "beatsaber/config", new GetConfig(() => CurrentConfig));
            _webServer.Router.AddRoute("GET", "beatsaber/song/cover", new GetSongCover(_qaeConfig, () => CurrentConfig));
            _webServer.Router.AddRoute("GET", "beatsaber/playlist/cover", new GetPlaylistCover(() => CurrentConfig, _qaeConfig));
            _webServer.Router.AddRoute("GET", "beatsaber/mod/cover", new GetModCover(_qaeConfig, () => CurrentConfig));
            _webServer.Router.AddRoute("POST", "beatsaber/upload", new PostFileUpload(_mod, ShowToast, () => ImportManager));
            _webServer.Router.AddRoute("POST", "beatsaber/commitconfig", new PostCommitConfig(_mod, ShowToast, SendMessageToClient, () => Engine, () => CurrentConfig, SendConfigChangeMessage, () => CanSync(), KillBeatSaber, SaveCommittedConfigToDisk));
            _webServer.Router.AddRoute("POST", "beatsaber/reloadsongfolders", new PostReloadSongFolders(_mod, _qaeConfig, ShowToast, SendMessageToClient, () => Engine, () => CurrentConfig, SendConfigChangeMessage, (suppress) => { _suppressConfigChangeMessage = suppress; }));
            _webServer.Router.AddRoute("GET", "mod/status", new GetModStatus(_mod));
            _webServer.Router.AddRoute("GET", "mod/netinfo", new GetNetInfo(_webServer));
            _webServer.Router.AddRoute("POST", "mod/install/step1", new PostModInstallStep1(_mod, SendMessageToClient));
            _webServer.Router.AddRoute("POST", "mod/install/step2", new PostModInstallStep2(_mod, SendMessageToClient));
            _webServer.Router.AddRoute("POST", "mod/install/step3", new PostModInstallStep3(_mod, SendMessageToClient));
            _webServer.Router.AddRoute("POST", "beatsaber/playlist/autocreate", new PostAutoCreatePlaylists(() => Engine, () => CurrentConfig));
            _webServer.Router.AddRoute("POST", "mod/resetassets", new PostResetAssets(_mod, ShowToast, SendConfigChangeMessage, FullEngineReset));
            _webServer.Router.AddRoute("POST", "mod/uninstallbeatsaber", new PostUninstallBeatSaber(_mod, ShowToast));
            _webServer.Router.AddRoute("DELETE", "/beatsaber/config", new DeletePendingConfig(SendConfigChangeMessage, FullEngineReset));
            _webServer.Router.AddRoute("POST", "/mod/postlogs", new PostUploadLogs());
            _webServer.Router.AddRoute("GET", "/mod/images", new GetImages(_qaeConfig));
            _webServer.Router.AddRoute("GET", "/mod/image", new GetImage(_qaeConfig));
            _webServer.Router.AddRoute("POST", "/mod/exit", new PostExit(HardQuit));
            _webServer.Router.AddRoute("POST", "/mod/package", new PostPackageAction(SendPackageLaunch, SendPackageStop));
            _webServer.Router.AddRoute("PUT", "/beatsaber/config", new PutConfig(() => Engine, () => CurrentConfig, SendConfigChangeMessage));
            _webServer.Router.AddRoute("POST", "/beatsaber/config/restore", new PostConfigRestore(() => Engine, () => _qaeConfig, ()=> CurrentConfig, SendConfigChangeMessage, _mod));
            _webServer.Router.AddRoute("GET", "/mod/startupstatus", new GetStartupStatus(() => GetSyncInfo(), () => CurrentConfig, _mod, () => _qaeConfig, () => Engine));
            //TEST ONE
            _webServer.Router.AddRoute("GET", "/beatsaber/sync", new GetSyncConfig(() => SyncManager));
            _webServer.Router.AddRoute("POST", "/beatsaber/sync", new PostSyncSync(() => SyncManager));


            //if you add a new MessageType and a handler here, make sure the type is added in MessageTypeConverter.cs
            _webServer.AddMessageHandler(MessageType.DeletePlaylist, new ClientDeletePlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.AddOrUpdatePlaylist, new ClientAddOrUpdatePlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.MoveSongToPlaylist, new ClientMoveSongToPlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.DeleteSong, new ClientDeleteSongHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.GetOps, new ClientGetOpsHandler(_trackedOps, SendMessageToClient));
            _webServer.AddMessageHandler(MessageType.SortPlaylist, new ClientSortPlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.AutoCreatePlaylists, new ClientAutoCreatePlaylistsHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.SetModStatus, new ClientSetModStatusHandler(() => Engine, () => CurrentConfig, SendMessageToClient));
            _webServer.AddMessageHandler(MessageType.MoveSongInPlaylist, new ClientMoveSongInPlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.MovePlaylist, new ClientMovePlaylistHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.DeleteMod, new ClientDeleteModHandler(() => Engine, () => CurrentConfig, SendMessageToClient));
            _webServer.AddMessageHandler(MessageType.ChangeColor, new ClientChangeColorHandler(() => Engine, () => CurrentConfig));
            _webServer.AddMessageHandler(MessageType.SetBeastSaberUsername, new ClientSetBeastSaberUsernameHandler(() => SyncManager));
            _webServer.AddMessageHandler(MessageType.UpdateFeedReader, new ClientUpdateFeedReaderHandler(() => SyncManager));
            _webServer.AddMessageHandler(MessageType.SyncSaberSync, new ClientSyncSaberSyncHandler(() => SyncManager));
            _webServer.AddMessageHandler(MessageType.StopDownloads, new ClientStopDownloadsHandler(() => _SongDownloadManager));
            _webServer.Start();
        }


        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Cleanup();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        public string Url
        {
            get
            {
                if (_webServer == null)
                    return null;
                if (!_webServer.IsRunning)
                    return null;
                return _webServer.ListeningOnUrl;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~BeatOnCore()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }
}