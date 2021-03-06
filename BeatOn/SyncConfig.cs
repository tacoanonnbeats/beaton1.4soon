﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using FeedReader;
using QuestomAssets;

namespace BeatOn
{
    /// <summary>
    /// Overall configuration for configuration of the SyncManager
    /// </summary>
    public class SyncConfig : INotifyPropertyChanged
    {
        public SyncConfig()
        {
            FeedReaders = new ObservableCollection<FeedConfig>();
        }

        /// <summary>
        /// BeastSaber username
        /// </summary>
        public string BeastSaberUsername { get; set; }

        /// <summary>
        /// True to check all existing songs for any updates to the song itself.  This will likely be extremely slow.
        /// </summary>
        public bool CheckExistingSongsUpdated { get; set; } = false;

        private ObservableCollection<FeedConfig> _feedReaders;

         /// <summary>
        /// List of definitions and settings for feed readers that will sync.
        /// </summary>
        public ObservableCollection<FeedConfig> FeedReaders
        {
            get
            {
                return _feedReaders;
            }
            set
            {
                if (value != _feedReaders)
                {
                    if (_feedReaders != null)
                        _feedReaders.CollectionChanged -= _feedReaders_CollectionChanged;

                    _feedReaders = value;

                    if (value != null)
                        _feedReaders.CollectionChanged += _feedReaders_CollectionChanged;

                    PropChanged(nameof(FeedReaders));
                }

            }
        }

