﻿#region Copyright (C) 2005-2013 Team MediaPortal

// Copyright (C) 2005-2013 Team MediaPortal
// http://www.team-mediaportal.com
// 
// MediaPortal is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MediaPortal is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MediaPortal. If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.Database;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Music;
using MediaPortal.Music.Database;
using MediaPortal.Profile;
using MediaPortal.Player;
using MediaPortal.Playlists;
using MediaPortal.TagReader;
using MediaPortal.LastFM;
using MediaPortal.Util;

namespace MediaPortal.ProcessPlugins.LastFMScrobbler
{

  [PluginIcons("ProcessPlugins.LastFMScrobbler.LastFMScrobbler.gif",
               "ProcessPlugins.LastFMScrobbler.LastFMScrobblerDisabled.gif")]
  public class LastFMScrobbler : IPlugin, ISetupForm
  {
    private static readonly Random Randomizer = new Random();
    private static bool _allowMultipleVersions = true;
    private static int _randomness = 100;
    private static bool _announceTracks;
    private static bool _scrobbleTracks;
    private static bool _avoidDuplicates;

    private static void LoadSettings()
    {
      using (var xmlreader = new MPSettings())
      {
        //TODO: Is randomness still required?
        _randomness = xmlreader.GetValueAsInt("lastfm:test", "randomness", 100);
        _allowMultipleVersions = xmlreader.GetValueAsBool("lastfm:test", "allowDiffVersions", true);
        _announceTracks = xmlreader.GetValueAsBool("lastfm:test", "announce", true);
        _scrobbleTracks = xmlreader.GetValueAsBool("lastfm:test", "scrobble", true);
        _avoidDuplicates = xmlreader.GetValueAsBool("lastfm:test", "avoidDuplicates", true);
      }
    }

    #region IPlugin members

    public void Start()
    {
      Log.Info("LastFMScrobbler: Starting");
      LoadSettings();
      g_Player.PlayBackStarted += OnPlayBackStarted;
      g_Player.PlayBackEnded += OnPlayBackEnded;
      g_Player.PlayBackChanged += OnPlayBackChanged;
      g_Player.PlayBackStopped += OnPlayBackStopped;
    }

    public void Stop()
    {
      Log.Info("LastFMScrobbler: Stopped");
    }

    #endregion

    #region ISetupForm methods

    /// <summary>
    ///   Determines whether this plugin can be enabled.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if this plugin can be enabled; otherwise, <c>false</c>.
    /// </returns>
    public bool CanEnable()
    {
      return true;
    }

    /// <summary>
    ///   Determines whether this plugin has setup.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if this plugin has setup; otherwise, <c>false</c>.
    /// </returns>
    public bool HasSetup()
    {
      return true;
    }

    /// <summary>
    ///   Gets the plugin name.
    /// </summary>
    /// <returns>The plugin name.</returns>
    public string PluginName()
    {
      return "LastFMScrobbler";
    }

    /// <summary>
    ///   Defaults enabled.
    /// </summary>
    /// <returns>true if this plugin is enabled by default, otherwise false.</returns>
    public bool DefaultEnabled()
    {
      return true;
    }

    /// <summary>
    ///   Gets the window id.
    /// </summary>
    /// <returns>The window id.</returns>
    public int GetWindowId()
    {
      return 0;
    }

    /// <summary>
    ///   Gets the plugin author.
    /// </summary>
    /// <returns>The plugin author.</returns>
    public string Author()
    {
      return "Jameson_uk";
    }

    /// <summary>
    ///   Gets the description of the plugin.
    /// </summary>
    /// <returns>The plugin description.</returns>
    public string Description()
    {
      return
        "Test version of new Last.FM Scrobbler";
    }

    /// <summary>
    ///   Shows the plugin configuration.
    /// </summary>
    public void ShowPlugin()
    {
      var config = new LastFMConfig();
      config.Show();
    }

    /// <summary>
    ///   Gets the home screen details for the plugin.
    /// </summary>
    /// <param name = "strButtonText">The button text.</param>
    /// <param name = "strButtonImage">The button image.</param>
    /// <param name = "strButtonImageFocus">The button image focus.</param>
    /// <param name = "strPictureImage">The picture image.</param>
    /// <returns>true if the plugin can be seen, otherwise false.</returns>
    public bool GetHome(out string strButtonText, out string strButtonImage, out string strButtonImageFocus,
                        out string strPictureImage)
    {
      strButtonText = strButtonImage = strButtonImageFocus = strPictureImage = String.Empty;
      return false;
    }

    #endregion ISetupForm methods

    #region g_player events

    /// <summary>
    /// Handle event fired by MP player.  Something has started playing so check if it is music then
    /// announce track as now playing on last.fm website
    /// </summary>
    /// <param name="type">MediaType of item that has started</param>
    /// <param name="filename">filename of item that has started</param>
    private static void OnPlayBackStarted(g_Player.MediaType type, string filename)
    {
      if (type != g_Player.MediaType.Music || !(_announceTracks || MusicState.AutoDJEnabled)) return;

      var pl = PlayListPlayer.SingletonPlayer.GetPlaylist(PlayListPlayer.SingletonPlayer.CurrentPlaylistType);
      var plI = pl.First(plItem => plItem.FileName == filename);
      if (plI == null || plI.MusicTag == null)
      {
        Log.Info("LastFMScrobbler: Unable to process song: {0}  as it does not exist in the playlist", filename);
        return;
      }

      var currentSong = (MusicTag)plI.MusicTag;

      if (currentSong == null || string.IsNullOrEmpty(currentSong.Title) ||
          (string.IsNullOrEmpty(currentSong.Artist) && string.IsNullOrEmpty(currentSong.AlbumArtist)))
      {
        Log.Warn("LastFMScrobbler: No track & artist tags found - Unable to process: {0}", filename);
        return;
      }

      var artist = currentSong.Artist ?? currentSong.AlbumArtist;

      if (_announceTracks)
      {
        new Thread(
          () =>
          AnnounceTrack(artist, currentSong.Title, currentSong.Album,
                        currentSong.Duration.ToString(CultureInfo.InvariantCulture))) {Name = "Last.FM Announcer"}.Start();
      }

      if (MusicState.AutoDJEnabled)
      {
        new Thread(() => AutoDJ(artist, currentSong.Title)) { Name = "AutoDJ" }.Start();        
      }

    }

    /// <summary>
    /// Handle event fired by MP player.  Playback has ended; things have come naturally to the end 
    /// (eg. last track on album has finished and no more items in playlist)
    /// </summary>
    /// <param name="type">MediaType of item that has ended</param>
    /// <param name="filename">filename of item that has ended</param>
    private static void OnPlayBackEnded(g_Player.MediaType type, string filename)
    {
      if (type != g_Player.MediaType.Music)
      {
        return;
      }

      DoOnChangedOrStopped(9999, filename);
    }

    /// <summary>
    /// Handle event fired by MP player.
    /// Playback has changed; this event signifies that the existing item has been changed
    /// this could be because that song has ended and playback has moved to next item in playlist
    /// or could be because user has skipped tracks
    /// </summary>
    /// <param name="type">MediaType of item that was playing</param>
    /// <param name="stoptime">Number of seconds item has played for when it stopped</param>
    /// <param name="filename">filename of item that was playing</param>
    private static void OnPlayBackChanged(g_Player.MediaType type, int stoptime, string filename)
    {
      if (type != g_Player.MediaType.Music)
      {
        return;
      }

      DoOnChangedOrStopped(stoptime, filename);
    }

    /// <summary>
    /// Handle event fired by MP player.
    /// Playback has stopped; user has pressed stop mid way through a track
    /// </summary>
    /// <param name="type">MediaType of track that was stopped</param>
    /// <param name="stoptime">Number of seconds item has played for before it was stopped</param>
    /// <param name="filename">filename of item that was stopped</param>
    private static void OnPlayBackStopped(g_Player.MediaType type, int stoptime, string filename)
    {
      if (type != g_Player.MediaType.Music)
      {
        return;
      }

      DoOnChangedOrStopped(stoptime, filename);
    }

    #endregion

    #region Background Threads