        private void _feedReaders_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var oi in e.OldItems)
                {
                    var f = oi as FeedConfig;
                    f.PropertyChanged -= feedReader_PropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var ni in e.NewItems)
                {
                    var f = ni as FeedConfig;
                    f.PropertyChanged += feedReader_PropertyChanged;
                }
            }
        }

        private void feedReader_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            PropChanged(nameof(FeedReaders));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void PropChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public abstract class FeedConfig : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        public Guid ID
        {
            get
            {
                return _id;
            }
            set
            {
                bool changed = value != _id;
                _id = value;
                if (changed)
                    PropChanged(nameof(ID));
            }
        }

        private int _maxSongs = 30;
        public int MaxSongs
        {
            get
            {
                return _maxSongs;
            }
            set
            {
                bool changed = _maxSongs != value;
                _maxSongs = value;
                if (changed)
                    PropChanged(nameof(MaxSongs));
            }
        }

        /// <summary>
        /// The last date/time that a sync was run
        /// </summary>
        public DateTime? LastSyncAttempt { get; set; }

        /// <summary>
        /// The last date/time that a sync was run
        /// </summary>
        public DateTime? LastSyncSuccess { get; set; }

        private bool _isEnabled;

        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }
            set
            {
                bool changed = value != _isEnabled;
                _isEnabled = value;
                if (changed)
                    PropChanged(nameof(IsEnabled));
            }
        }

        protected abstract IFeedReader FeedReader { get; }
        protected abstract IFeedSettings FeedSettings { get; }
        public abstract string PlaylistID { get; }
        public abstract string DisplayName { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void PropChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        /// <summary>
        /// Keyed Dictionary<PlaylistID, Tuple<PlaylistName, Dictionary<SongHash, scrapedsong>>>.  Yes I should just make a class.
        /// </summary>
        public virtual Dictionary<string, Tuple<string, Dictionary<string, ScrapedSong>>> GetSongsByPlaylist()
        {
            var dict = new Dictionary<string, Tuple<string, Dictionary<string, ScrapedSong>>>();
            dict.Add(PlaylistID, new Tuple<string, Dictionary<string, ScrapedSong>>(DisplayName, FeedReader.GetSongsFromFeed(FeedSettings)));
            return dict;
        }
    }

    public class BeastSaberFeedConfig : FeedConfig
    {
        public override string PlaylistID => $"SyncService_BeastSaber{FeedType.ToString()}";
        public override string DisplayName
        {
            get
            {
                switch (FeedType)
                {
                    case BeastSaberFeeds.BOOKMARKS:
                        return "Bookmarks";
                    case BeastSaberFeeds.CURATOR_RECOMMENDED:
                        return "Curator Recommended";
                    case BeastSaberFeeds.FOLLOWING:
                        return "Following";
                    default:
                        Log.LogErr($"Unhandled Beast Saber FeedType {FeedType}");
                        return "Sync";
                }
            }
        }
        //todo: eval what the quest can handle
        private const int MAX_CONCURRENCY = 4;
        internal string BeastSaberUsername { get; set; }

        private BeastSaberFeeds _feedType;
        public BeastSaberFeeds FeedType
        {
            get
            {
                return _feedType;
            }
            set
            {
                bool changed = _feedType != value;
                _feedType = value;
                if (changed)
                    PropChanged(nameof(FeedType));
            }
        }

        protected override IFeedSettings FeedSettings
        {
            get
            {
                return new BeastSaberFeedSettings((int)FeedType) { MaxSongs = this.MaxSongs };
            }
        }

        protected override IFeedReader FeedReader
        {
            get
            {
                return new BeastSaberReader(BeastSaberUsername, MAX_CONCURRENCY);
            }
        }
    }

    public class BeatSaverFeedConfig : FeedConfig
    {
        public BeatSaverFeedConfig()
        {
            Authors = new ObservableCollection<string>();
            Authors.CollectionChanged += Authors_CollectionChanged;
        }

        private void Authors_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            PropChanged("Authors");
        }

        private BeatSaverFeeds _feedType;
        public BeatSaverFeeds FeedType
        {
            get
            {
                return _feedType;
            }
            set
            {
                bool changed = value != _feedType;
                _feedType = value;
                if (changed)
                    PropChanged(nameof(FeedType));
            }
        }

        public ObservableCollection<string> Authors { get; private set; }
        public override string PlaylistID => $"SyncService_BeatSaver{FeedType.ToString()}";

        public override string DisplayName
        {
            get
            {
                switch (FeedType)
                {
                    case BeatSaverFeeds.DOWNLOADS:
                        return "Top Downloads";
                    case BeatSaverFeeds.HOT:
                        return "Hot";
                    case BeatSaverFeeds.LATEST:
                        return "Latest";
                    case BeatSaverFeeds.PLAYS:
                        return "Top Played";
                    case BeatSaverFeeds.AUTHOR:
                        return $"By Authors"; //todo: consider {((Authors == null) ? "(none)" : ((Authors.Count > 1) ? string.Join(", ", Authors) : Authors.First()))}";
                    case BeatSaverFeeds.SEARCH:
                        throw new NotImplementedException();
                    default:
                        Log.LogErr($"Unhandled Score Saber FeedType {FeedType}");
                        return "Sync";
                }
            }
        }

        protected override IFeedReader FeedReader
        {
            get
            {
                return new BeatSaverReader();
            }
        }

        protected override IFeedSettings FeedSettings
        {
            get
            {
                return new BeatSaverFeedSettings((int)FeedType)
                {
                    MaxSongs = MaxSongs,
                    Authors = Authors?.ToArray()
                };
            }
        }
        /// <summary>
        /// Keyed Dictionary<PlaylistID, Tuple<PlaylistName, Dictionary<SongHash, scrapedsong>>>.  Yes I should just make a class.
        /// </summary>
        public override Dictionary<string, Tuple<string, Dictionary<string, ScrapedSong>>> GetSongsByPlaylist()
        {
            if (FeedType != BeatSaverFeeds.AUTHOR)
                return base.GetSongsByPlaylist();

            var dict = new Dictionary<string, Tuple<string, Dictionary<string, ScrapedSong>>>();
            var songs = FeedReader.GetSongsFromFeed(FeedSettings);
            foreach (var song in songs)
            {
                var plID = PlaylistID + "_" + song.Value.MapperName.Replace(" ", "");
                Tuple<string, Dictionary<string, ScrapedSong>> authorPL;
                if (!dict.ContainsKey(plID))
                {
                    authorPL = new Tuple<string, Dictionary<string, ScrapedSong>>($"Author {song.Value.MapperName}", new Dictionary<string, ScrapedSong>());
                    dict.Add(plID, authorPL);
                }
                else
                {
                    authorPL = dict[plID];
                }
                if (!authorPL.Item2.ContainsKey(song.Key))
                {
                    authorPL.Item2.Add(song.Key, song.Value);
                }
            }
            return dict;
        }
    }

    public class ScoreSaberFeedConfig : FeedConfig
    {
        public override string PlaylistID => $"SyncService_ScoreSaber{FeedType.ToString()}";
        public override string DisplayName
        {
            get
            {
                switch (FeedType)
                {
                    case ScoreSaberFeeds.LATEST_RANKED:
                        return "Latest Ranked";
                    case ScoreSaberFeeds.TOP_PLAYED:
                        return "Top Played";
                    case ScoreSaberFeeds.TOP_RANKED:
                        return "Top Ranked";
                    case ScoreSaberFeeds.TRENDING:
                        return "Trending";
                    default:
                        Log.LogErr($"Unhandled Score Saber FeedType {FeedType}");
                        return "Sync";
                }
            }
        }

        private ScoreSaberFeeds _feedType;

        public ScoreSaberFeeds FeedType
        {
            get
            {
                return _feedType;
            }
            set
            {
                bool changed = _feedType != value;
                _feedType = value;
                if (changed)
                    PropChanged(nameof(FeedType));
            }
        }

        protected override IFeedReader FeedReader
        {
            get
            {
                return new ScoreSaberReader();
            }
        }

        protected override IFeedSettings FeedSettings
        {
            get
            {
                return new ScoreSaberFeedSettings((int)FeedType) { MaxSongs = MaxSongs };
            }
        }
    }
}