    /// <summary>
    /// Announce now playing details on last.fm website
    /// </summary>
    /// <param name="artist">artist of track being played</param>
    /// <param name="track">name of track being played</param>
    /// <param name="album">album track being played is part of</param>
    /// <param name="duration">duration of track being played</param>
    private static void AnnounceTrack(string artist, string track, string album, string duration)
    {
      try
      {
        LastFMLibrary.UpdateNowPlaying(artist, track, album, duration);
        Log.Info("Submitted last.fm now playing update for: {0} - {1}", artist, track);
      }
      catch (LastFMException ex)
      {
        if (ex.LastFMError != LastFMException.LastFMErrorCode.UnknownError)
        {
          Log.Error("Last.fm error when announcing now playing track: {0} - {1}", artist, track);
          Log.Error(ex.Message);
        }
        else
        {
          Log.Error("Exception when updating now playing track on last.fm");
          Log.Error(ex.InnerException ?? ex);
        }
      }
      catch (Exception ex)
      {
        Log.Error("Last.fm error when announcing now playing track: {0} - {1}", artist, track);
        Log.Error(ex);
      }
    }

    /// <summary>
    /// Call backgound worker for scrobbling when track ends
    /// whether this is due to user skipping, user stopping or playback naturally coming to an end
    /// </summary>
    /// <param name="stoptime">Number of seconds through track was when it was stopped</param>
    /// <param name="filename">filename of track that was stopped</param>
    private static void DoOnChangedOrStopped(int stoptime, string filename)
    {
      // only scrobble if enabled
      if (!_scrobbleTracks) return;

      new Thread(() => ScrobbleTrack(filename, stoptime)) {Name = "Last.fm Scrobbler"}.Start();
    }

    /// <summary>
    /// Scrobble track to last.fm
    /// </summary>
    /// <param name="filename">filename to scrobble</param>
    /// <param name="stoptime">how long song had played for</param>
    private static void ScrobbleTrack(string filename, int stoptime)
    {
      // get song details and if appropriate scrobble to last.fm

      var pl = PlayListPlayer.SingletonPlayer.GetPlaylist(PlayListPlayer.SingletonPlayer.CurrentPlaylistType);
      var plI = pl.FirstOrDefault(plItem => plItem.FileName == filename);
      if (plI == null || plI.MusicTag == null)
      {
        Log.Info("Unable to scrobble song: {0}  as it does not exist in the playlist");
        return;
      }

      var currentSong = (MusicTag)plI.MusicTag;

      if (currentSong == null)
      {
        Log.Info("Unable to scrobble: {0}", filename);
        Log.Info("No tags found");
        return;
      }

      if (string.IsNullOrEmpty(currentSong.Title))
      {
        Log.Info("Unable to scrobble: {0}", filename);
        Log.Info("No title for track");
        return;
      }

      var artist = currentSong.Artist;
      if (string.IsNullOrEmpty(artist))
      {
        if (string.IsNullOrEmpty(currentSong.AlbumArtist))
        {
          Log.Info("Unable to scrobble: {0}", filename);
          Log.Info("No artist or album artist found");
          return;
        }
        artist = currentSong.AlbumArtist;
      }

      if (currentSong.Duration < 30)
      { // last.fm say not to scrobble songs that last less than 30 seconds
        return;
      }
      if (stoptime < 240 && stoptime < (currentSong.Duration / 2))
      { // last.fm say only to scrobble is more than 4 minutes has been listned to or 
        // at least hald the duration of the song
        return;
      }

      if (!Win32API.IsConnectedToInternet())
      {
        LastFMLibrary.CacheScrobble(artist, currentSong.Title, currentSong.Album, true, DateTime.UtcNow);
        Log.Info("No internet connection so unable to scrobble: {0} - {1}", currentSong.Title, artist);
        Log.Info("Scrobble has been cached");
        return;
      }

      try
      {
        LastFMLibrary.Scrobble(artist, currentSong.Title, currentSong.Album);
        Log.Info("Last.fm scrobble: {0} - {1}", currentSong.Title, artist);
      }
      catch (LastFMException ex)
      {
        if (ex.LastFMError == LastFMException.LastFMErrorCode.ServiceOffline ||
            ex.LastFMError == LastFMException.LastFMErrorCode.ServiceUnavailable)
        {
          LastFMLibrary.CacheScrobble(artist, currentSong.Title,currentSong.Album,true,DateTime.UtcNow);
          Log.Info("Unable to scrobble: {0} - {1}", currentSong.Title, artist);
          Log.Info("Scrobble has been cached");
        }
        else
        {
          Log.Error("Unable to scrobble: {0} - {1}", currentSong.Title, artist);
          Log.Error(ex);
        }
      }
      
    }

    #endregion

    #region playlist / AutoDJ

    /// <summary>
    /// Checks if an item with the same file path already exists in the current playlist
    /// </summary>
    /// <param name="fileName">full file path of item to be checked</param>
    /// <returns>True is file exists in current playlist</returns>
    public static bool InPlaylist(string fileName)
    {
      var pl = PlayListPlayer.SingletonPlayer.GetPlaylist(PlayListPlayer.SingletonPlayer.CurrentPlaylistType);
      return pl.Any(plItem => plItem.FileName == fileName);
    }

    /// <summary>
    /// Handles AutoDJ details of identifying similar tracks and choosing which to add to playlist
    /// </summary>
    /// <param name="artist">Artist of track</param>
    /// <param name="track">Name of track</param>
    public static void AutoDJ(string artist, string track)
    {
      // try and match similar track to one being played
      var tracks = GetSimilarTracks(artist, track);
      if (LocalTracksAdded(tracks))
      {
        return;
      }

      // no match so lets attempt to lookup similar tracks based on artists top tracks
      Log.Debug("Unable to match similar tracks for {0} - {1} : trying similar tracks for top artist tracks", artist, track);
      tracks = GetArtistTopTracks(artist);
      var i = 0;
      if (tracks != null)
      {
        foreach (var lastFmTrack in tracks)
        {
          if (i == 5)
          { // only check at most 5 tracks to prevent flooding last.fm with requests
            break;
          }

          tracks = GetSimilarTracks(lastFmTrack.ArtistName, lastFmTrack.TrackTitle);
          if (LocalTracksAdded(tracks))
          {
            return;
          }

          i++;
        }
      }

      // still no match so lets just try and match top artist tracks
      Log.Debug("Unable to match similar top artist tracks for {0} : trying top artist tracks", artist);
      tracks = GetArtistTopTracks(artist);
      if (LocalTracksAdded(tracks))
      {
        return;
      }
      
      Log.Info("Auto DJ: Unable to match any tracks for {0} - {1}", artist, track);
    }


    /// <summary>
    /// Checks last.fm for similar tracks and handle any errors that might occur
    /// </summary>
    /// <param name="artist">Artist to check</param>
    /// <param name="track">Track title to check</param>
    /// <returns>Similar tracks as defined by last.fm or null</returns>
    private static IEnumerable<LastFMSimilarTrack> GetSimilarTracks(string artist, string track)
    {
      IEnumerable<LastFMSimilarTrack> tracks;
      try
      {
        tracks = LastFMLibrary.GetSimilarTracks(track, artist);
      }
      catch (LastFMException ex)
      {
        if (ex.LastFMError == LastFMException.LastFMErrorCode.InvalidParameters)
        {
          Log.Debug("AutoDJ: Unable to get similar track for : {0} - {1}", artist, track);
          Log.Debug("AutoDJ: {0}", ex.Message);
        }
        else
        {
          Log.Error("Error in Last.fm AutoDJ - getting similar tracks");
          Log.Error(ex);
        }
        return null;
      }
      catch (Exception ex)
      {
        Log.Error("Error in Last.fm AutoDJ - getting similar tracks");
        Log.Error(ex);
        return null;
      }

      return tracks;
    }

    /// <summary>
    /// Checks last.fm for top artist tracks and handle any errors that might occur 
    /// </summary>
    /// <param name="artist">Artist to search for</param>
    /// <returns>Similar tracks as defined by last.fm or null</returns>
    private static IEnumerable<LastFMSimilarTrack> GetArtistTopTracks(string artist)
    {
      IEnumerable<LastFMSimilarTrack> tracks;
      try
      {
        tracks = LastFMLibrary.GetArtistTopTracks(artist);
      }
      catch (LastFMException ex)
      {
        if (ex.LastFMError == LastFMException.LastFMErrorCode.InvalidParameters &&
            ex.Message == "The artist you supplied could not be found")
        {
          Log.Debug("AutoDJ: Last.fm does not recognise artist: {0}", artist);
        }
        else
        {
          Log.Error("Error in Last.fm AutoDJ - getting artist top tracks");
          Log.Error(ex);
        }
        return null;
      }
      catch (Exception ex)
      {
        Log.Error("Error in Last.fm AutoDJ - getting artist top tracks");
        Log.Error(ex);
        return null;
      }

      return tracks;
    }

    /// <summary>
    /// Checks a collection of tracks from last.fm against the users local database
    /// If tracks are matched then these tracks will be considered by auto DJ
    /// </summary>
    /// <param name="tracks">Collection of tracks to check for local copies</param>
    /// <returns>True if tracks were added to playlist else false</returns>
    private static bool LocalTracksAdded(IEnumerable<LastFMSimilarTrack> tracks)
    {
      if (tracks == null)
      {
        return false;
      }

      var dbTracks = GetSimilarTracksInDatabase(tracks);

      if (dbTracks.Count == 0)
      {
        return false;
      }

      AutoDJAddToPlaylist(dbTracks);
      return true;
    }

    /// <summary>
    /// Chooses a song from a list to add to the current playlist
    /// </summary>
    /// <param name="dbTracks">List of tracks to select from</param>
    public static void AutoDJAddToPlaylist(List<Song> dbTracks)
    {
      //Pick one of the available tracks to add to playlist
      var pl = PlayListPlayer.SingletonPlayer.GetPlaylist(PlayListPlayer.SingletonPlayer.CurrentPlaylistType);
      var currentTrackIndex = PlayListPlayer.SingletonPlayer.CurrentSong;
      var plRemainingTracks = pl.Count - currentTrackIndex - 1;
      var numTracksWanted = 1;  // by default just add a single track
      if (plRemainingTracks < 5)
      {
        // less than five more tracks in playlist so try and build up a reserve
        numTracksWanted = 5 - plRemainingTracks;
      }

      // might not actually be five tracks available in the database
      var numTracksToAdd = Math.Min(numTracksWanted, dbTracks.Count);

      Log.Info("Auto DJ: Matched {0} local songs.  Attempting to add {1} tracks", dbTracks.Count, numTracksToAdd);

      for (var i = 0; i < numTracksToAdd; i++)
      {
        var maxSize = Math.Min(dbTracks.Count, _randomness);
        var trackNo = Randomizer.Next(0, maxSize);
        var pli = dbTracks[trackNo].ToPlayListItem();
        pli.Source = PlayListItem.PlayListItemSource.Recommendation;
        pli.SourceDescription = "LastFM:AutoDJ";
        pl.Add(pli);
        Log.Info("Auto DJ: Added to playlist: {0} - {1}", dbTracks[trackNo].Artist, dbTracks[trackNo].Title);
        dbTracks.RemoveAt(trackNo);  // remove song after adding to playlist to prevent the same sone being added twice
      }
    }

    /// <summary>
    /// Takes a list of tracks supplied by last.fm and matches them to tracks in the database
    /// </summary>
    /// <param name="tracks">List of last FM tracks to check</param>
    /// <returns>List of matched songs from input that exist in the users database</returns>
    private static List<Song> GetSimilarTracksInDatabase(IEnumerable<LastFMSimilarTrack> tracks)
    {
      // list contains songs which exist in users collection
      var dbTrackListing = new List<Song>();

      //identify which are available in users collection (ie. we can use they for auto DJ mode)
      foreach (var strSql in tracks.Select(track => String.Format("select * from tracks where strartist like '%| {0} |%' and strTitle = '{1}'",
                                                                  DatabaseUtility.RemoveInvalidChars(track.ArtistName),
                                                                  DatabaseUtility.RemoveInvalidChars(track.TrackTitle))))
      {
        List<Song> trackListing;
        MusicDatabase.Instance.GetSongsBySQL(strSql, out trackListing);

        dbTrackListing.AddRange(trackListing);
      }

      // only add track that already exists in playlist if there is no alternative
      if (_avoidDuplicates && dbTrackListing.Count(track => !InPlaylist(track.FileName)) > 1)
      {
        dbTrackListing = dbTrackListing.Where(track => !InPlaylist(track.FileName)).ToList();
      }

      return _allowMultipleVersions ? dbTrackListing : dbTrackListing.GroupBy(t => new {t.Artist, t.Title}).Select(y => y.First()).ToList();
    }

    #endregion
  }
}
